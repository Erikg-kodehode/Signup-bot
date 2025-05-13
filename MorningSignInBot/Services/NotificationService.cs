using Discord;
using Discord.Net;  // This contains HttpException
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
    internal class BotMessageState // Changed from BotState to avoid conflict if BotState enum exists elsewhere
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
                _logger.LogInformation("State file not found at path: {Path}, no previous message to delete based on state.", _stateFilePath);
                return;
            }

            BotMessageState? previousState = null;
            try
            {
                _logger.LogDebug("Reading state file from: {Path}", _stateFilePath);
                string json = await File.ReadAllTextAsync(_stateFilePath);
                previousState = JsonSerializer.Deserialize<BotMessageState>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read or deserialize state file: {Path}. Deleting corrupted state file.", _stateFilePath);
                try { File.Delete(_stateFilePath); } catch { /* ignore cleanup error */ }
                return;
            }


            if (previousState?.LastMessageId > 0 && previousState?.LastChannelId > 0)
            {
                _logger.LogInformation("Found previous message state - MessageId: {MessageId}, ChannelId: {ChannelId}",
                    previousState.LastMessageId, previousState.LastChannelId);

                if (_client.GetChannel(previousState.LastChannelId) is not ITextChannel channel)
                {
                    _logger.LogWarning("Could not find channel {ChannelId} or it is not a text channel. Deleting state file.", previousState.LastChannelId);
                    try { File.Delete(_stateFilePath); } catch { /* ignore */ }
                    return;
                }

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
                    var messageToDelete = await channel.GetMessageAsync(previousState.LastMessageId);
                    if (messageToDelete == null)
                    {
                        _logger.LogWarning("Message {MessageId} no longer exists in channel {ChannelName}. Deleting state file.",
                            previousState.LastMessageId, channel.Name);
                        File.Delete(_stateFilePath);
                        return;
                    }

                    await channel.DeleteMessageAsync(previousState.LastMessageId);
                    _logger.LogInformation("Successfully deleted message {MessageId} from channel {ChannelName}",
                        previousState.LastMessageId, channel.Name);
                    File.Delete(_stateFilePath);
                }
                catch (HttpException discordEx) when (discordEx.HttpCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Previous message {MessageId} was already deleted (404 Not Found). Deleting state file.", previousState.LastMessageId);
                    File.Delete(_stateFilePath);
                }
                catch (HttpException discordEx)
                {
                    _logger.LogError(discordEx, "Discord API error deleting message {MessageId}. Status: {Status}, Code: {DiscordCode}, Reason: {Reason}",
                        previousState.LastMessageId, discordEx.HttpCode, discordEx.DiscordCode, discordEx.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in DeletePreviousMessageAsync for message {MessageId}", previousState.LastMessageId);
                }
            }
            else
            {
                _logger.LogWarning("Invalid state file content or missing IDs. Deleting state file.");
                try { File.Delete(_stateFilePath); } catch { /* ignore */ }
            }
        }

        private async Task SaveMessageStateAsync(IMessage message)
        {
            if (message == null) return;

            var currentState = new BotMessageState
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
        await SendDailySignInToRoleAsync(role: null!);
    }

    public async Task SendDailySignInToRoleAsync(IRole role)
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

            var buttonKontor = new ButtonBuilder()
                .WithLabel("Logg inn (Kontor)")
                .WithCustomId(SignInButtonKontorId)
                .WithStyle(ButtonStyle.Success)
                .WithEmote(Emoji.Parse("🏢"));

            var buttonHjemme = new ButtonBuilder()
                .WithLabel("Logg inn (Hjemmekontor)")
                .WithCustomId(SignInButtonHjemmeId)
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(Emoji.Parse("🏡"));

            var component = new ComponentBuilder()
                .WithButton(buttonKontor)
                .WithButton(buttonHjemme)
                .Build();

            var currentDate = DateTime.Now;
            string roleText = role != null ? $"{role.Mention} " : "";
            string roleTitle = role != null ? $" for {role.Name}" : "";
            
            var embed = new EmbedBuilder()
                .WithTitle($"📋 Daglig Innsjekking{roleTitle}: {currentDate:dddd, d. MMMM yyyy}")
                .WithDescription($"**God morgen!** 👋\n\n{roleText}Vennligst logg inn for dagens arbeidsdag ved å bruke en av knappene nedenfor.")
                .WithColor(new Discord.Color(52, 152, 219)) // Explicitly use Discord.Color
                .WithFooter(footer => {
                    footer.Text = "Innsjekking registreres automatisk i systemet";
                    // footer.IconUrl = "https://cdn.discordapp.com/emojis/1042377292536643655.png"; 
                })
                .WithTimestamp(DateTimeOffset.Now);

            sentMessage = await targetChannel.SendMessageAsync(
                text: role != null ? role.Mention : null,
                embed: embed.Build(),
                components: component);

            if (sentMessage != null)
            {
                string roleInfo = role != null ? $" targeting role {role.Name}" : "";
                _logger.LogInformation("Sign-in message sent successfully to channel {ChannelId}{RoleInfo} with ID {MessageId}", channelId, roleInfo, sentMessage.Id);
                await SaveMessageStateAsync(sentMessage);
            }
            else { _logger.LogWarning("Failed to send message to channel {ChannelId}. SendMessageAsync returned null.", channelId); }
        }
        catch (HttpException discordEx) { _logger.LogError(discordEx, "Discord API error sending sign-in message. Code: {ErrorCode}, Reason: {Reason}", discordEx.HttpCode, discordEx.Reason); }
        catch (Exception ex) { _logger.LogError(ex, "An error occurred while sending the sign-in message."); }
    }
    }
}