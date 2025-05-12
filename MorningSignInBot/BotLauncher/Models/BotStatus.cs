using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BotLauncher.Models
{
    public enum BotState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    public class BotStatus : INotifyPropertyChanged
    {
        private BotState _state = BotState.Stopped;
        private string _statusMessage = "Bot is stopped";
        private DateTime _lastStateChange = DateTime.Now;
        private int _processId = 0;
        private DateTime _startTime = DateTime.MinValue;
        private TimeSpan _uptime = TimeSpan.Zero;
        private int _logEntriesCount = 0;
        private bool _isConnectedToDiscord = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public BotState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    LastStateChange = DateTime.Now;
                    
                    StatusMessage = value switch
                    {
                        BotState.Stopped => "Bot is stopped",
                        BotState.Starting => "Bot is starting...",
                        BotState.Running => "Bot is running",
                        BotState.Stopping => "Bot is shutting down...",
                        BotState.Error => "Bot encountered an error",
                        _ => "Unknown state"
                    };
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public DateTime LastStateChange
        {
            get => _lastStateChange;
            set => SetProperty(ref _lastStateChange, value);
        }

        public int ProcessId
        {
            get => _processId;
            set => SetProperty(ref _processId, value);
        }

        public DateTime StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public TimeSpan Uptime
        {
            get => _uptime;
            set => SetProperty(ref _uptime, value);
        }

        public int LogEntriesCount
        {
            get => _logEntriesCount;
            set => SetProperty(ref _logEntriesCount, value);
        }

        public bool IsConnectedToDiscord
        {
            get => _isConnectedToDiscord;
            set => SetProperty(ref _isConnectedToDiscord, value);
        }

        public void UpdateUptime()
        {
            if (State == BotState.Running && StartTime != DateTime.MinValue)
            {
                Uptime = DateTime.Now - StartTime;
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

