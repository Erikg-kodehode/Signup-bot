using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

using BotLauncher.Models;
using BotLauncher.Services;

// If System.Windows.Threading.DispatcherTimer is not found, explicitly add:
// using System.Windows.Threading;

namespace BotLauncher
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly BotService _botService;
        private readonly ConfigurationService _configService;
        private readonly LogMonitorService _logMonitorService;
        private readonly BotStatus _status;
        private bool _isClosing;

        public event PropertyChangedEventHandler? PropertyChanged;

        public BotStatus Status => _status;
        public ObservableCollection<LogEntry> LogEntries => _logMonitorService.LogEntries;
        public BotConfig Config => _configService.Config;

        public string UptimeDisplay => $"Uptime: {_status.Uptime:hh\\:mm\\:ss}";
        public bool IsRunning => _status.State == BotState.Running;
        public bool CanStartBot => _status.State == BotState.Stopped || _status.State == BotState.Error;
        public bool CanStopBot => _status.State == BotState.Running || _status.State == BotState.Starting;

        public bool AutoScroll { get; set; } = true;

        public MainWindow()
        {
            InitializeComponent();

            _status = new BotStatus();
            _configService = new ConfigurationService();
            _botService = new BotService(_configService, _status);
            _logMonitorService = new LogMonitorService(_botService);

            _status.PropertyChanged += BotStatus_PropertyChanged;
            DataContext = this;
            _botService.StateChanged += OnBotStateChanged;

            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                OnPropertyChanged(nameof(UptimeDisplay));
            };
            timer.Start();
        }

        private void BotStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(BotStatus.State))
                {
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(CanStartBot));
                    OnPropertyChanged(nameof(CanStopBot));
                }
                if (e.PropertyName == nameof(BotStatus.Uptime))
                {
                    OnPropertyChanged(nameof(UptimeDisplay));
                }
            });
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _configService.LoadFromEnvironment();
            if (!string.IsNullOrEmpty(_configService.Config.BotToken))
            {
                TokenBox.Password = _configService.Config.BotToken;
            }
            _logMonitorService.AddLog("Bot Launcher loaded. Review settings and start the bot.", BotLauncher.Services.LogLevel.Info);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _isClosing = true;
            if (_status.State == BotState.Running || _status.State == BotState.Starting)
            {
                if (MessageBox.Show("The bot is currently running. Do you want to stop it and exit?",
                                    "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    Task.Run(async () =>
                    {
                        await ShutdownAndCloseAsync();
                        Dispatcher.Invoke(Close);
                    });
                }
                else
                {
                    e.Cancel = true;
                }
            }
            else
            {
                if (_status != null) _status.PropertyChanged -= BotStatus_PropertyChanged;
                if (_botService != null) _botService.StateChanged -= OnBotStateChanged;
            }
        }

        private async Task ShutdownAndCloseAsync()
        {
            if (_status.State == BotState.Running || _status.State == BotState.Starting)
            {
                _logMonitorService.AddLog("Attempting to shut down bot before closing...", BotLauncher.Services.LogLevel.Info);
                await _botService.StopBotAsync();
            }
            if (_status != null) _status.PropertyChanged -= BotStatus_PropertyChanged;
            if (_botService != null) _botService.StateChanged -= OnBotStateChanged;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _configService.Config.BotToken = TokenBox.Password;
            _logMonitorService.AddLog("Start button clicked. Attempting to start bot...", BotLauncher.Services.LogLevel.Info);
            await _botService.StartBotAsync();
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _logMonitorService.AddLog("Stop button clicked. Attempting to stop bot...", BotLauncher.Services.LogLevel.Info);
            await _botService.StopBotAsync();
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _configService.Config.BotToken = TokenBox.Password;
            _configService.SaveConfiguration().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logMonitorService.AddLog($"Error saving settings: {t.Exception?.GetBaseException().Message}", BotLauncher.Services.LogLevel.Error);
                    Dispatcher.Invoke(() => MessageBox.Show($"Error saving settings: {t.Exception?.GetBaseException().Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                else
                {
                    _logMonitorService.AddLog("Settings saved successfully.", BotLauncher.Services.LogLevel.Info);
                    Dispatcher.Invoke(() => MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
                }
            });
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _logMonitorService.Clear();
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".log",
                FileName = $"bot_launcher_logs_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            if (dialog.ShowDialog() == true)
            {
                _logMonitorService.ExportCurrentLogs(dialog.FileName);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = ".NET DLL (*.dll)|*.dll|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Bot Executable/DLL"
            };

            if (dialog.ShowDialog() == true)
            {
                Config.CustomBotPath = dialog.FileName;
                _logMonitorService.AddLog($"Selected custom bot path: {dialog.FileName}", BotLauncher.Services.LogLevel.Info);
            }
        }

        private void OnBotStateChanged(object? sender, BotState newState)
        {
            if (_isClosing) return;
            Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStartBot));
                OnPropertyChanged(nameof(CanStopBot));
            });
        }
    }
}