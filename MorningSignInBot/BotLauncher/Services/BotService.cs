using BotLauncher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace BotLauncher.Services
{
    public class BotService
    {
        private readonly ConfigurationService _configService;
        private Process? _botProcess;
        private readonly BotStatus _status;
        private readonly Timer _uptimeTimer;
        private StringBuilder _inputBuffer = new StringBuilder();

        public event EventHandler<string>? LogReceived;
        public event EventHandler<BotState>? StateChanged;

        public BotService(ConfigurationService configService, BotStatus status)
        {
            _configService = configService;
            _status = status;
            
            _uptimeTimer = new Timer(1000);
            _uptimeTimer.Elapsed += (s, e) => _status.UpdateUptime();
        }

        public async Task StartBotAsync()
        {
            if (_status.State == BotState.Running || _status.State == BotState.Starting)
                return;

            try
            {
                _status.State = BotState.Starting;
                StateChanged?.Invoke(this, _status.State);
                LogEvent("Starting bot process...");

                string executablePath;
                if (_configService.Config.UseEmbeddedBot)
                {
                    // Use the embedded bot (MorningSignInBot.dll)
                    executablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MorningSignInBot.dll");
                    
                    if (!File.Exists(executablePath))
                    {
                        // Try to find it in the parent directory (the bot project directory)
                        executablePath = Path.Combine(
                            Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? string.Empty, 
                            "MorningSignInBot.dll");
                    }
                }
                else
                {
                    // Use a custom path provided by the user
                    executablePath = _configService.Config.CustomBotPath;
                }

                if (!File.Exists(executablePath))
                {
                    LogEvent($"Error: Bot executable not found at {executablePath}");
                    _status.State = BotState.Error;
                    _status.StatusMessage = "Bot executable not found";
                    StateChanged?.Invoke(this, _status.State);
                    return;
                }

                // Prepare environment variables for the bot
                var envVars = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Discord__TargetChannelId"] = _configService.Config.TargetChannelId,
                    ["Discord__SignInHour"] = _configService.Config.SignInHour.ToString(),
                    ["Discord__SignInMinute"] = _configService.Config.SignInMinute.ToString()
                };

                // If token is provided directly, use it
                if (!string.IsNullOrEmpty(_configService.Config.BotToken))
                {
                    envVars["Discord__BotToken"] = _configService.Config.BotToken;
                }

                // Start the process
                _botProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = executablePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };

                // Add environment variables
                foreach (var pair in envVars)
                {
                    _botProcess.StartInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }

                // Set up output handling
                _botProcess.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        ProcessOutputLine(args.Data);
                    }
                };

                _botProcess.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        LogEvent($"ERROR: {args.Data}");
                    }
                };

                // Start the process
                _botProcess.Start();
                _botProcess.BeginOutputReadLine();
                _botProcess.BeginErrorReadLine();

                // Track process details
                _status.ProcessId = _botProcess.Id;
                _status.StartTime = DateTime.Now;
                _status.State = BotState.Running;
                _uptimeTimer.Start();
                
                StateChanged?.Invoke(this, _status.State);
                LogEvent($"Bot process started with PID {_botProcess.Id}");

                // Monitor for process exit
                await Task.Run(() =>
                {
                    _botProcess.WaitForExit();
                    
                    // When process exits
                    _uptimeTimer.Stop();
                    _status.State = BotState.Stopped;
                    _status.ProcessId = 0;
                    _status.IsConnectedToDiscord = false;
                    
                    LogEvent("Bot process has exited");
                    StateChanged?.Invoke(this, _status.State);
                    
                    _botProcess.Dispose();
                    _botProcess = null;
                });
            }
            catch (Exception ex)
            {
                LogEvent($"Error starting bot: {ex.Message}");
                _status.State = BotState.Error;
                _status.StatusMessage = $"Error: {ex.Message}";
                StateChanged?.Invoke(this, _status.State);
            }
        }

        public async Task StopBotAsync()
        {
            if (_status.State != BotState.Running || _botProcess == null)
                return;

            try
            {
                _status.State = BotState.Stopping;
                StateChanged?.Invoke(this, _status.State);
                LogEvent("Stopping bot...");

                // Try graceful shutdown first
                if (!_botProcess.HasExited)
                {
                    _botProcess.CloseMainWindow();
                    
                    // Wait up to 5 seconds for graceful exit
                    await Task.Run(() =>
                    {
                        if (!_botProcess.WaitForExit(5000))
                        {
                            // Force kill if it doesn't exit gracefully
                            LogEvent("Bot did not exit gracefully, forcing termination");
                            _botProcess.Kill(true);
                        }
                    });
                }

                _uptimeTimer.Stop();
                _status.State = BotState.Stopped;
                _status.ProcessId = 0;
                _status.IsConnectedToDiscord = false;
                StateChanged?.Invoke(this, _status.State);
                LogEvent("Bot stopped");
            }
            catch (Exception ex)
            {
                LogEvent($"Error stopping bot: {ex.Message}");
                _status.State = BotState.Error;
                _status.StatusMessage = $"Error while stopping: {ex.Message}";
                StateChanged?.Invoke(this, _status.State);
            }
        }

        private void ProcessOutputLine(string line)
        {
            LogEvent(line);

            // Parse line to extract state information
            if (line.Contains("Discord client state: Connected", StringComparison.OrdinalIgnoreCase))
            {
                _status.IsConnectedToDiscord = true;
            }
            else if (line.Contains("Discord client state: Disconnected", StringComparison.OrdinalIgnoreCase))
            {
                _status.IsConnectedToDiscord = false;
            }
            else if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                    line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
            {
                // Log contains serious error, but don't change state unless it's already running
                if (_status.State == BotState.Running)
                {
                    _status.StatusMessage = "Bot encountered an error but is still running";
                }
            }
        }

        private void LogEvent(string message)
        {
            LogReceived?.Invoke(this, message);
            _status.LogEntriesCount++;
        }

        public void SendInputToBot(string input)
        {
            if (_botProcess != null && !_botProcess.HasExited)
            {
                try
                {
                    _botProcess.StandardInput.WriteLine(input);
                    LogEvent($"SENT: {input}");
                }
                catch (Exception ex)
                {
                    LogEvent($"Error sending input to bot: {ex.Message}");
                }
            }
        }
    }
}
