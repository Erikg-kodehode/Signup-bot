using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.ComponentModel;
using System.Linq;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using BotLauncher.Services;
using BotLauncher.Models;

namespace BotLauncher
{
    public partial class MainWindow : Window
    {
        private readonly BotService _botService;
        private readonly ConfigurationService _configService;
        private readonly LogMonitorService _logMonitorService;
        private readonly BotStatus _status;
        private bool _isClosing;

        // Properties for data binding
        public BotStatus Status => _status;
        public ObservableCollection<LogEntry> LogEntries => _logMonitorService.LogEntries;
        public BotConfig Config => _configService.Config;
        public bool AutoScroll { get; set; } = true;
        public string UptimeDisplay => $"Uptime: {_status.Uptime:hh\\:mm\\:ss}";
        public bool IsRunning => _status.State == BotState.Running;
        public bool CanStartBot => _status.State == BotState.Stopped || _status.State == BotState.Error;
        public bool CanStopBot => _status.State == BotState.Running || _status.State == BotState.Starting;

        public MainWindow()
        {
            InitializeComponent();
            
            _status = new BotStatus();
            _configService = new ConfigurationService();
            _botService = new BotService(_configService, _status);
            _logMonitorService = new LogMonitorService(_botService);

            // Set up data context for binding
            DataContext = this;
            
            // Wire up event handlers
            _botService.StateChanged += OnBotStateChanged;
            
            // Setup timer for updating the UI
            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => 
            {
                // Update properties that depend on the status
                OnPropertyChanged(nameof(UptimeDisplay));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStartBot));
                OnPropertyChanged(nameof(CanStopBot));
            };
            timer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Try to load from environment first for overrides
            _configService.LoadFromEnvironment();
            
            // Set the token if it exists in the configuration
            if (!string.IsNullOrEmpty(_configService.Config.BotToken))
            {
                TokenBox.Password = _configService.Config.BotToken;
            }
            
            // Add initial log entry
            _logMonitorService.AddLog("Bot Launcher started", LogLevel.Info);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _isClosing = true;
            if (_status.State == BotState.Running)
            {
                e.Cancel = true;
                ShutdownAndClose();
            }
        }

        private async void ShutdownAndClose()
        {
            if (_status.State == BotState.Running)
            {
                try
                {
                    _logMonitorService.AddLog("Shutting down bot before closing...", LogLevel.Info);
                    await _botService.StopBotAsync();
                }
                catch (Exception ex)
                {
                    _logMonitorService.AddLog($"Error stopping bot: {ex.Message}", LogLevel.Error);
                    MessageBox.Show($"Error stopping bot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            Close();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logMonitorService.AddLog("Starting bot...", LogLevel.Info);
                await _botService.StartBotAsync();
            }
            catch (Exception ex)
            {
                _logMonitorService.AddLog($"Error starting bot: {ex.Message}", LogLevel.Error);
                MessageBox.Show($"Error starting bot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logMonitorService.AddLog("Stopping bot...", LogLevel.Info);
                await _botService.StopBotAsync();
            }
            catch (Exception ex)
            {
                _logMonitorService.AddLog($"Error stopping bot: {ex.Message}", LogLevel.Error);
                MessageBox.Show($"Error stopping bot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _configService.Config.BotToken = TokenBox.Password;
                
                // Save configuration
                _configService.SaveConfiguration();
                
                _logMonitorService.AddLog("Settings saved successfully", LogLevel.Info);
                MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logMonitorService.AddLog($"Error saving settings: {ex.Message}", LogLevel.Error);
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                FileName = $"bot_logs_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _logMonitorService.ExportCurrentLogs(dialog.FileName);
                    MessageBox.Show("Logs exported successfully!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logMonitorService.AddLog($"Error exporting logs: {ex.Message}", LogLevel.Error);
                    MessageBox.Show($"Error exporting logs: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Bot executable (*.exe)|*.exe|.NET DLL (*.dll)|*.dll|All files (*.*)|*.*",
                Title = "Select Bot Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                _configService.Config.CustomBotPath = dialog.FileName;
                _logMonitorService.AddLog($"Selected custom bot path: {dialog.FileName}", LogLevel.Info);
            }
        }

        private void OnBotStateChanged(object? sender, BotState state)
        {
            if (_isClosing) return;

            Dispatcher.Invoke(() =>
            {
                _status.State = state;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStartBot));
                OnPropertyChanged(nameof(CanStopBot));
            });
        }

        private void OnPropertyChanged(string propertyName)
        {
            Dispatcher.Invoke(() =>
            {
                // Force UI update for binding
                if (DataContext != null)
                {
                    var binding = System.Windows.Data.BindingOperations.GetBinding(this, DependencyProperty.FromName(propertyName, GetType()));
                    if (binding != null)
                    {
                        var expression = System.Windows.Data.BindingOperations.GetBindingExpression(this, DependencyProperty.FromName(propertyName, GetType()));
                        if (expression != null)
                        {
                            expression.UpdateTarget();
                        }
                    }
                }
            });
        }
    }
}
