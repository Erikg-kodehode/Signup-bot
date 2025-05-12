using System.Collections.Generic;

namespace MorningSignInBot.Configuration
{

    public class GuildConfiguration
    {
        public ulong GuildId { get; set; }
        public ulong AdminRoleId { get; set; }
        public string? GuildName { get; set; }
    }

    public class DiscordSettings
    {
        public string BotToken { get; set; } = string.Empty; // Populated by user secrets or env var
        public string TargetChannelId { get; set; } = string.Empty;
        public int SignInHour { get; set; } = 8;
        public int SignInMinute { get; set; } = 0;
        public List<GuildConfiguration> Guilds { get; set; } = new();
    }
}