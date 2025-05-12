using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic; // Keep this

namespace MorningSignInBot.Configuration
{
    [Table("StageNotificationConfigs")]
    public class StageNotificationSetting
    {
        [Key]
        public ulong StageChannelId { get; set; }

        [Required]
        public ulong NotificationRoleId { get; set; }

        public ulong? NotificationChannelId { get; set; } // Optional notification channel

        public string? CustomMessage { get; set; }

        [Required]
        public ulong GuildId { get; set; }
        
        // Indicates if this role is manually overridden or should be auto-detected
        public bool IsRoleOverrideEnabled { get; set; } = false;
    }

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
        public List<StageNotificationSetting> StageNotifications { get; set; } = new(); // No longer used from config
    }
}