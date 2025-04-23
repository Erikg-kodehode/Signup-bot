using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using System;
using System.Threading.Tasks;

namespace MorningSignInBot.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly DiscordSocketClient _client;
        private readonly DiscordSettings _settings;

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
        }

        public async Task SendDailySignInAsync()
        {
            _logger.LogDebug("Attempting to send daily sign-in message...");

            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday || DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Skipping message send: Weekend.");
                return;
            }

            try
            {
                if (!ulong.TryParse(_settings.TargetChannelId, out ulong channelId))
                {
                    _logger.LogError("Invalid TargetChannelId format: {ChannelId}", _settings.TargetChannelId);
                    return;
                }

                if (_client.ConnectionState != ConnectionState.Connected)
                {
                    _logger.LogWarning("Cannot send message: Discord client not connected.");
                    return;
                }

                var channel = await _client.GetChannelAsync(channelId);
                if (channel is not ITextChannel targetChannel)
                {
                    _logger.LogError("Target channel {ChannelId} not found or is not a text channel.", channelId);
                    return;
                }

                var buttonKontor = new ButtonBuilder()
                   .WithLabel("Logg inn (Kontor)")
                   .WithCustomId(SignInButtonKontorId)
                   .WithStyle(ButtonStyle.Success)
                   .WithEmote(Emoji.Parse("🏢"));

                var buttonHjemme = new ButtonBuilder()
                    .WithLabel("Logg inn (Hjemmekontor)")
                    .WithCustomId(SignInButtonHjemmeId)
                    .WithStyle(ButtonStyle.Primary)
                    .WithEmote(Emoji.Parse("🏠"));

                var component = new ComponentBuilder()
                   .WithButton(buttonKontor)
                   .WithButton(buttonHjemme)
                   .Build();

                string messageText = $"God morgen! Vennligst logg inn for **{DateTime.Now:dddd, d. MMMM}** ved å bruke en av knappene under.";
                await targetChannel.SendMessageAsync(text: messageText, components: component);

                _logger.LogInformation("Sign-in message sent successfully to channel {ChannelId}", channelId);
            }
            catch (Discord.Net.HttpException discordEx)
            {
                _logger.LogError(discordEx, "Discord API error sending sign-in message. Code: {ErrorCode}, Reason: {Reason}", discordEx.HttpCode, discordEx.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while sending the sign-in message.");
            }
        }
    }
}