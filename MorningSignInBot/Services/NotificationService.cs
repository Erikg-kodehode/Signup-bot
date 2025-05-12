using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using System;
using System.IO; // Required for file operations
using System.Text.Json; // Required for JSON
using System.Threading.Tasks;

namespace MorningSignInBot.Services
{
    // Simple class to hold the state we need to save
    internal class BotState
    {
        public ulong LastMessageId { get; set; }
        public ulong LastChannelId { get; set; }
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly DiscordSocketClient _client;
        private readonly DiscordSettings _settings;
        private readonly string _stateFilePath; // Path to save the last message ID

        private const string SignInButtonKontorId = "daily_signin_kontor";
        private const string SignInButtonHjemmeId = "daily_signin_hjemme";

        public NotificationService(
            ILogger<NotificationService> logger,
            DiscordSocketClient client,
            IOptions<DiscordSettings> settings)
        {
            _logger = logger;
            _client = client;
            _settings = settings.Value;
            // Store state file in the application's base directory
            _stateFilePath = Path.Combine(AppContext.BaseDirectory, "bot_message_state.json");
        }

        // --- New Method: Delete Previous Message ---
        public async Task DeletePreviousMessageAsync()
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("State file not found, no previous message to delete.");
                return;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_stateFilePath);
                BotState? previousState = JsonSerializer.Deserialize<BotState>(json);

                if (previousState?.LastMessageId > 0 && previousState?.LastChannelId > 0)
                {
                    _logger.LogInformation("Attempting to delete previous message {MessageId} in channel {ChannelId}", previousState.LastMessageId, previousState.LastChannelId);

                    if (await _client.GetChannelAsync(previousState.LastChannelId) is ITextChannel channel)
                    {
                        // Get the message - optional, can just try deleting
                        // IMessage? messageToDelete = await channel.GetMessageAsync(previousState.LastMessageId);
                        // if (messageToDelete != null) { ... }

                        // Attempt deletion directly
                        await channel.DeleteMessageAsync(previousState.LastMessageId);
                        _logger.LogInformation("Successfully deleted previous message {MessageId}", previousState.LastMessageId);

                        // Optionally delete the state file after successful deletion,
                        // or just let it be overwritten when the new message is sent.
                        // File.Delete(_stateFilePath);
                    }
                    else
                    {
                        _logger.LogWarning("Could not find channel {ChannelId} to delete previous message.", previousState.LastChannelId);
                    }
                }
                else
                {
                    _logger.LogWarning("State file did not contain valid previous message/channel IDs.");
                }
            }
            catch (Discord.Net.HttpException discordEx) when (discordEx.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Previous message was likely already deleted (404 Not Found).");
                // Optionally delete the state file here too if message is gone
                // try { File.Delete(_stateFilePath); } catch { }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading state file or deleting previous message.");
                // Don't delete state file on error, maybe we can retry next time
            }
            // We always proceed to send the new message regardless of deletion success/failure
        }

        // --- New Method: Save Message State ---
        private async Task SaveMessageStateAsync(IMessage message)
        {
            if (message == null) return;

            var currentState = new BotState
            {
                LastMessageId = message.Id,
                LastChannelId = message.Channel.Id
            };

            try
            {
                string json = JsonSerializer.Serialize(currentState, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_stateFilePath, json);
                _logger.LogInformation("Saved state for message {MessageId} in channel {ChannelId}", currentState.LastMessageId, currentState.LastChannelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save message state to file {FilePath}", _stateFilePath);
            }
        }

        // --- Modified Method: SendDailySignInAsync ---
        public async Task SendDailySignInAsync()
        {
            // --- Attempt to delete previous day's message FIRST ---
            await DeletePreviousMessageAsync();
            // --------------------------------------------------------

            _logger.LogDebug("Attempting to send daily sign-in message...");

            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Skipping message send: Weekend.");
                return;
            }

            IMessage? sentMessage = null; // Keep track of the sent message
            try
            {
                if (!ulong.TryParse(_settings.TargetChannelId, out ulong channelId))
                { _logger.LogError("Invalid TargetChannelId format: {ChannelId}", _settings.TargetChannelId); return; }

                if (_client.ConnectionState != ConnectionState.Connected)
                { _logger.LogWarning("Cannot send message: Discord client not connected."); return; }

                var channel = await _client.GetChannelAsync(channelId);
                if (channel is not ITextChannel targetChannel)
                { _logger.LogError("Target channel {ChannelId} not found or is not a text channel.", channelId); return; }

                var buttonKontor = new ButtonBuilder().WithLabel("Logg inn (Kontor)").WithCustomId(SignInButtonKontorId).WithStyle(ButtonStyle.Success).WithEmote(Emoji.Parse("🏢"));
                var buttonHjemme = new ButtonBuilder().WithLabel("Logg inn (Hjemmekontor)").WithCustomId(SignInButtonHjemmeId).WithStyle(ButtonStyle.Primary).WithEmote(Emoji.Parse("🏠"));
                var component = new ComponentBuilder().WithButton(buttonKontor).WithButton(buttonHjemme).Build();
                string messageText = $"God morgen! Vennligst logg inn for **{DateTime.Now:dddd, d. MMMM}** ved å bruke en av knappene under.";

                sentMessage = await targetChannel.SendMessageAsync(text: messageText, components: component); // Assign to variable

                if (sentMessage != null)
                {
                    _logger.LogInformation("Sign-in message sent successfully to channel {ChannelId} with ID {MessageId}", channelId, sentMessage.Id);
                    // --- Save the state of the NEWLY sent message ---
                    await SaveMessageStateAsync(sentMessage);
                    // -------------------------------------------------
                }
                else { _logger.LogWarning("Failed to send message to channel {ChannelId}. SendMessageAsync returned null.", channelId); }
            }
            catch (Discord.Net.HttpException discordEx) { _logger.LogError(discordEx, "Discord API error sending sign-in message. Code: {ErrorCode}, Reason: {Reason}", discordEx.HttpCode, discordEx.Reason); }
            catch (Exception ex) { _logger.LogError(ex, "An error occurred while sending the sign-in message."); }
        }
    }
}