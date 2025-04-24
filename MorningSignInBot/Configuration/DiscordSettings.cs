namespace MorningSignInBot.Configuration
{
public class DiscordSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string TargetChannelId { get; set; } = string.Empty;
    public int SignInHour { get; set; }
    public int SignInMinute { get; set; }
    
    // Add server configurations
    public List<GuildConfiguration> Guilds { get; set; } = new();
}

public class GuildConfiguration
{
    public ulong GuildId { get; set; }
    public ulong AdminRoleId { get; set; }
    public string? GuildName { get; set; }
}
}