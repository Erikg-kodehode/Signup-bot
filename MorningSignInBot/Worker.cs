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
using PublicHoliday;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord.Net;
using System.IO; // Added for File operations

namespace MorningSignInBot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordSocketClient _client;
        private readonly IOptions<DiscordSettings> _settingsOptions;
        private readonly InteractionService _interactionService;
        private readonly IServiceProvider _services;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly INotificationService _notificationService;
        private readonly NorwayPublicHoliday _norwayCalendar;
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
            _settingsOptions = settings;
            _interactionService = interactionService;
            _services = services;
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
            _norwayCalendar = new NorwayPublicHoliday();
        }

        private async Task<string?> TryReadDockerSecretAsync()
        {
            const string secretPath = "/run/secrets/discord_bot_token";
            if (File.Exists(secretPath)) // CS0103 resolved by using System.IO;
            {
                try
                {
                    _logger.LogInformation("Reading bot token from Docker secret");
                    string token = await File.ReadAllTextAsync(secretPath); // CS0103 resolved by using System.IO;
                    return token.Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read bot token from Docker secret");
                }
            }
            else
            {
                _logger.LogDebug("Docker secret file does not exist: {SecretPath}", secretPath);
            }
            return null;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting.");
            _client.Log += LogAsync;
            _client.Ready += OnReadyAsync;
            _client.InteractionCreated += HandleInteractionAsync;

            string? botToken = await TryReadDockerSecretAsync();

            if (string.IsNullOrWhiteSpace(botToken))
            {
                _logger.LogInformation("No Docker secret found, checking configuration/environment");
                var currentSettings = _settingsOptions.Value;
                botToken = currentSettings.BotToken;
            }

            if (string.IsNullOrWhiteSpace(botToken))
            {
                _logger.LogWarning("Bot token is missing from secrets and configuration/environment.");
                Console.WriteLine("!!!!!! BOT TOKEN MISSING !!!!!!");
                Console.Write("Please paste your Discord Bot Token and press Enter: ");
                botToken = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(botToken))
                {
                    _logger.LogCritical("No token provided via input. Exiting...");
                    throw new InvalidOperationException("Bot token was not provided.");
                }
                _logger.LogInformation("Bot token received via console input.");
            }

            if (string.IsNullOrWhiteSpace(botToken))
            {
                _logger.LogCritical("BOT TOKEN IS MISSING. Cannot start.");
                throw new ArgumentNullException(nameof(botToken), "Bot token cannot be empty.");
            }

            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker service is running. Discord client state: {State}", _client.ConnectionState);
            Console.WriteLine($"---> Worker service is running. Discord client state: {_client.ConnectionState}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Worker running. Discord client state: {State}", _client.ConnectionState);
                    Console.WriteLine($"---> Worker running. Discord client state: {_client.ConnectionState} at {DateTime.Now.ToString("HH:mm:ss")}");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in worker execution loop");
                    Console.WriteLine($"---> ERROR in worker execution loop: {ex.Message}");
                }
            }

            _logger.LogInformation("Worker service is stopping");
            Console.WriteLine("---> Worker service is stopping");
        }

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
            _logger.LogInformation("Discord client is ready. Starting command registration process...");
            try
            {
                var firstGuildConfig = _settingsOptions.Value.Guilds.FirstOrDefault();
                if (firstGuildConfig == null || firstGuildConfig.GuildId == 0)
                {
                    _logger.LogError("No valid Guild ID found in configuration (Discord:Guilds). Cannot register commands.");
                    return;
                }

                ulong guildId = firstGuildConfig.GuildId;
                var guild = _client.GetGuild(guildId);

                int retries = 0;
                while (guild == null && retries < 5)
                {
                    _logger.LogWarning("Guild {GuildId} not found, retrying in 5 seconds...", guildId);
                    await Task.Delay(5000);
                    guild = _client.GetGuild(guildId);
                    retries++;
                }

                if (guild == null)
                {
                    _logger.LogError("Could not find guild with ID {GuildId} specified in configuration after retries.", guildId);
                    return;
                }

                await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                _logger.LogInformation("Added interaction modules to InteractionService.");

#if DEBUG
                _logger.LogWarning("Registering commands to Guild ID: {GuildId} (DEBUG MODE)", guildId);
                await _interactionService.RegisterCommandsToGuildAsync(guildId, true);
#else
                _logger.LogInformation("Registering commands Globally.");
                await _interactionService.RegisterCommandsGloballyAsync(true);
#endif

                _logger.LogInformation("Command registration process completed for Guild ID: {GuildId}", guildId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed during Ready event tasks (Module/Command Registration)");
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
                }

                var ctx = new SocketInteractionContext(_client, interaction);
                var result = await _interactionService.ExecuteCommandAsync(ctx, _services);

                if (!result.IsSuccess)
                {
                    _logger.LogError("Error executing interaction command: {ErrorReason} ({Command})", result.ErrorReason, interaction.Type);
                    if (!interaction.HasResponded)
                    {
                        try
                        {
                            await interaction.RespondAsync("Beklager, en feil oppstod under kjøring av kommandoen.", ephemeral: true);
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to send error response for interaction {Id}", interaction.Id); }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during interaction handling (ID: {InteractionId})", interaction.Id);
                if (interaction.Type == InteractionType.ApplicationCommand || interaction.Type == InteractionType.MessageComponent)
                {
                    try
                    {
                        var errorMsg = "Beklager, en uventet feil oppstod.";
                        if (!interaction.HasResponded) await interaction.RespondAsync(errorMsg, ephemeral: true);
                        else await interaction.FollowupAsync(errorMsg, ephemeral: true);
                    }
                    catch (Exception followupEx) { _logger.LogError(followupEx, "Failed to send error followup for interaction (ID: {InteractionId})", interaction.Id); }
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

                    bool alreadySignedIn = await dbContext.SignIns
                        .AnyAsync(s => s.UserId == interaction.User.Id && s.Timestamp >= startOfDayUtc);

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
                try
                {
                    if (interaction.HasResponded)
                        await interaction.FollowupAsync("Beklager, en feil oppstod under lagring av innsjekking.", ephemeral: true);
                    else
                        await interaction.RespondAsync("Beklager, en feil oppstod under lagring av innsjekking.", ephemeral: true);
                }
                catch (Exception followupEx)
                {
                    _logger.LogError(followupEx, "Failed to send error followup after sign-in save failure for {UserId}.", interaction.User.Id);
                }
            }
        }

        private void ScheduleNextSignInMessage()
        {
            var currentSettings = _settingsOptions.Value;
            DateTime now = DateTime.Now;
            DateTime nextRunTime = new DateTime(now.Year, now.Month, now.Day, currentSettings.SignInHour, currentSettings.SignInMinute, 0);

            if (now > nextRunTime)
            {
                nextRunTime = nextRunTime.AddDays(1);
            }

            _logger.LogDebug("Initial calculated next run time (Local): {RunTime}", nextRunTime);

            while (true)
            {
                bool isWeekend = nextRunTime.DayOfWeek == DayOfWeek.Saturday || nextRunTime.DayOfWeek == DayOfWeek.Sunday;
                bool isHoliday = _norwayCalendar.IsPublicHoliday(nextRunTime);

                if (!isWeekend && !isHoliday)
                {
                    break;
                }

                string reason = isWeekend ? "Weekend" : "Public Holiday";
                _logger.LogTrace("Skipping date {SkipDate:yyyy-MM-dd} ({DayOfWeek}) - Reason: {Reason}", nextRunTime, nextRunTime.DayOfWeek, reason);

                nextRunTime = nextRunTime.AddDays(1);
            }

            TimeSpan delay = nextRunTime - now;

            if (delay < TimeSpan.Zero)
            {
                _logger.LogWarning("Calculated negative delay ({Delay}). Running check in 10 seconds.", delay);
                delay = TimeSpan.FromSeconds(10);
            }

            _logger.LogInformation("Scheduling next sign-in message check for: {RunTime:yyyy-MM-dd HH:mm:ss} (Local Time) (in {Delay})", nextRunTime, delay);

            _timer?.Dispose();
            _timer = new System.Threading.Timer(async _ => await TimerTickAsync(), null, delay, Timeout.InfiniteTimeSpan);
        }

        private async Task TimerTickAsync()
        {
            _logger.LogDebug("Timer triggered for daily message check.");
            try
            {
                if (_client.ConnectionState == ConnectionState.Connected)
                {
                    DateTime now = DateTime.Now;
                    bool isWeekend = now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday;
                    bool isHoliday = _norwayCalendar.IsPublicHoliday(now);

                    if (!isWeekend && !isHoliday)
                    {
                        await _notificationService.SendDailySignInAsync();
                    }
                    else
                    {
                        _logger.LogInformation("Timer ticked, but today ({Date}) is a weekend or holiday. Skipping message send.", now.ToString("yyyy-MM-dd"));
                    }
                }
                else
                {
                    _logger.LogWarning("Timer ticked but Discord client was not connected. Skipping message send.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing scheduled task (TimerTickAsync) via NotificationService.");
            }
            finally
            {
                ScheduleNextSignInMessage();
            }
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