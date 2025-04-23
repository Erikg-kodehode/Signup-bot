namespace MorningSignInBot.Configuration
{
    public class DiscordSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string TargetChannelId { get; set; } = string.Empty;
        public int SignInHour { get; set; } = 8;
        public int SignInMinute { get; set; } = 0;
    }
}