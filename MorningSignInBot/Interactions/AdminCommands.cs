using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MorningSignInBot.Data; // Correct namespace
using MorningSignInBot.Services; // Correct namespace
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MorningSignInBot.Interactions
{
    [RequireUserPermission(GuildPermission.Administrator)]
    [Group("innsjekk", "Kommandoer for å sjekke innlogginger.")]
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly SignInContext _dbContext;
        private readonly INotificationService _notificationService;
        private readonly ILogger<AdminCommands> _logger;

        public AdminCommands(
            SignInContext dbContext,
            INotificationService notificationService,
            ILogger<AdminCommands> logger)
        {
            _dbContext = dbContext;
            _notificationService = notificationService;
            _logger = logger;
        }

        [SlashCommand("vis", "Vis hvem som logget inn på en gitt dato (YYYY-MM-DD).")]
        public async Task ShowSignIns(
            [Summary("dato", "Dato (YYYY-MM-DD). Dagens dato hvis utelatt.")] string? datoString = null)
        {
            await DeferAsync(ephemeral: false);

            DateTime targetDate;
            if (string.IsNullOrWhiteSpace(datoString)) { targetDate = DateTime.Today; }
            else if (!DateTime.TryParseExact(datoString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                await FollowupAsync($"Ugyldig datoformat. Bruk YYYY-MM-DD.", ephemeral: true);
                return;
            }

            DateTime startOfDayUtc = targetDate.Date.ToUniversalTime();
            DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);

            _logger.LogInformation("Admin {AdminUser} checking sign-ins for date: {TargetDate}", Context.User.Username, targetDate.ToString("yyyy-MM-dd"));

            try
            {
                var signIns = await _dbContext.SignIns
                    .Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc)
                    .OrderBy(s => s.Timestamp)
                    .AsNoTracking() // Read-only query
                    .ToListAsync();

                if (!signIns.Any())
                {
                    await FollowupAsync($"Ingen logget inn den {targetDate:dd. MMMM yyyy}.", ephemeral: false);
                    return;
                }

                var embedBuilder = new EmbedBuilder()
                    .WithTitle($"Innsjekkinger for {targetDate:dd. MMMM yyyy}")
                    .WithColor(Color.Blue)
                    .WithTimestamp(DateTimeOffset.Now);

                var description = new StringBuilder();
                foreach (var entry in signIns)
                {
                    DateTime localTime = entry.Timestamp.ToLocalTime();
                    string timeString = localTime.ToString("HH:mm:ss");
                    description.AppendLine($"**{entry.Username}** ({entry.SignInType}) - Kl. {timeString}");
                }

                if (description.Length > 4096) { description.Length = 4090; description.Append("..."); }

                embedBuilder.WithDescription(description.ToString());
                await FollowupAsync(embed: embedBuilder.Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying sign-ins for date {TargetDate}", targetDate);
                await FollowupAsync("En feil oppstod under henting av data.", ephemeral: true);
            }
        }

        [SlashCommand("sendnå", "Sender dagens innsjekkingsmelding manuelt nå.")]
        public async Task ForceSendSignIn()
        {
            _logger.LogInformation("Admin {AdminUser} triggered manual sign-in message send.", Context.User.Username);
            await RespondAsync("Forsøker å sende innsjekkingsmeldingen...", ephemeral: true);

            try
            {
                await _notificationService.SendDailySignInAsync();
                await FollowupAsync("Innsjekkingsmelding forsøkt sendt.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual trigger of SendDailySignInAsync by admin {AdminUser}", Context.User.Username);
                await FollowupAsync("En feil oppstod under manuell sending.", ephemeral: true);
            }
        }

        [SlashCommand("slett", "[KOMMER SNART] Sletter en innsjekking for en bruker.")]
        public async Task DeleteSignInPlaceholder(IGuildUser bruker, string dato)
        {
            await RespondAsync("Denne kommandoen er ikke implementert ennå.", ephemeral: true);
        }

        [SlashCommand("mangler", "[KOMMER SNART] Viser hvem i en rolle som ikke sjekket inn.")]
        public async Task ShowMissingPlaceholder(IRole rolle, string? dato)
        {
            await RespondAsync("Denne kommandoen er ikke implementert ennå.", ephemeral: true);
        }
    }
}