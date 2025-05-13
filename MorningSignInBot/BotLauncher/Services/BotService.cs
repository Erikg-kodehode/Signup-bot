using BotLauncher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
// Ensure this using is present if LogLevel is used from BotLauncher.Services
// using BotLauncher.Services; 

namespace BotLauncher.Services
{
    public class BotService : IBotService
    {
        private readonly ConfigurationService _configService;
        private Process? _botProcess;
        private readonly BotStatus _status;
        private readonly Timer _uptimeTimer;
        private readonly StringBuilder _inputBuffer = new StringBuilder();

        public event EventHandler<string>? LogReceived;
        public event EventHandler<BotState>? StateChanged;

        public bool IsRunning => _status.State == BotState.Running;

        public BotService(ConfigurationService configService, BotStatus status)
        {
            _configService = configService;
            _status = status;

            _uptimeTimer = new Timer(1000);
            _uptimeTimer.Elapsed += (s, e) => _status.UpdateUptime();
        }

        private string? FindBotDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllName = "MorningSignInBot.dll";
            string[] pathsToTest = {
                Path.Combine(baseDir, dllName),
                Path.Combine(baseDir, "..", "MorningSignInBot", dllName),
                Path.Combine(baseDir, "..", "..", "..", "MorningSignInBot", "bin", "Debug", "net8.0", dllName),
                Path.Combine(baseDir, "..", "..", "..", "MorningSignInBot", "bin", "Release", "net8.0", dllName)
            };

            foreach (var p in pathsToTest)
            {
                string fullPath = Path.GetFullPath(p);
                if (File.Exists(fullPath))
                {
                    LogEvent($"Found bot DLL at: {fullPath}");
                    return fullPath;
                }
            }
            LogEvent($"Bot DLL '{dllName}' not found in common locations relative to '{baseDir}'.", BotLauncher.Services.LogLevel.Error);
            return null;
        }

        public async Task StartBotAsync()
        {
            await Task.Yield();

            if (_status.State == BotState.Running || _status.State == BotState.Starting)
            {
                LogEvent("Bot is already running or starting.", BotLauncher.Services.LogLevel.Warning);
                return;
            }

            _status.State = BotState.Starting;
            StateChanged?.Invoke(this, _status.State);
            LogEvent("Attempting to start bot process...");

            try
            {
                string? executablePath;
                if (_configService.Config.UseEmbeddedBot)
                {
                    executablePath = FindBotDllPath();
                }
                else
                {
                    executablePath = _configService.Config.CustomBotPath;
                }

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    string NFEmessage = $"Error: Bot executable/DLL not found or path is invalid. Path: '{executablePath ?? "not specified"}'";
                    LogEvent(NFEmessage, BotLauncher.Services.LogLevel.Error);
                    _status.State = BotState.Error;
                    _status.StatusMessage = "Bot executable/DLL not found.";
                    StateChanged?.Invoke(this, _status.State);
                    return;
                }

                var envVars = new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Discord__TargetChannelId"] = _configService.Config.TargetChannelId,
                    ["Discord__SignInHour"] = _configService.Config.SignInHour.ToString(),
                    ["Discord__SignInMinute"] = _configService.Config.SignInMinute.ToString(),
                    ["Discord__Guilds__0__GuildId"] = _configService.Config.GuildId,
                    ["Discord__Guilds__0__AdminRoleId"] = _configService.Config.AdminRoleId,
                    ["Discord__BotToken"] = null
                };

                if (!string.IsNullOrEmpty(_configService.Config.BotToken))
                {
                    envVars["Discord__BotToken"] = _configService.Config.BotToken;
                }
                else
                {
                    LogEvent("Bot token is not configured in the launcher. The bot will attempt to read it from its own sources (e.g., secrets, console).", BotLauncher.Services.LogLevel.Warning);
                }

