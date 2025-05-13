using System;
using System.Threading.Tasks;
using BotLauncher.Models;

namespace BotLauncher.Services
{
    public interface IBotService
    {
        event EventHandler<string>? LogReceived;
        event EventHandler<BotState>? StateChanged;
        Task StartBotAsync();
        Task StopBotAsync();
        void SendInputToBot(string input);
        bool IsRunning { get; }
    }
}