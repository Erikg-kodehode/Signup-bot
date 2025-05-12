using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotLauncher.Models
{
    public class BotConfig : INotifyPropertyChanged
    {
        private string _targetChannelId = string.Empty;
        private string _guildId = string.Empty;
        private string _adminRoleId = string.Empty;
        private int _signInHour = 8;
        private int _signInMinute = 0;
        private string _botTokenPath = string.Empty;
        private string _botToken = string.Empty;
        private bool _useEmbeddedBot = true;
        private string _customBotPath = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        [JsonPropertyName("TargetChannelId")]
        public string TargetChannelId
        {
            get => _targetChannelId;
            set => SetProperty(ref _targetChannelId, value);
        }

        [JsonPropertyName("GuildId")]
        public string GuildId
        {
            get => _guildId;
            set => SetProperty(ref _guildId, value);
        }

        [JsonPropertyName("AdminRoleId")]
        public string AdminRoleId
        {
            get => _adminRoleId;
            set => SetProperty(ref _adminRoleId, value);
        }

        [JsonPropertyName("SignInHour")]
        public int SignInHour
        {
            get => _signInHour;
            set => SetProperty(ref _signInHour, Math.Max(0, Math.Min(23, value)));
        }

        [JsonPropertyName("SignInMinute")]
        public int SignInMinute
        {
            get => _signInMinute;
            set => SetProperty(ref _signInMinute, Math.Max(0, Math.Min(59, value)));
        }

        [JsonPropertyName("BotTokenPath")]
        public string BotTokenPath
        {
            get => _botTokenPath;
            set => SetProperty(ref _botTokenPath, value);
        }

        [JsonPropertyName("BotToken")]
        public string BotToken
        {
            get => _botToken;
            set => SetProperty(ref _botToken, value);
        }

        [JsonPropertyName("UseEmbeddedBot")]
        public bool UseEmbeddedBot
        {
            get => _useEmbeddedBot;
            set => SetProperty(ref _useEmbeddedBot, value);
        }

        [JsonPropertyName("CustomBotPath")]
        public string CustomBotPath
        {
            get => _customBotPath;
            set => SetProperty(ref _customBotPath, value);
        }

        public static BotConfig LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return new BotConfig();
            }

            try
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<BotConfig>(json) ?? new BotConfig();
                return config;
            }
            catch (Exception)
            {
                return new BotConfig();
            }
        }

        public void SaveToFile(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path) ?? string.Empty;
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(path, json);
            }
            catch (Exception)
            {
                // Suppress errors
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}

