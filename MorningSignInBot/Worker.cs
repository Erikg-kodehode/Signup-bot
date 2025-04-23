using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using MorningSignInBot.Data;
using MorningSignInBot.Services;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MorningSignInBot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordSocketClient _client;
        private readonly DiscordSettings _settings;
        private readonly InteractionService _interactionService;
        private readonly IServiceProvider _services;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly INotificationService _notificationService;
        private System.Threading.Timer? _timer;

        private const string SignInButtonKontorId = "daily_signin_kontor";
        private const string SignInButtonHjemmeId = "daily_signin_hjemme";

        public Worker(
            ILogger<Worker> logger,
            DiscordSocketClient client,
            IOptions<DiscordSettings> settings,
            InteractionService interactionService,
            IServiceProvider services,
            IServiceScopeFactory scopeFactory,
            INotificationService notificationService)
        {
            _logger = logger;
            _client = client;
            _settings = settings.Value;
            _interactionService = interactionService;
            _services = services;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting.");

            _client.Log += LogAsync;
            _client.Ready += OnReadyAsync;
            _client.InteractionCreated += HandleInteractionAsync;

            try
            {
                await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                _logger.LogInformation("Interaction modules loaded.");
            }
            catch (Exception ex) { _logger.LogError(ex, "Error loading interaction modules."); }

            await _client.LoginAsync(TokenType.Bot, _settings.BotToken);
            await _client.StartAsync();

            await base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            _timer?.Dispose();

            if (_client != null)
            {
                _client.Log -= LogAsync;
                _client.Ready -= OnReadyAsync;
                _client.InteractionCreated -= HandleInteractionAsync;
                try { await _client.StopAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Exception during client stop."); }
                try { await _client.LogoutAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Exception during client logout."); }
            }
            await base.StopAsync(cancellationToken);
        }

        private async Task OnReadyAsync()
        {
            _logger.LogInformation("Discord client is ready. Registering commands...");

            try
            {
                ulong testGuildId = 1364185117182005308; // <-- REPLACE 0 WITH YOUR ACTUAL TEST SERVER/GUILD ID!
                if (testGuildId != 0)
                {
                    await _interactionService.RegisterCommandsToGuildAsync(testGuildId, true);
                    _logger.LogInformation("Registered commands to Guild ID: {GuildId}", testGuildId);
                }
                else
                {
                    _logger.LogWarning("Test Guild ID not set. Attempting global command registration (may take up to an hour).");
                    await _interactionService.RegisterCommandsGloballyAsync(true);
                    _logger.LogInformation("Attempting global command registration.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register interaction commands.");
            }

            ScheduleNextSignInMessage();
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketMessageComponent componentInteraction)
                {
                    string customId = componentInteraction.Data.CustomId;
                    if (customId == SignInButtonKontorId || customId == SignInButtonHjemmeId)
                    {
                        await HandleSignInButton(componentInteraction);
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("Unhandled button CustomId: {CustomId}", customId);
                        try { if (!componentInteraction.HasResponded) await componentInteraction.DeferAsync(ephemeral: true); } catch { }
                        return;
                    }
                }

                var ctx = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling interaction.");
                if (interaction.Type == InteractionType.ApplicationCommand || interaction.Type == InteractionType.MessageComponent)
                {
                    try
                    {
                        var errorMsg = "En feil oppstod.";
                        if (!interaction.HasResponded) await interaction.RespondAsync(errorMsg, ephemeral: true);
                        else await interaction.FollowupAsync(errorMsg, ephemeral: true);
                    }
                    catch (Exception followupEx) { _logger.LogError(followupEx, "Failed to send error followup."); }
                }
            }
        }

        private async Task HandleSignInButton(SocketMessageComponent interaction)
        {
            string signInType = interaction.Data.CustomId == SignInButtonKontorId ? "Kontor" : "Hjemmekontor";
            string responseMessage = $"Du er nå logget inn ({signInType})!";

            try
            {
                await interaction.DeferAsync(ephemeral: true);

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    DateTime startOfDayUtc = DateTime.UtcNow.Date;
                    bool alreadySignedIn = await dbContext.SignIns.AnyAsync(s =>
                        s.UserId == interaction.User.Id && s.Timestamp >= startOfDayUtc);

                    if (alreadySignedIn)
                    {
                        _logger.LogWarning("User {User} ({UserId}) tried to sign in again today.", interaction.User.Username, interaction.User.Id);
                        await interaction.FollowupAsync("Du har allerede logget inn i dag.", ephemeral: true);
                        return;
                    }

                    var entry = new SignInEntry(
                        userId: interaction.User.Id,
                        username: interaction.User.GlobalName ?? interaction.User.Username,
                        timestamp: DateTime.UtcNow,
                        signInType: signInType
                    );

                    dbContext.SignIns.Add(entry);
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("User {User} ({UserId}) signed in as {SignInType}.", entry.Username, entry.UserId, signInType);
                    await interaction.FollowupAsync(responseMessage, ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sign-in for User {User} ({UserId}), Type {SignInType}.", interaction.User.Username, interaction.User.Id, signInType);
                try { await interaction.FollowupAsync("Feil ved lagring av innsjekking.", ephemeral: true); } catch { }
            }
        }

        private void ScheduleNextSignInMessage()
        {
            DateTime now = DateTime.Now;
            DateTime nextRunTime = new DateTime(now.Year, now.Month, now.Day, _settings.SignInHour, _settings.SignInMinute, 0);

            if (now > nextRunTime) { nextRunTime = nextRunTime.AddDays(1); }
            while (nextRunTime.DayOfWeek == DayOfWeek.Saturday || nextRunTime.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogTrace("Skipping weekend day: {WeekendDay}", nextRunTime.DayOfWeek);
                nextRunTime = nextRunTime.AddDays(1);
            }

            TimeSpan delay = nextRunTime - now;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.FromMinutes(1);
                _logger.LogWarning("Calculated negative delay after weekend check, using fallback.");
            }

            _logger.LogInformation("Scheduling next sign-in message check for: {RunTime} (in {Delay})", nextRunTime, delay);

            _timer?.Dispose();
            _timer = new System.Threading.Timer(
               callback: async _ => await TimerTickAsync(),
               state: null,
               dueTime: delay,
               period: Timeout.InfiniteTimeSpan);
        }

        private async Task TimerTickAsync()
        {
            _logger.LogDebug("Timer triggered for daily message check.");
            try
            {
                if (_client.ConnectionState == ConnectionState.Connected)
                {
                    await _notificationService.SendDailySignInAsync();
                }
                else
                {
                    _logger.LogWarning("Timer ticked but client was not connected. Skipping send.");
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error executing scheduled task via NotificationService."); }
            finally { ScheduleNextSignInMessage(); }
        }

        private Task LogAsync(LogMessage log)
        {
            var severity = log.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };
            _logger.Log(severity, log.Exception, "[Discord {Source}] {Message}", log.Source, log.Message);
            return Task.CompletedTask;
        }
    }
}