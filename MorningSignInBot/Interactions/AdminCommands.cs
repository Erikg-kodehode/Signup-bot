using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using MorningSignInBot.Data;
using MorningSignInBot.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MorningSignInBot.Interactions
{
    [Attributes.RequireConfiguredRole]
    [Group("admin", "Administrative kommandoer for innsjekking.")]
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AdminCommands> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiscordSettings _discordSettings;
        private readonly INotificationService _notificationService;
        private readonly IOptionsMonitor<DiscordSettings> _discordSettingsMonitor;

        public AdminCommands(
            IServiceScopeFactory scopeFactory,
            ILogger<AdminCommands> logger,
            IConfiguration configuration,
            IOptionsMonitor<DiscordSettings> discordSettingsMonitor,
            INotificationService notificationService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _discordSettings = discordSettingsMonitor.CurrentValue;
            _discordSettingsMonitor = discordSettingsMonitor;
            _notificationService = notificationService;
        }

        [SlashCommand("view-config", "Viser gjeldende konfigurasjonsinnstillinger.")]
        public async Task ViewConfig()
        {
            await DeferAsync(ephemeral: true);

            try
            {
                var currentSettings = _discordSettingsMonitor.CurrentValue; // Use IOptionsMonitor for current value
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("Bot Konfigurering")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp()
                    .WithFooter("Bruk /admin set-time eller /admin set-channel for å endre innstillinger")
                    .AddField("Innsjekking-tidspunkt", $"{currentSettings.SignInHour:00}:{currentSettings.SignInMinute:00}", true);

                if (!string.IsNullOrEmpty(currentSettings.TargetChannelId) &&
                    ulong.TryParse(currentSettings.TargetChannelId, out ulong channelId))
                {
                    var channel = Context.Guild.GetChannel(channelId);
                    string channelInfo = channel != null ? $"#{channel.Name} ({channelId})" : $"Ukjent kanal ({channelId})";
                    embedBuilder.AddField("Målkanal", channelInfo, true);
                }
                else
                {
                    embedBuilder.AddField("Målkanal", "Ikke konfigurert", true);
                }

                await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feil ved visning av konfigurasjonen");
                await FollowupAsync("En feil oppstod under visning av konfigurasjonen.", ephemeral: true);
            }
        }

        [SlashCommand("set-time", "Setter tiden for daglig innsjekking.")]
        public async Task SetTime(
            [Summary("time", "Time (0-23)"), MinValue(0), MaxValue(23)] int hour,
            [Summary("minutt", "Minutt (0-59)"), MinValue(0), MaxValue(59)] int minute)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                string appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (!File.Exists(appSettingsPath))
                {
                    await FollowupAsync("Kunne ikke finne konfigurasjonsfilen (appsettings.json).", ephemeral: true);
                    return;
                }

                string json = await File.ReadAllTextAsync(appSettingsPath);
                JsonDocument document = JsonDocument.Parse(json);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();

                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name == "Discord")
                        {
                            writer.WritePropertyName("Discord");
                            writer.WriteStartObject();

                            foreach (var discordProperty in property.Value.EnumerateObject())
                            {
                                if (discordProperty.Name == "SignInHour")
                                {
                                    writer.WriteNumber("SignInHour", hour);
                                }
                                else if (discordProperty.Name == "SignInMinute")
                                {
                                    writer.WriteNumber("SignInMinute", minute);
                                }
                                else
                                {
                                    discordProperty.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }
                        else
                        {
                            property.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }

                string updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                await File.WriteAllTextAsync(appSettingsPath, updatedJson);

                _logger.LogInformation("SignInHour and SignInMinute updated in appsettings.json. Runtime will reflect after IOptionsMonitor reload or app restart.");
                // Note: For IOptionsMonitor to pick up changes from appsettings.json without a restart,
                // the file provider for the configuration needs to be set up to reload on change.
                // This is typical in ASP.NET Core but might need explicit setup in a generic host.
                // The simplest way to ensure change is effective is often a bot restart.

                await FollowupAsync($"Daglig innsjekking er nå satt til kl. {hour:00}:{minute:00}. Endringen trer i kraft ved neste IOptionsMonitor oppdatering eller omstart av botten.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feil ved endring av innsjekking-tidspunkt");
                await FollowupAsync("En feil oppstod under endring av innsjekking-tidspunkt.", ephemeral: true);
            }
        }

        [SlashCommand("set-channel", "Setter kanalen for daglig innsjekking.")]
        public async Task SetChannel(
            [Summary("kanal", "Kanalen der innsjekking skal postes")] ITextChannel channel)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                string appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (!File.Exists(appSettingsPath))
                {
                    await FollowupAsync("Kunne ikke finne konfigurasjonsfilen (appsettings.json).", ephemeral: true);
                    return;
                }

                string json = await File.ReadAllTextAsync(appSettingsPath);
                JsonDocument document = JsonDocument.Parse(json);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name == "Discord")
                        {
                            writer.WritePropertyName("Discord");
                            writer.WriteStartObject();
                            foreach (var discordProperty in property.Value.EnumerateObject())
                            {
                                if (discordProperty.Name == "TargetChannelId")
                                {
                                    writer.WriteString("TargetChannelId", channel.Id.ToString());
                                }
                                else
                                {
                                    discordProperty.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }
                        else
                        {
                            property.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }

                string updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                await File.WriteAllTextAsync(appSettingsPath, updatedJson);

                _logger.LogInformation("TargetChannelId updated in appsettings.json. Runtime will reflect after IOptionsMonitor reload or app restart.");

                await FollowupAsync($"Daglig innsjekking vil nå bli sendt til #{channel.Name}. Endringen trer i kraft ved neste IOptionsMonitor oppdatering eller omstart av botten.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feil ved endring av målkanal");
                await FollowupAsync("En feil oppstod under endring av målkanal.", ephemeral: true);
            }
        }

        [SlashCommand("send-now", "Sender daglig innsjekking umiddelbart.")]
        public async Task SendNow()
        {
            await DeferAsync(ephemeral: true);

            try
            {
                await _notificationService.SendDailySignInAsync();
                await FollowupAsync("Daglig innsjekking ble sendt manuelt.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feil ved manuell sending av innsjekking");
                await FollowupAsync("En feil oppstod under sending av innsjekking.", ephemeral: true);
            }
        }

        [SlashCommand("send-now-role", "Sender daglig innsjekking til en spesifikk rolle umiddelbart.")]
        public async Task SendNowRole(
            [Summary("rolle", "Rollen som skal motta innsjekking-melding")] IRole role)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                if (role.IsManaged || role.Id == Context.Guild.EveryoneRole.Id)
                {
                    await FollowupAsync("Kan ikke sende innsjekking til @everyone, @here eller integrerte roller.", ephemeral: true);
                    return;
                }

                await _notificationService.SendDailySignInToRoleAsync(role);
                await FollowupAsync($"Daglig innsjekking ble sendt manuelt til {role.Mention}.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feil ved manuell sending av innsjekking til rolle {RoleName}", role.Name);
                await FollowupAsync("En feil oppstod under sending av innsjekking til rollen.", ephemeral: true);
            }
        }

        [SlashCommand("mangler", "Viser hvem i en rolle som ikke sjekket inn på en gitt dato.")]
        public async Task ShowMissing(
            [Summary("rolle", "Rollen som skal sjekkes.")] IRole rolle,
            [Summary("dato", "Valgfri: Dato (dd-MM-yyyy eller yyyy-MM-dd). Bruk forslag eller skriv inn."), Autocomplete(typeof(Interactions.AutocompleteHandlers.DateAutocompleteHandler))]
            string? datoString = null)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                if (!TryParseDate(datoString, out DateTime targetDate))
                {
                    await FollowupAsync($"Ugyldig datoformat. Bruk ÅÅÅÅ-MM-DD eller DD-MM-YYYY, eller velg et forslag.", ephemeral: true);
                    return; 
                }

                DateTime startOfDayUtc = targetDate.Date.ToUniversalTime();
                DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);

                HashSet<ulong> membersWithRole;
                try
                {
                    await Context.Guild.DownloadUsersAsync();
                    membersWithRole = Context.Guild.Users
                        .Where(u => !u.IsBot && u.Roles.Any(r => r.Id == rolle.Id))
                        .Select(u => u.Id)
                        .ToHashSet();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Klarte ikke hente medlemmer for rollen '{RoleName}'. Sjekk bot-rettigheter/intents.", rolle.Name);
                    await FollowupAsync($"Klarte ikke hente medlemmer for rollen '{rolle.Name}'. Sjekk bot-rettigheter/intents.", ephemeral: true); 
                    return; 
                }

                if (!membersWithRole.Any())
                {
                    await FollowupAsync($"Ingen brukere funnet med rollen '{rolle.Name}'.", ephemeral: true);
                    return;
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    try
                    {
                        var signedInUserIdsList = await dbContext.SignIns
                            .Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc)
                            .Select(s => s.UserId)
                            .Distinct()
                            .ToListAsync();
                        var signedInUserIds = signedInUserIdsList.ToHashSet();
                        _logger.LogDebug("Fant {Count} unike brukere som sjekket inn den {TargetDate}", signedInUserIds.Count, targetDate.ToString("yyyy-MM-dd"));

                        var missingUserIds = membersWithRole.Except(signedInUserIds).ToList();
                        if (!missingUserIds.Any())
                        {
                            await FollowupAsync($"Alle med rollen '{rolle.Name}' sjekket inn den {targetDate:dd. MMMM yyyy}!", ephemeral: true);
                            return; 
                        }

                        var missingUsersMentions = new List<string>();
                        foreach (var userId in missingUserIds) 
                        {
                            var user = Context.Guild.GetUser(userId);
                            missingUsersMentions.Add(user?.Mention ?? $"`{userId}` (Ukjent/Forlatt?)");
                        }
                        
                        var embedBuilder = new EmbedBuilder()
                            .WithTitle($"Manglende innsjekkinger for '{rolle.Name}'")
                            .WithColor(Discord.Color.Orange)
                            .WithTimestamp(DateTimeOffset.Now)
                            .WithFooter($"Totalt {missingUserIds.Count} mangler");

                        string descriptionContent = $"Brukere med rollen '{rolle.Name}' som **ikke** sjekket inn den {targetDate:dd. MMMM yyyy}:\n\n{string.Join("\n", missingUsersMentions)}";

                        if (descriptionContent.Length > EmbedBuilder.MaxDescriptionLength)
                        {
                            // Truncate and add ellipsis if description is too long
                            descriptionContent = descriptionContent.Substring(0, Math.Min(descriptionContent.Length, EmbedBuilder.MaxDescriptionLength - 20)) + "\n... (listen er for lang)";
                            embedBuilder.WithDescription(descriptionContent);
                        }
                        else
                        {
                            embedBuilder.WithDescription(descriptionContent);
                        }
                        
                        await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error ved sammenligning av innsjekkinger for rolle '{RoleName}' på {TargetDate}", rolle.Name, targetDate);
                        await FollowupAsync("En feil oppstod under sammenligning av data.", ephemeral: true); 
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error i ShowMissing kommandoen");
                await FollowupAsync("Det oppstod en feil under utførelsen av kommandoen.", ephemeral: true);
            }
        }

        [SlashCommand("vis", "Viser hvem som har sjekket inn, filtrert på rolle og/eller dato.")]
        public async Task ViewSignIns(
            [Summary("rolle", "Valgfri: Rollen som skal filtreres på.")] IRole? rolle = null,
            [Summary("dato", "Valgfri: Dato (dd-MM-yyyy eller yyyy-MM-dd). Bruk forslag eller skriv inn."), Autocomplete(typeof(Interactions.AutocompleteHandlers.DateAutocompleteHandler))] string? datoString = null)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                if (!TryParseDate(datoString, out DateTime targetDate))
                {
                    await FollowupAsync($"Ugyldig datoformat. Bruk ÅÅÅÅ-MM-DD eller DD-MM-YYYY, eller velg et forslag.", ephemeral: true);
                    return;
                }

                DateTime startOfDayUtc = targetDate.Date.ToUniversalTime();
                DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    var query = dbContext.SignIns.AsQueryable();

                    query = query.Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc);

                    List<ulong>? roleMemberIds = null;
                    if (rolle != null)
                    {
                        await Context.Guild.DownloadUsersAsync();
                        roleMemberIds = Context.Guild.Users
                            .Where(u => !u.IsBot && u.Roles.Any(r => r.Id == rolle.Id))
                            .Select(u => u.Id)
                            .ToList();

                        if (roleMemberIds == null || !roleMemberIds.Any())
                        {
                            await FollowupAsync($"Ingen brukere funnet med rollen '{rolle.Name}'.", ephemeral: true);
                            return;
                        }
                        query = query.Where(s => roleMemberIds.Contains(s.UserId));
                    }

                    var signIns = await query.OrderBy(s => s.Timestamp).ToListAsync();

                    if (!signIns.Any())
                    {
                        string filterText = rolle != null ? $" for rollen '{rolle.Name}'" : "";
                        await FollowupAsync($"Ingen innsjekkinger funnet{filterText} for {targetDate:dd. MMMM yyyy}.", ephemeral: true);
                        return;
                    }

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle($"Innsjekkinger for {targetDate:dd. MMMM yyyy}" + (rolle != null ? $" (Rolle: {rolle.Name})" : ""))
                        .WithColor(Color.Green)
                        .WithTimestamp(DateTimeOffset.Now);

                    var descriptionBuilder = new StringBuilder();
                    foreach (var signIn in signIns)
                    {
                        var user = Context.Guild.GetUser(signIn.UserId);
                        string userName = user?.Mention ?? signIn.Username ?? signIn.UserId.ToString();
                        descriptionBuilder.AppendLine($"{userName} - {signIn.SignInType} ({signIn.Timestamp:HH:mm:ss} UTC)");
                    }

                    string descriptionContent = descriptionBuilder.ToString();
                    if (descriptionContent.Length > EmbedBuilder.MaxDescriptionLength)
                    {
                        descriptionContent = descriptionContent.Substring(0, Math.Min(descriptionContent.Length, EmbedBuilder.MaxDescriptionLength - 20)) + "\n... (listen er for lang)";
                        embedBuilder.WithDescription(descriptionContent);
                    }
                    else
                    {
                        embedBuilder.WithDescription(descriptionContent);
                    }

                    await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error i ViewSignIns kommandoen");
                await FollowupAsync("En feil oppstod under henting av innsjekkinger.", ephemeral: true);
            }
        }

        [SlashCommand("slett", "Sletter innsjekking for en bruker på en gitt dato.")]
        public async Task DeleteSignIn(
            [Summary("bruker", "Brukeren hvis innsjekking skal slettes.")] IGuildUser bruker,
            [Summary("dato", "Dato for innsjekking (dd-MM-yyyy eller yyyy-MM-dd)."), Autocomplete(typeof(Interactions.AutocompleteHandlers.DateAutocompleteHandler))] string datoString)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                if (!TryParseDate(datoString, out DateTime targetDate))
                {
                    await FollowupAsync($"Ugyldig datoformat. Bruk ÅÅÅÅ-MM-DD eller DD-MM-YYYY, eller velg et forslag.", ephemeral: true);
                    return;
                }

                DateTime startOfDayUtc = targetDate.Date.ToUniversalTime();
                DateTime endOfDayUtc = startOfDayUtc.AddDays(1).AddTicks(-1);

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    var signInEntry = await dbContext.SignIns
                        .FirstOrDefaultAsync(s => s.UserId == bruker.Id && s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc);

                    if (signInEntry == null)
                    {
                        await FollowupAsync($"Ingen innsjekking funnet for {bruker.Mention} på {targetDate:dd. MMMM yyyy}.", ephemeral: true);
                        return;
                    }

                    dbContext.SignIns.Remove(signInEntry);
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("Slettet innsjekking ID {SignInId} for bruker {UserName} ({UserId}) på dato {TargetDate}",
                        signInEntry.Id, bruker.Username, bruker.Id, targetDate.ToString("yyyy-MM-dd"));
                    await FollowupAsync($"Innsjekking for {bruker.Mention} på {targetDate:dd. MMMM yyyy} er slettet.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error i DeleteSignIn kommandoen for bruker {UserName} ({UserId})", bruker.Username, bruker.Id);
                await FollowupAsync("En feil oppstod under sletting av innsjekking.", ephemeral: true);
            }
        }

        private bool TryParseDate(string? dateString, out DateTime targetDate)
        {
            targetDate = DateTime.Today;
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return true;
            }

            if (dateString.Equals("I dag", StringComparison.OrdinalIgnoreCase))
            {
                targetDate = DateTime.Today;
                return true;
            }
            if (dateString.Equals("I går", StringComparison.OrdinalIgnoreCase))
            {
                targetDate = DateTime.Today.AddDays(-1);
                return true;
            }

            if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                return true;
            }
            if (DateTime.TryParseExact(dateString, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                return true;
            }

            _logger.LogWarning("Klarte ikke å parse dato: {DateString}. Støttede formater er yyyy-MM-dd og dd-MM-yyyy.", dateString);
            return false;
        }
    }
}
