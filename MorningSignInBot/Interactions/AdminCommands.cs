using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MorningSignInBot.Data;
using MorningSignInBot.Interactions.AutocompleteHandlers;
using MorningSignInBot.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MorningSignInBot.Interactions
{
    [RequireRole(1364185117182005308)]
    [Group("admin", "Administrative kommandoer for innsjekking.")]
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly INotificationService _notificationService;
        private readonly ILogger<AdminCommands> _logger;
        private readonly string[] _dateFormats = { "dd-MM-yyyy", "yyyy-MM-dd" };
        private const string _dateFormatDescription = "dd-MM-yyyy eller umpire-MM-dd";

        public AdminCommands(
            IServiceScopeFactory scopeFactory,
            INotificationService notificationService,
            ILogger<AdminCommands> logger)
        {
            _scopeFactory = scopeFactory;
            _notificationService = notificationService;
            _logger = logger;
        }

        private async Task SendTemporaryResponseAsync(string message, bool isError = false, int deleteAfterSeconds = 10)
        {
            try
            {
                var response = await FollowupAsync(message, 
                    ephemeral: true,  // Keep ephemeral for privacy
                    embeds: isError ? new[] { new EmbedBuilder().WithDescription(message).WithColor(Color.Red).Build() } : null);
                
                // For ephemeral messages, we'll just let them naturally disappear from the user's view
                // Discord doesn't allow deletion of ephemeral messages
                _logger.LogDebug("Sent temporary response: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send temporary response");
            }
        }
        private bool TryParseDate(string? dateString, out DateTime targetDate)
        {
            targetDate = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(dateString))
            {
                targetDate = DateTime.Today;
                _logger.LogDebug("[DateParseResult] Input was blank, defaulting to today: {TargetDate}", targetDate);
                return true;
            }
            _logger.LogDebug("[DateParseAttempt] Trying to parse date input: '{DateInput}' with formats [{Formats}]", dateString, string.Join(", ", _dateFormats));
            bool success = DateTime.TryParseExact(dateString, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate);
            _logger.LogDebug("[DateParseResult] Parse success: {Success}, Result Date: {TargetDate}", success, targetDate);
            return success;
        }

        [SlashCommand("vis", "Vis hvem som logget inn (filtrer evt. på rolle og dato).")]
        public async Task ShowSignIns(
            [Summary("rolle", "Valgfri: Filtrer listen til kun denne rollen.")] IRole? rolle = null,
            [Summary("dato", $"Valgfri: Dato ({_dateFormatDescription}). Gir forslag. Dagens dato hvis utelatt.")]
            [Autocomplete(typeof(DateAutocompleteHandler))]
            string? datoString = null)
        {
            await DeferAsync(ephemeral: true);
            if (!TryParseDate(datoString, out DateTime targetDate)) { await FollowupAsync($"Ugyldig datoformat. Bruk formatet {_dateFormatDescription} eller velg et forslag.", ephemeral: true); return; }

            DateTime startOfDayUtc = targetDate.Date.ToUniversalTime(); DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);
            _logger.LogInformation("Admin {AdminUser} checking sign-ins for date: {TargetDate}, Role Filter: {RoleFilter}", Context.User.Username, targetDate.ToString("yyyy-MM-dd"), rolle?.Name ?? "Ingen");

            if (rolle != null && Context.Guild == null) { await FollowupAsync("Rollefiltrering kan kun brukes i en server.", ephemeral: true); return; }
            HashSet<ulong>? membersWithRole = null;
            if (rolle != null) { try { await Context.Guild.DownloadUsersAsync(); membersWithRole = Context.Guild.Users.Where(u => !u.IsBot && u.Roles.Any(r => r.Id == rolle.Id)).Select(u => u.Id).ToHashSet(); _logger.LogDebug("Found {Count} members for role filter '{RoleName}'", membersWithRole.Count, rolle.Name); } catch (Exception ex) { _logger.LogError(ex, "Failed getting members for role filter '{RoleName}'.", rolle.Name); await FollowupAsync($"Klarte ikke hente medlemmer for rollen '{rolle.Name}'.", ephemeral: true); return; } }

            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                try
                {
                    var signInsQuery = dbContext.SignIns.Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc).OrderBy(s => s.Timestamp).AsNoTracking(); var signIns = await signInsQuery.ToListAsync();
                    if (rolle != null && membersWithRole != null) { signIns = signIns.Where(s => membersWithRole.Contains(s.UserId)).ToList(); _logger.LogDebug("Filtered sign-ins to {Count} entries based on role '{RoleName}'", signIns.Count, rolle.Name); }
                    string title = rolle == null ? $"Innsjekkinger for {targetDate:dd. MMMM umpire}" : $"Innsjekkinger for Rolle '{rolle.Name}' den {targetDate:dd. MMMM umpire}";
                    if (!signIns.Any()) { string noResultMessage = rolle == null ? $"Ingen logget inn den {targetDate:dd. MMMM umpire}." : $"Ingen med rollen '{rolle.Name}' logget inn den {targetDate:dd. MMMM umpire}."; await FollowupAsync(noResultMessage, ephemeral: true); return; }
                    var embedBuilder = new EmbedBuilder().WithTitle(title).WithColor(Color.Blue).WithTimestamp(DateTimeOffset.Now); var description = new StringBuilder();
                    foreach (var entry in signIns) { DateTime localTime = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc).ToLocalTime(); string timeString = localTime.ToString("HH:mm:ss"); description.AppendLine($"**{entry.Username}** ({entry.SignInType}) - Kl. {timeString}"); }
                    if (description.Length > 4096) { description.Length = 4090; description.Append("..."); }
                    embedBuilder.WithDescription(description.ToString()); await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                }
                catch (Exception ex) { _logger.LogError(ex, "Error querying sign-ins for date {TargetDate}, Role Filter: {RoleFilter}", targetDate, rolle?.Name ?? "Ingen"); await FollowupAsync("En feil oppstod under henting av data.", ephemeral: true); }
            }
        }

        [SlashCommand("sendnå", "Sender dagens innsjekkingsmelding manuelt nå.")]
        public async Task ForceSendSignIn()
        {
            _logger.LogInformation("Admin {AdminUser} triggered manual sign-in message send.", Context.User.Username);
            await DeferAsync(ephemeral: true);
            try 
            {
                await _notificationService.DeletePreviousMessageAsync();
                await _notificationService.SendDailySignInAsync();
                await SendTemporaryResponseAsync("Innsjekkingsmelding sendt og forrige melding slettet.", false, 5);
            }
            catch (Exception ex) 
            { 
                _logger.LogError(ex, "Error during manual trigger by admin {AdminUser}", Context.User.Username); 
                await SendTemporaryResponseAsync("En feil oppstod under sending av melding.", true, 10);
            }
        }

        [SlashCommand("slett", "Sletter en innsjekking for en bruker på en gitt dato.")]
        public async Task DeleteSignIn(
             [Summary("bruker", "Brukeren hvis innsjekking skal slettes.")] IGuildUser bruker,
             [Summary("dato", $"Dato for innsjekkingen ({_dateFormatDescription}). Gir forslag.")]
             [Autocomplete(typeof(DateAutocompleteHandler))]
             string datoString)
        {
            await DeferAsync(ephemeral: true);
            if (!TryParseDate(datoString, out DateTime targetDate) || string.IsNullOrWhiteSpace(datoString)) { if (string.IsNullOrWhiteSpace(datoString)) { await FollowupAsync($"Dato må oppgis for sletting. Bruk formatet {_dateFormatDescription}.", ephemeral: true); return; } await FollowupAsync($"Ugyldig datoformat. Bruk formatet {_dateFormatDescription}.", ephemeral: true); return; }

            DateTime startOfDayUtc = targetDate.Date.ToUniversalTime(); DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);
            _logger.LogInformation("Admin {AdminUser} attempting delete for user {TargetUser} ({TargetUserId}) on {TargetDate}", Context.User.Username, bruker.Username, bruker.Id, targetDate.ToString("yyyy-MM-dd"));

            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                try
                {
                    var entryToDelete = await dbContext.SignIns.Where(s => s.UserId == bruker.Id && s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc).FirstOrDefaultAsync();
                    if (entryToDelete == null) { await FollowupAsync($"{bruker.Mention} hadde ingen innsjekking den {targetDate:dd. MMMM umpire} som kunne slettes.", ephemeral: true); return; }
                    dbContext.SignIns.Remove(entryToDelete); int changes = await dbContext.SaveChangesAsync();
                    if (changes > 0) { _logger.LogInformation("Deleted entry ID {EntryId} for {TargetUser} on {TargetDate}", entryToDelete.Id, bruker.Username, targetDate.ToString("yyyy-MM-dd")); await FollowupAsync($"Slettet innsjekking for {bruker.Mention} den {targetDate:dd. MMMM umpire}.", ephemeral: true); }
                    else { _logger.LogWarning("Attempted delete for {TargetUser} on {TargetDate}, but no changes saved.", bruker.Username, targetDate.ToString("yyyy-MM-dd")); await FollowupAsync($"Kunne ikke slette innsjekkingen (ingen endringer lagret).", ephemeral: true); }
                }
                catch (Exception ex) { _logger.LogError(ex, "Error deleting sign-in for {TargetUser} on {TargetDate}", bruker.Username, targetDate); await FollowupAsync("En feil oppstod under sletting av data.", ephemeral: true); }
            }
        }

        [SlashCommand("mangler", "Viser hvem i en rolle som ikke sjekket inn på en gitt dato.")]
        public async Task ShowMissing(
            [Summary("rolle", "Rollen som skal sjekkes.")] IRole rolle,
            [Summary("dato", $"Valgfri: Dato ({_dateFormatDescription}). Gir forslag. Dagens dato hvis utelatt.")]
            [Autocomplete(typeof(DateAutocompleteHandler))]
            string? datoString = null)
        {
            await DeferAsync(ephemeral: true);
            if (!TryParseDate(datoString, out DateTime targetDate)) { await FollowupAsync($"Ugyldig datoformat. Bruk formatet {_dateFormatDescription} eller velg et forslag.", ephemeral: true); return; }

            DateTime startOfDayUtc = targetDate.Date.ToUniversalTime(); DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);
            _logger.LogInformation("Admin {AdminUser} checking missing for role '{RoleName}' on {TargetDate}", Context.User.Username, rolle.Name, targetDate.ToString("yyyy-MM-dd"));

            if (Context.Guild == null) { await FollowupAsync("Kommandoen må kjøres i en server.", ephemeral: true); return; }
            HashSet<ulong> membersWithRole;
            try { await Context.Guild.DownloadUsersAsync(); membersWithRole = Context.Guild.Users.Where(u => !u.IsBot && u.Roles.Any(r => r.Id == rolle.Id)).Select(u => u.Id).ToHashSet(); if (!membersWithRole.Any()) { await FollowupAsync($"Fant ingen brukere med rollen '{rolle.Name}'.", ephemeral: true); return; } _logger.LogDebug("Found {Count} members with role '{RoleName}'", membersWithRole.Count, rolle.Name); }
            catch (Exception ex) { _logger.LogError(ex, "Failed getting members for role '{RoleName}'. Check permissions/intents.", rolle.Name); await FollowupAsync($"Klarte ikke hente medlemmer for rollen '{rolle.Name}'. Sjekk bot-rettigheter/intents.", ephemeral: true); return; }

            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                try
                {
                    var signedInUserIdsList = await dbContext.SignIns.Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc).Select(s => s.UserId).Distinct().ToListAsync(); var signedInUserIds = signedInUserIdsList.ToHashSet(); _logger.LogDebug("Found {Count} unique users signed in on {TargetDate}", signedInUserIds.Count, targetDate.ToString("yyyy-MM-dd"));
                    var missingUserIds = membersWithRole.Except(signedInUserIds).ToList();
                    if (!missingUserIds.Any()) { await FollowupAsync($"Alle med rollen '{rolle.Name}' logget inn den {targetDate:dd. MMMM umpire}!", ephemeral: true); return; }
                    var missingUsersText = new List<string>(); foreach (var userId in missingUserIds) { var user = Context.Guild.GetUser(userId); missingUsersText.Add(user?.Mention ?? $"`{userId}` (Ukjent/Forlatt?)"); }
                    var embedBuilder = new EmbedBuilder().WithTitle($"Manglende innsjekkinger for '{rolle.Name}'").WithDescription($"Brukere med rollen '{rolle.Name}' som **ikke** logget inn den {targetDate:dd. MMMM umpire}:\n\n{string.Join("\n", missingUsersText)}").WithColor(Color.Orange).WithTimestamp(DateTimeOffset.Now);
                    if (embedBuilder.Description.Length > 4096) { embedBuilder.Description = embedBuilder.Description.Substring(0, 4090) + "..."; }
                    await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                }
                catch (Exception ex) { _logger.LogError(ex, "Error comparing sign-ins for role '{RoleName}' on {TargetDate}", rolle.Name, targetDate); await FollowupAsync("En feil oppstod under sammenligning av data.", ephemeral: true); }
            }
        }
    }
}