                _botProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"\"{executablePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppDomain.CurrentDomain.BaseDirectory
                    }
                };

                foreach (var pair in envVars)
                {
                    if (pair.Value != null)
                    {
                        _botProcess.StartInfo.EnvironmentVariables[pair.Key] = pair.Value;
                        LogEvent($"Setting ENV for bot: {pair.Key}={(pair.Key == "Discord__BotToken" && !string.IsNullOrEmpty(pair.Value) ? "***" : pair.Value)}");
                    }
                }

                _botProcess.OutputDataReceived += (sender, args) => ProcessOutputLine(args.Data, BotLauncher.Services.LogLevel.Info);
                _botProcess.ErrorDataReceived += (sender, args) => ProcessOutputLine(args.Data, BotLauncher.Services.LogLevel.Error);

                if (!_botProcess.Start())
                {
                    LogEvent("Failed to start the bot process.", BotLauncher.Services.LogLevel.Error);
                    _status.State = BotState.Error;
                    _status.StatusMessage = "Failed to start process.";
                    StateChanged?.Invoke(this, _status.State);
                    return;
                }

                _botProcess.BeginOutputReadLine();
                _botProcess.BeginErrorReadLine();

                _status.ProcessId = _botProcess.Id;
                _status.StartTime = DateTime.Now;
                _status.State = BotState.Running;
                _uptimeTimer.Start();
                StateChanged?.Invoke(this, _status.State);
                LogEvent($"Bot process started successfully with PID: {_status.ProcessId}. Path: {executablePath}");

                _ = Task.Run(() =>
                {
                    try
                    {
                        _botProcess.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        LogEvent($"Error during WaitForExit: {ex.Message}", BotLauncher.Services.LogLevel.Error);
                    }
                    finally
                    {
                        OnBotProcessExited();
                    }
                });
            }
            catch (Exception ex)
            {
                LogEvent($"Critical error starting bot: {ex.Message}\nStackTrace: {ex.StackTrace}", BotLauncher.Services.LogLevel.Fatal);
                _status.State = BotState.Error;
                _status.StatusMessage = $"Startup Error: {ex.Message}";
                StateChanged?.Invoke(this, _status.State);
            }
        }

        private void OnBotProcessExited()
        {
            int exitCode = -1;
            try
            {
                if (_botProcess != null && _botProcess.HasExited)
                {
                    exitCode = _botProcess.ExitCode;
                }
                else if (_botProcess != null && !_botProcess.HasExited)
                {
                    LogEvent("Bot process exited without standard ExitCode reporting (possibly killed).", BotLauncher.Services.LogLevel.Warning);
                }
            }
            catch (InvalidOperationException) { /* Process may have already been killed or exited abruptly */ }
            catch (Exception ex)
            {
                LogEvent($"Exception getting exit code: {ex.Message}", BotLauncher.Services.LogLevel.Warning);
            }

            _uptimeTimer.Stop();
            _status.State = BotState.Stopped;
            _status.ProcessId = 0;
            _status.IsConnectedToDiscord = false;
            LogEvent($"Bot process has exited. Exit Code: {exitCode}", exitCode == 0 ? BotLauncher.Services.LogLevel.Info : BotLauncher.Services.LogLevel.Warning);
            StateChanged?.Invoke(this, _status.State);

            _botProcess?.Dispose();
            _botProcess = null;
        }

        public async Task StopBotAsync()
        {
            await Task.Yield();

            if (_status.State != BotState.Running && _status.State != BotState.Starting)
            {
                LogEvent("Bot is not running or starting, no action to stop.", BotLauncher.Services.LogLevel.Warning);
                return;
            }
            if (_botProcess == null || _botProcess.HasExited)
            {
                LogEvent("Bot process is not active or already exited.", BotLauncher.Services.LogLevel.Warning);
                _status.State = BotState.Stopped;
                StateChanged?.Invoke(this, _status.State);
                return;
            }

            _status.State = BotState.Stopping;
            StateChanged?.Invoke(this, _status.State);
            LogEvent("Attempting to stop bot process...");

            try
            {
                if (!_botProcess.HasExited)
                {
                    if (_botProcess.CloseMainWindow())
                    {
                        LogEvent("Sent CloseMainWindow signal. Waiting for graceful exit...");
                        if (await Task.Run(() => _botProcess.WaitForExit(5000)))
                        {
                            LogEvent("Bot process exited gracefully.");
                        }
                        else
                        {
                            LogEvent("Bot did not exit gracefully after CloseMainWindow. Attempting to kill.", BotLauncher.Services.LogLevel.Warning);
                            _botProcess.Kill(true);
                            LogEvent("Bot process killed.");
                        }
                    }
                    else
                    {
                        LogEvent("Could not send CloseMainWindow signal (e.g., console app). Attempting to kill.", BotLauncher.Services.LogLevel.Warning);
                        _botProcess.Kill(true);
                        LogEvent("Bot process killed.");
                    }
                }
            }
            catch (InvalidOperationException ioe) when (ioe.Message.Contains("No process is associated"))
            {
                LogEvent("Bot process was already gone when trying to stop it.", BotLauncher.Services.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                LogEvent($"Error during bot stop: {ex.Message}", BotLauncher.Services.LogLevel.Error);
                _status.State = BotState.Error;
                _status.StatusMessage = $"Error stopping: {ex.Message}";
                StateChanged?.Invoke(this, _status.State);
                return;
            }

            if (_botProcess == null || _botProcess.HasExited)
            {
                OnBotProcessExited();
            }
        }

        private void ProcessOutputLine(string? line, BotLauncher.Services.LogLevel defaultLevel)
        {
            if (line == null) return;
            BotLauncher.Services.LogLevel determinedLevel = DetermineLogLevelFromLine(line, defaultLevel);
            LogEvent(line, determinedLevel);

            if (line.Contains("Discord client state: Connected", StringComparison.OrdinalIgnoreCase))
            {
                _status.IsConnectedToDiscord = true;
            }
            else if (line.Contains("Discord client state: Disconnected", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Discord Gateway] Disconnected", StringComparison.OrdinalIgnoreCase))
            {
                _status.IsConnectedToDiscord = false;
            }
            else if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) || line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase))
            {
                if (_status.State == BotState.Running)
                {
                    _status.StatusMessage = "Bot reported a critical error";
                }
            }
        }

        private BotLauncher.Services.LogLevel DetermineLogLevelFromLine(string line, BotLauncher.Services.LogLevel defaultLevel)
        {
            string upperMessage = line.ToUpperInvariant();
            if (upperMessage.Contains("FATAL") || upperMessage.Contains("CRITICAL")) return BotLauncher.Services.LogLevel.Fatal;
            if (upperMessage.Contains("ERROR") || upperMessage.Contains("EXCEPTION")) return BotLauncher.Services.LogLevel.Error;
            if (upperMessage.Contains("WARN") || upperMessage.Contains("WARNING")) return BotLauncher.Services.LogLevel.Warning;
            if (upperMessage.Contains("INFO") || upperMessage.Contains("INFORMATION")) return BotLauncher.Services.LogLevel.Info;
            if (upperMessage.Contains("DEBUG")) return BotLauncher.Services.LogLevel.Debug;
            if (upperMessage.Contains("TRACE") || upperMessage.Contains("VERBOSE")) return BotLauncher.Services.LogLevel.Debug;

            return defaultLevel;
        }

        private void LogEvent(string message, BotLauncher.Services.LogLevel level = BotLauncher.Services.LogLevel.Info)
        {
            LogReceived?.Invoke(this, $"[{level.ToString().ToUpper()}] {message}");
            if (level != BotLauncher.Services.LogLevel.Debug)
            {
                _status.LogEntriesCount++;
            }
        }

        public void SendInputToBot(string input)
        {
            if (_botProcess != null && !_botProcess.HasExited && _botProcess.StandardInput != null)
            {
                try
                {
                    _botProcess.StandardInput.WriteLine(input);
                    LogEvent($"SENT TO BOT: {input}", BotLauncher.Services.LogLevel.Debug);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("StandardIn has not been redirected"))
                {
                    LogEvent($"Error sending input: StandardInput not redirected. {ex.Message}", BotLauncher.Services.LogLevel.Error);
                }
                catch (Exception ex)
                {
                    LogEvent($"Error sending input to bot: {ex.Message}", BotLauncher.Services.LogLevel.Error);
                }
            }
            else
            {
                LogEvent("Cannot send input: Bot process is not running or input stream is unavailable.", BotLauncher.Services.LogLevel.Warning);
            }
        }
    }
}