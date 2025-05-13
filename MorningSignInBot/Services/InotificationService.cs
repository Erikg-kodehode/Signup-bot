using Discord;
using System.Threading.Tasks;

namespace MorningSignInBot.Services
{
    public interface INotificationService
    {
        Task SendDailySignInAsync();
        Task SendDailySignInToRoleAsync(IRole role);
        Task DeletePreviousMessageAsync();
    }
}
