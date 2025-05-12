using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MorningSignInBot.Services
{
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
        private readonly string _stateFilePath;

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
            _stateFilePath = Path.Combine(AppContext.BaseDirectory, "bot_message_state.json");
        }

        public async Task DeletePreviousMessageAsync()
        {
            _logger.LogDebug("Starting DeletePreviousMessageAsync...");
            
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("State file not found at path: {Path}", _stateFilePath);
                return;
            }

            try
            {
                _logger.LogDebug("Reading state file from: {Path}", _stateFilePath);
                string json = await File.ReadAllTextAsync(_stateFilePath);
                BotState? previousState = JsonSerializer.Deserialize<BotState>(json);

                if (previousState?.LastMessageId > 0 && previousState?.LastChannelId > 0)
                {
                    _logger.LogInformation("Found previous message state - MessageId: {MessageId}, ChannelId: {ChannelId}", 
                        previousState.LastMessageId, previousState.LastChannelId);

                    var channel = await _client.GetChannelAsync(previousState.LastChannelId) as ITextChannel;
                    if (channel == null)
                    {
                        _logger.LogWarning("Could not find channel {ChannelId} or it is not a text channel.", previousState.LastChannelId);
                        return;
                    }

                    // Check bot permissions
                    var botUser = await channel.Guild.GetCurrentUserAsync();
                    var permissions = botUser.GetPermissions(channel);
                    
                    if (!permissions.ManageMessages)
                    {
                        _logger.LogError("Bot lacks ManageMessages permission in channel {ChannelName} ({ChannelId})", 
                            channel.Name, channel.Id);
                        return;
                    }

                    try
                    {
                        // First try to fetch the message to confirm it exists
                        var messageToDelete = await channel.GetMessageAsync(previousState.LastMessageId);
                        if (messageToDelete == null)
                        {
                            _logger.LogWarning("Message {MessageId} no longer exists in channel {ChannelName}", 
                                previousState.LastMessageId, channel.Name);
                            File.Delete(_stateFilePath);
                            return;
                        }

                        // Attempt deletion
                        await channel.DeleteMessageAsync(previousState.LastMessageId);
                        _logger.LogInformation("Successfully deleted message {MessageId} from channel {ChannelName}", 
                            previousState.LastMessageId, channel.Name);

                        // Delete the state file since we succeeded
                        File.Delete(_stateFilePath);
                    }
                    catch (Discord.Net.HttpException discordEx)
                    {
                        _logger.LogError(discordEx, "Discord API error deleting message {MessageId}. Status: {Status}, Code: {Code}, Reason: {Reason}", 
                            previousState.LastMessageId, discordEx.HttpCode, discordEx.DiscordCode, discordEx.Reason);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid state file content: {Content}", json);
                    File.Delete(_stateFilePath);
                }
            }
            catch (Discord.Net.HttpException discordEx) when (discordEx.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Previous message was already deleted (404 Not Found).");
                File.Delete(_stateFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in DeletePreviousMessageAsync");
                // Leave state file for retry unless its corrupted
                if (ex is JsonException || ex is FileNotFoundException)
                {
                    try { File.Delete(_stateFilePath); } 
                    catch { /* ignore cleanup errors */ }
                }
            }
        }

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

        public async Task SendDailySignInAsync()
        {
            await DeletePreviousMessageAsync();

            _logger.LogDebug("Attempting to send daily sign-in message...");

            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Skipping message send: Weekend.");
                return;
            }

            IMessage? sentMessage = null;
            try
            {
                if (!ulong.TryParse(_settings.TargetChannelId, out ulong channelId))
                { _logger.LogError("Invalid TargetChannelId format: {ChannelId}", _settings.TargetChannelId); return; }

                if (_client.ConnectionState != ConnectionState.Connected)
                { _logger.LogWarning("Cannot send message: Discord client not connected."); return; }

                var channel = await _client.GetChannelAsync(channelId);
                if (channel is not ITextChannel targetChannel)
                { _logger.LogError("Target channel {ChannelId} not found or is not a text channel.", channelId); return; }

                // Create improved buttons with better visual styling
                var buttonKontor = new ButtonBuilder()
                    .WithLabel("Logg inn (Kontor)")
                    .WithCustomId(SignInButtonKontorId)
                    .WithStyle(ButtonStyle.Success)
                    .WithEmote(Emoji.Parse("🏢")); // Office building emoji

                var buttonHjemme = new ButtonBuilder()
                    .WithLabel("Logg inn (Hjemmekontor)")
                    .WithCustomId(SignInButtonHjemmeId)
                    .WithStyle(ButtonStyle.Primary)
                    .WithEmote(Emoji.Parse("🏡")); // Home emoji

                var component = new ComponentBuilder()
                    .WithButton(buttonKontor)
                    .WithButton(buttonHjemme)
                    .Build();

                // Create an appealing embed for the message
                var currentDate = DateTime.Now;
                var embed = new EmbedBuilder()
                    .WithTitle($"📋 Daglig Innsjekking: {currentDate:dddd, d. MMMM yyyy}")
                    .WithDescription("**God morgen!** 👋\n\nVennligst logg inn for dagens arbeidsdag ved å bruke en av knappene nedenfor.")
                    .WithColor(new Color(52, 152, 219)) // Nice blue color
                    .WithFooter(footer => {
                        footer.Text = "Innsjekking registreres automatisk i systemet";
                        footer.IconUrl = "https://cdn.discordapp.com/emojis/1042377292536643655.png"; // You can use your own icon URL here
                    })
                    .WithTimestamp(DateTimeOffset.Now);

                // Send message with embed and buttons
                sentMessage = await targetChannel.SendMessageAsync(
                    text: null, // No text content (using embed instead)
                    embed: embed.Build(),
                    components: component);

                if (sentMessage != null)
                {
                    _logger.LogInformation("Sign-in message sent successfully to channel {ChannelId} with ID {MessageId}", channelId, sentMessage.Id);
                    await SaveMessageStateAsync(sentMessage);
                }
                else { _logger.LogWarning("Failed to send message to channel {ChannelId}. SendMessageAsync returned null.", channelId); }
            }
            catch (Discord.Net.HttpException discordEx) { _logger.LogError(discordEx, "Discord API error sending sign-in message. Code: {ErrorCode}, Reason: {Reason}", discordEx.HttpCode, discordEx.Reason); }
            catch (Exception ex) { _logger.LogError(ex, "An error occurred while sending the sign-in message."); }
        }
    }
}