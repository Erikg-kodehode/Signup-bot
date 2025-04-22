using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore; // Required for EF Core async operations
using System;
using System.Globalization; // Required for DateTime parsing
using System.Linq; // Required for LINQ queries like Where, OrderBy
using System.Text; // Required for StringBuilder
using System.Threading.Tasks; // Required for async Task

namespace MorningSignInBot
{
    // Module restricted to Administrators
    [RequireUserPermission(GuildPermission.Administrator)]
    [Group("innsjekk", "Kommandoer for å sjekke innlogginger.")] // Group commands under /innsjekk
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly SignInContext _dbContext;
        private readonly ILogger<AdminCommands> _logger; // Optional: Add logging

        // Constructor injection for database context and logger
        public AdminCommands(SignInContext dbContext, ILogger<AdminCommands> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [SlashCommand("vis", "Vis hvem som logget inn på en gitt dato (YYYY-MM-DD).")]
        public async Task ShowSignIns(
            [Summary("dato", "Dato for innsjekkinger (format YYYY-MM-DD). Bruker dagens dato hvis utelatt.")] string datoString = null)
        {
            await DeferAsync(ephemeral: false); // Acknowledge interaction while we query DB

            DateTime targetDate;

            if (string.IsNullOrWhiteSpace(datoString))
            {
                targetDate = DateTime.Today; // Default to today
            }
            else if (!DateTime.TryParseExact(datoString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                await FollowupAsync($"Ugyldig datoformat. Bruk YYYY-MM-DD (f.eks. {DateTime.Today:yyyy-MM-dd}).", ephemeral: true);
                return;
            }

            // Calculate start and end of the target day
            DateTime startOfDay = targetDate.Date; // Midnight at start of day
            DateTime endOfDay = startOfDay.AddDays(1).AddTicks(-1); // Just before midnight next day

            _logger.LogInformation("Admin {AdminUser} checking sign-ins for date: {TargetDate}", Context.User.Username, targetDate.ToString("yyyy-MM-dd"));

            try
            {
                var signIns = await _dbContext.SignIns
                    .Where(s => s.Timestamp >= startOfDay && s.Timestamp <= endOfDay)
                    .OrderBy(s => s.Timestamp)
                    .ToListAsync(); // Get data from DB

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
                    // Format timestamp for Norway locale if possible, default to HH:mm:ss
                    string timeString = entry.Timestamp.ToString("HH:mm:ss");
                    description.AppendLine($"**{entry.Username}** ({entry.SignInType}) - Kl. {timeString}");
                }

                // Discord embed descriptions have a limit (4096 chars)
                if (description.Length > 4096)
                {
                    description.Length = 4090; // Trim slightly
                    description.Append("...");
                }

                embedBuilder.WithDescription(description.ToString());

                await FollowupAsync(embed: embedBuilder.Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying sign-ins for date {TargetDate}", targetDate);
                await FollowupAsync("En feil oppstod under henting av data.", ephemeral: true);
            }
        }
    }
}