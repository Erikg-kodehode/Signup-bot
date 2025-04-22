using Discord;
using Discord.Interactions; // Required for InteractionService
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore; // Required for constructor injection pattern if needed, though interaction handler uses scope factory now
using Microsoft.Extensions.DependencyInjection; // Required for IServiceScopeFactory
using Microsoft.Extensions.Options;
using System;
using System.Reflection; // Required for AddModulesAsync
using System.Threading;
using System.Threading.Tasks;

namespace MorningSignInBot
{
    // Configuration Class (remains the same)
    public class DiscordSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string TargetChannelId { get; set; } = string.Empty;
        public int SignInHour { get; set; } = 8;
        public int SignInMinute { get; set; } = 0;
    }

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordSocketClient _client;
        private readonly DiscordSettings _settings;
        private readonly InteractionService _interactionService; // For handling commands
        private readonly IServiceProvider _services; // For registering modules & handling interactions
        private readonly IServiceScopeFactory _scopeFactory; // For getting DbContext in interaction handler
        private System.Threading.Timer? _timer;

        // --- Define button IDs ---
        private const string SignInButtonKontorId = "daily_signin_kontor";
        private const string SignInButtonHjemmeId = "daily_signin_hjemme";

        public Worker(
            ILogger<Worker> logger,
            DiscordSocketClient client,
            IOptions<DiscordSettings> settings,
            InteractionService interactionService,
            IServiceProvider services, // Get the main service provider
            IServiceScopeFactory scopeFactory) // Get scope factory
        {
            _logger = logger;
            _client = client;
            _settings = settings.Value;
            _interactionService = interactionService;
            _services = services;
            _scopeFactory = scopeFactory;

            // Validation remains the same...
            if (string.IsNullOrWhiteSpace(_settings.BotToken))
                throw new ArgumentNullException(nameof(_settings.BotToken), "Bot token cannot be empty. Check appsettings.json.");
            if (string.IsNullOrWhiteSpace(_settings.TargetChannelId) || !ulong.TryParse(_settings.TargetChannelId, out _))
                throw new ArgumentException("TargetChannelId is invalid or missing. Check appsettings.json.", nameof(_settings.TargetChannelId));
            if (_settings.SignInHour < 0 || _settings.SignInHour > 23)
                throw new ArgumentOutOfRangeException(nameof(_settings.SignInHour), "SignInHour must be between 0 and 23.");
            if (_settings.SignInMinute < 0 || _settings.SignInMinute > 59)
                throw new ArgumentOutOfRangeException(nameof(_settings.SignInMinute), "SignInMinute must be between 0 and 59.");
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);

            _client.Log += LogAsync;
            _client.Ready += OnReadyAsync; // Hook into Ready event
            _client.InteractionCreated += HandleInteractionAsync; // Hook into Interaction event

            // Register interaction modules
            await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            await _client.LoginAsync(TokenType.Bot, _settings.BotToken);
            await _client.StartAsync();

            await base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExecuteAsync called. Bot is running in background.");
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker stopping at: {time}", DateTimeOffset.Now);
            _timer?.Change(Timeout.Infinite, 0);
            _timer?.Dispose();

            if (_client != null)
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
                _client.Log -= LogAsync;
                _client.Ready -= OnReadyAsync;
                _client.InteractionCreated -= HandleInteractionAsync;
            }
            await base.StopAsync(cancellationToken);
        }

        // --- Handles Bot Ready ---
        private async Task OnReadyAsync()
        {
            _logger.LogInformation("Discord client is ready!");

            // Register commands globally or to a specific guild
            // Using RegisterCommandsGloballyAsync can take up to an hour to propagate
            // Using RegisterCommandsToGuildAsync is instant for testing/development
            // Choose one method:

            // --- Option 1: Register Globally (takes time) ---
            // try {
            //     await _interactionService.RegisterCommandsGloballyAsync(true);
            //     _logger.LogInformation("Interaction commands registered globally.");
            // } catch (Exception ex) {
            //     _logger.LogError(ex, "Error registering global commands.");
            // }


            // --- Option 2: Register to a Specific Guild (instant for testing) ---
            ulong? testGuildId = null; // SET YOUR TEST SERVER ID HERE FOR INSTANT COMMAND UPDATES
            // Example: ulong? testGuildId = 123456789012345678;
            if (testGuildId.HasValue)
            {
                try
                {
                    await _interactionService.RegisterCommandsToGuildAsync(testGuildId.Value, true);
                    _logger.LogInformation("Interaction commands registered to test guild {GuildId}.", testGuildId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error registering commands to guild {GuildId}.", testGuildId.Value);
                }
            }
            else
            {
                _logger.LogWarning("No test guild ID set. Commands might take up to an hour to appear if registered globally or not registered.");
                // Fallback or decide if global registration is desired here if no test guild
                try
                {
                    await _interactionService.RegisterCommandsGloballyAsync(true);
                    _logger.LogInformation("Attempting to register interaction commands globally (may take up to an hour).");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error registering global commands.");
                }
            }


            // Schedule the first run of the daily message
            ScheduleNextSignInMessage();
        }


        // --- Handles Interactions (Commands and Buttons) ---
        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                // --- Handle Button Clicks Separately ---
                if (interaction is SocketMessageComponent componentInteraction)
                {
                    string customId = componentInteraction.Data.CustomId;
                    _logger.LogDebug("Button Interaction received: User {User} ({UserId}) | CustomId: {CustomId}",
                        componentInteraction.User.Username, componentInteraction.User.Id, customId);

                    // Check if it's one of our sign-in buttons
                    if (customId == SignInButtonKontorId || customId == SignInButtonHjemmeId)
                    {
                        await HandleSignInButton(componentInteraction); // Pass to specific handler
                        return; // Stop processing here for buttons
                    }
                    else
                    {
                        // Handle other potential button clicks if needed
                        _logger.LogWarning("Received interaction with unhandled Button CustomId: {CustomId} from User {User}", customId, componentInteraction.User.Username);
                        // Optional: Acknowledge to prevent "Interaction Failed"
                        try { if (!componentInteraction.HasResponded) await componentInteraction.DeferAsync(ephemeral: true); } catch { }
                        return;
                    }
                }


                // --- Handle Slash Commands via Interaction Service ---
                var ctx = new SocketInteractionContext(_client, interaction);
                // Execute the command using the InteractionService, injecting scoped services if necessary
                await _interactionService.ExecuteCommandAsync(ctx, _services);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling interaction.");
                // If interaction hasn't been responded to, inform user about the error
                if (interaction.Type == InteractionType.ApplicationCommand || interaction.Type == InteractionType.MessageComponent)
                {
                    try
                    {
                        if (!interaction.HasResponded)
                            await interaction.RespondAsync("En feil oppstod under behandling av kommandoen.", ephemeral: true);
                        else
                            await interaction.FollowupAsync("En feil oppstod under behandling av kommandoen.", ephemeral: true);
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogError(followupEx, "Failed to send error followup message.");
                    }
                }
            }
        }


        // --- Handler for Sign-In Button Clicks ---
        private async Task HandleSignInButton(SocketMessageComponent interaction)
        {
            string signInType = interaction.Data.CustomId switch
            {
                SignInButtonKontorId => "Kontor",
                SignInButtonHjemmeId => "Hjemmekontor",
                _ => "Ukjent" // Should not happen if called correctly
            };

            string responseMessage = signInType switch
            {
                "Kontor" => "Du er nå logget inn (Kontor)!",
                "Hjemmekontor" => "Du er nå logget inn (Hjemmekontor)!",
                _ => "Du er nå logget inn!"
            };


            try
            {
                // Acknowledge interaction quickly before DB operation
                await interaction.RespondAsync(responseMessage, ephemeral: true);

                // Create a scope to resolve DbContext - BEST PRACTICE for background services / event handlers
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();

                    // Check if user already signed in today (optional, prevents duplicates)
                    DateTime startOfDay = DateTime.Today;
                    bool alreadySignedIn = await dbContext.SignIns.AnyAsync(s =>
                        s.UserId == interaction.User.Id &&
                        s.Timestamp >= startOfDay);

                    if (alreadySignedIn)
                    {
                        _logger.LogWarning("User {User} ({UserId}) tried to sign in again today.", interaction.User.Username, interaction.User.Id);
                        await interaction.FollowupAsync("Du har allerede logget inn i dag.", ephemeral: true); // Inform user
                        return;
                    }


                    // Create and save the entry
                    var entry = new SignInEntry(
                        userId: interaction.User.Id,
                        username: interaction.User.Username, // Consider using GlobalName or DisplayName if preferred/available
                        timestamp: DateTime.UtcNow, // Use UTC for consistency
                        signInType: signInType
                    );

                    dbContext.SignIns.Add(entry);
                    await dbContext.SaveChangesAsync(); // Save to database

                    _logger.LogInformation("User {User} ({UserId}) signed in as {SignInType}.", interaction.User.Username, interaction.User.Id, signInType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sign-in for User {User} ({UserId}), Type {SignInType}.",
                    interaction.User.Username, interaction.User.Id, signInType);

                // Try to inform user of error if followup is possible
                try { await interaction.FollowupAsync("Beklager, en feil oppstod under lagring av innsjekkingen.", ephemeral: true); } catch { }
            }
        }


        // --- *** MODIFIED: Schedules the timer, SKIPPING WEEKENDS *** ---
        private void ScheduleNextSignInMessage()
        {
            DateTime now = DateTime.Now;
            DateTime nextRunTime = new DateTime(now.Year, now.Month, now.Day, _settings.SignInHour, _settings.SignInMinute, 0);

            // If the scheduled time today has already passed, start calculation from tomorrow
            if (now > nextRunTime)
            {
                nextRunTime = nextRunTime.AddDays(1);
            }

            // --- Skip Weekends ---
            while (nextRunTime.DayOfWeek == DayOfWeek.Saturday || nextRunTime.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Scheduled time {nextRunTime} is a weekend. Skipping to next day.", nextRunTime);
                nextRunTime = nextRunTime.AddDays(1);
            }
            // ---------------------

            TimeSpan delay = nextRunTime - now;

            // Ensure delay is positive (should be, but safety check)
            if (delay < TimeSpan.Zero)
            {
                _logger.LogError("Calculated negative delay. Scheduling for next valid cycle.");
                // Attempt recovery - schedule for the target time on the next *valid* day
                nextRunTime = new DateTime(now.Year, now.Month, now.Day, _settings.SignInHour, _settings.SignInMinute, 0).AddDays(1);
                while (nextRunTime.DayOfWeek == DayOfWeek.Saturday || nextRunTime.DayOfWeek == DayOfWeek.Sunday)
                {
                    nextRunTime = nextRunTime.AddDays(1);
                }
                delay = nextRunTime - now;
                if (delay < TimeSpan.Zero)
                { // If still negative, something's very wrong
                    delay = TimeSpan.FromMinutes(1); // Fallback to 1 minute
                    _logger.LogError("Delay calculation failed persistently. Using fallback delay.");
                }
            }

            _logger.LogInformation("Scheduling next sign-in message for: {runTime} (in {delay})", nextRunTime, delay);

            _timer?.Dispose();
            _timer = new System.Threading.Timer(
               callback: async _ => await SendSignInMessageAsync(),
               state: null,
               dueTime: delay,
               period: Timeout.InfiniteTimeSpan
           );
        }

        // --- *** MODIFIED: Sends message with TWO buttons *** ---
        private async Task SendSignInMessageAsync()
        {
            _logger.LogInformation("Timer elapsed. Attempting to send sign-in message...");

            // --- Add Check: Don't send if today is weekend (extra safety) ---
            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Today is a weekend. Skipping message send.");
                ScheduleNextSignInMessage(); // Reschedule immediately for Monday
                return;
            }
            // --------------------------------------------------------------------

            try
            {
                if (!ulong.TryParse(_settings.TargetChannelId, out ulong channelId)) { /* Error handling... */ return; }
                if (_client.GetChannel(channelId) is not ITextChannel targetChannel) { /* Error handling... */ return; }

                // --- Create Buttons (Norwegian Labels) ---
                var buttonKontor = new ButtonBuilder()
                   .WithLabel("Logg inn (Kontor)")
                   .WithCustomId(SignInButtonKontorId)
                   .WithStyle(ButtonStyle.Success) // Green
                   .WithEmote(Emoji.Parse("🏢")); // Office building emoji

                var buttonHjemme = new ButtonBuilder()
                    .WithLabel("Logg inn (Hjemmekontor)")
                    .WithCustomId(SignInButtonHjemmeId)
                    .WithStyle(ButtonStyle.Primary) // Blue
                    .WithEmote(Emoji.Parse("🏠")); // House emoji

                var component = new ComponentBuilder()
                   .WithButton(buttonKontor)
                   .WithButton(buttonHjemme)
                   .Build();

                // --- Send Message (Norwegian Text) ---
                string messageText = $"God morgen! Vennligst logg inn for **{DateTime.Now:dddd, d. MMMM}** ved å bruke en av knappene under.";
                var sentMessage = await targetChannel.SendMessageAsync(text: messageText, components: component);

                if (sentMessage != null)
                {
                    _logger.LogInformation("Sign-in message sent successfully to channel {channelId}", channelId);
                }
                else { /* Error handling... */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while sending the sign-in message.");
            }
            finally
            {
                ScheduleNextSignInMessage(); // Always reschedule for the next valid day
                _logger.LogInformation("Rescheduled timer.");
            }
        }


        // --- Logs Discord messages (remains the same) ---
        private Task LogAsync(LogMessage log)
        {
            // Simple console logger - consider using a more robust logging framework like Serilog later
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