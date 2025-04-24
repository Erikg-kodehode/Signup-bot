using System.Threading.Tasks;

namespace MorningSignInBot.Services
{
    public interface INotificationService
    {
        Task SendDailySignInAsync();
        Task DeletePreviousMessageAsync();
    }
}