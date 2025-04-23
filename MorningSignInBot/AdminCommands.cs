using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore; // Required for EF Core async operations
using Microsoft.Extensions.Logging; // Required for ILogger
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
        private readonly ILogger<AdminCommands> _logger;

        // Constructor injection for database context and logger
        public AdminCommands(SignInContext dbContext, ILogger<AdminCommands> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [SlashCommand("vis", "Vis hvem som logget inn på en gitt dato (YYYY-MM-DD).")]
        public async Task ShowSignIns(
            [Summary("dato", "Dato for innsjekkinger (format YYYY-MM-DD). Bruker dagens dato hvis utelatt.")] string? datoString = null) // Changed to string?
        {
            // Defer response while database query runs
            await DeferAsync(ephemeral: false);

            DateTime targetDate;

            if (string.IsNullOrWhiteSpace(datoString))
            {
                targetDate = DateTime.Today; // Default to today
            }
            // Validate and parse the date string
            else if (!DateTime.TryParseExact(datoString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                await FollowupAsync($"Ugyldig datoformat. Bruk YYYY-MM-DD (f.eks. {DateTime.Today:yyyy-MM-dd}).", ephemeral: true);
                return;
            }

            // Calculate start and end times for the target day in UTC if Timestamps are stored in UTC
            // If Timestamps are local, adjust accordingly. Assuming UTC for DB storage consistency.
            DateTime startOfDayUtc = targetDate.Date.ToUniversalTime();
            DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);

            _logger.LogInformation("Admin {AdminUser} checking sign-ins for date: {TargetDate}", Context.User.Username, targetDate.ToString("yyyy-MM-dd"));

            try
            {
                // Query database for entries within the date range
                var signIns = await _dbContext.SignIns
                    .Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc)
                    .OrderBy(s => s.Timestamp)
                    .ToListAsync(); // Get data from DB

                // Handle case where no one signed in
                if (!signIns.Any())
                {
                    await FollowupAsync($"Ingen logget inn den {targetDate:dd. MMMM yyyy}.", ephemeral: false);
                    return;
                }

                // Build the response embed
                var embedBuilder = new EmbedBuilder()
                    .WithTitle($"Innsjekkinger for {targetDate:dd. MMMM yyyy}")
                    .WithColor(Color.Blue)
                    .WithTimestamp(DateTimeOffset.Now);

                var description = new StringBuilder();
                foreach (var entry in signIns)
                {
                    // Convert UTC timestamp back to local time for display if needed
                    // Assuming CEST/Norway time for display. Adjust if server timezone differs.
                    DateTime localTime = entry.Timestamp.ToLocalTime(); // Simple local conversion
                    string timeString = localTime.ToString("HH:mm:ss");
                    description.AppendLine($"**{entry.Username}** ({entry.SignInType}) - Kl. {timeString}");
                }

                // Handle potential Discord embed description length limits
                if (description.Length > 4096)
                {
                    description.Length = 4090;
                    description.Append("...");
                }

                embedBuilder.WithDescription(description.ToString());

                // Send the embed as a followup response
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