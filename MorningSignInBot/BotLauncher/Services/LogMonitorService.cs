using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BotLauncher.Services
{
    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }

        public LogEntry(string message, LogLevel level = LogLevel.Info)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public class LogMonitorService
    {
        private readonly BotService _botService;
        private readonly DispatcherTimer _autoScrollTimer;
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private readonly int _maxEntries = 1000;

        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
        public bool AutoScroll { get; set; } = true;
        public event EventHandler? LogUpdated;

        public LogMonitorService(BotService botService, string logDirectory = "Logs")
        {
            _botService = botService;
            _botService.LogReceived += OnBotLogReceived;
            
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDirectory);
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
            
            _logFilePath = Path.Combine(_logDirectory, $"launcher_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            // Setup auto-scroll timer
            _autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _autoScrollTimer.Tick += (s, e) => LogUpdated?.Invoke(this, EventArgs.Empty);
            _autoScrollTimer.Start();
            
            // Add initial log entry
            AddLog($"Log monitor started. Logs will be saved to: {_logFilePath}", LogLevel.Info);
        }

        private void OnBotLogReceived(object? sender, string message)
        {
            LogLevel level = DetermineLogLevel(message);
            AddLog(message, level);
        }

        public void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            var entry = new LogEntry(message, level);
            
            // We need to use the Dispatcher to modify ObservableCollection from different threads
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LogEntries.Add(entry);
                    
                    // Limit the number of entries to prevent memory issues
                    while (LogEntries.Count > _maxEntries)
                    {
                        LogEntries.RemoveAt(0);
                    }
                });
            }
            
            // Write to log file
            Task.Run(() => WriteToLogFile(entry));
        }

        private void WriteToLogFile(LogEntry entry)
        {
            try
            {
                string logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch
            {
                // Silently fail if we can't write to the log file
            }
        }

        private LogLevel DetermineLogLevel(string message)
        {
            string upperMessage = message.ToUpperInvariant();
            
            if (upperMessage.Contains("FATAL") || upperMessage.Contains("CRITICAL"))
                return LogLevel.Fatal;
            if (upperMessage.Contains("ERROR") || upperMessage.Contains("EXCEPTION"))
                return LogLevel.Error;
            if (upperMessage.Contains("WARN"))
                return LogLevel.Warning;
            if (upperMessage.Contains("DEBUG") || upperMessage.Contains("TRACE"))
                return LogLevel.Debug;
            
            return LogLevel.Info;
        }

        public void Clear()
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LogEntries.Clear();
                    AddLog("Log cleared by user", LogLevel.Info);
                });
            }
        }
        public void ExportCurrentLogs(string filePath)
        {
            try
            {
                using (var writer = new StreamWriter(filePath))
                {
                    foreach (var entry in LogEntries)
                    {
                        writer.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
                    }
                }
                
                AddLog($"Logs exported to: {filePath}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"Failed to export logs: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
