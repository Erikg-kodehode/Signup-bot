using BotLauncher.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BotLauncher.Services
{
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        private BotConfig _config;

        public BotConfig Config => _config;
        
        public event EventHandler? ConfigurationSaved;
        
        public ConfigurationService(string configFileName = "botlauncher_config.json")
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MorningSignInBot");
            
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            _configFilePath = Path.Combine(appDataPath, configFileName);
            _config = BotConfig.LoadFromFile(_configFilePath);
            
            // If no config exists, create default values
            if (string.IsNullOrEmpty(_config.TargetChannelId))
            {
                _config = CreateDefaultConfig();
                SaveConfiguration();
            }
        }

        private BotConfig CreateDefaultConfig()
        {
            return new BotConfig
            {
                SignInHour = 8,
                SignInMinute = 0,
                UseEmbeddedBot = true,
                CustomBotPath = ""
            };
        }

        public Task SaveConfiguration()
        {
            return Task.Run(() =>
            {
                _config.SaveToFile(_configFilePath);
                ConfigurationSaved?.Invoke(this, EventArgs.Empty);
            });
        }

        public void LoadFromEnvironment()
        {
            // Try to read configuration values from environment variables
            string? channelId = Environment.GetEnvironmentVariable("Discord__TargetChannelId");
            if (!string.IsNullOrEmpty(channelId))
            {
                _config.TargetChannelId = channelId;
            }
            
            string? guildId = Environment.GetEnvironmentVariable("Discord__GuildId");
            if (!string.IsNullOrEmpty(guildId))
            {
                _config.GuildId = guildId;
            }
            
            string? adminRoleId = Environment.GetEnvironmentVariable("Discord__AdminRoleId");
            if (!string.IsNullOrEmpty(adminRoleId))
            {
                _config.AdminRoleId = adminRoleId;
            }
            
            string? signInHour = Environment.GetEnvironmentVariable("Discord__SignInHour");
            if (!string.IsNullOrEmpty(signInHour) && int.TryParse(signInHour, out int hour))
            {
                _config.SignInHour = Math.Max(0, Math.Min(23, hour));
            }
            
            string? signInMinute = Environment.GetEnvironmentVariable("Discord__SignInMinute");
            if (!string.IsNullOrEmpty(signInMinute) && int.TryParse(signInMinute, out int minute))
            {
                _config.SignInMinute = Math.Max(0, Math.Min(59, minute));
            }
            
            string? botToken = Environment.GetEnvironmentVariable("Discord__BotToken");
            if (!string.IsNullOrEmpty(botToken))
            {
                _config.BotToken = botToken;
            }
        }
    }
}

