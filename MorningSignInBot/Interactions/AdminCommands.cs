using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MorningSignInBot.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MorningSignInBot.Interactions
{
    [RequireRole(1364185117182005308)]
    [Group("admin", "Administrative kommandoer for innsjekking.")]
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AdminCommands> _logger;

        public AdminCommands(
            IServiceScopeFactory scopeFactory,
            ILogger<AdminCommands> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        [SlashCommand("mangler", "Viser hvem i en rolle som ikke sjekket inn på en gitt dato.")]
        public async Task ShowMissing(
            [Summary("rolle", "Rollen som skal sjekkes.")] IRole rolle,
            [Summary("dato", "Valgfri: Dato (dd-MM-yyyy eller yyyy-MM-dd). Gir forslag.")]
            string? datoString = null)
        {
            // Implementation of ShowMissing command
            try
            {
                if (!TryParseDate(datoString, out DateTime targetDate)) 
                { 
                    await FollowupAsync($"Ugyldig datoformat.", ephemeral: true); 
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
                    _logger.LogError(ex, "Failed getting members for role '{RoleName}'. Check permissions/intents.", rolle.Name); 
                    await FollowupAsync($"Klarte ikke hente medlemmer for rollen '{rolle.Name}'. Sjekk bot-rettigheter/intents.", ephemeral: true); 
                    return; 
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    try
                    {
                        var signedInUserIdsList = await dbContext.SignIns.Where(s => s.Timestamp >= startOfDayUtc && s.Timestamp <= endOfDayUtc).Select(s => s.UserId).Distinct().ToListAsync(); 
                        var signedInUserIds = signedInUserIdsList.ToHashSet(); 
                        _logger.LogDebug("Found {Count} unique users signed in on {TargetDate}", signedInUserIds.Count, targetDate.ToString("yyyy-MM-dd"));
                        
                        var missingUserIds = membersWithRole.Except(signedInUserIds).ToList();
                        if (!missingUserIds.Any()) 
                        { 
                            await FollowupAsync($"Alle med rollen '{rolle.Name}' logget inn den {targetDate:dd. MMMM yyyy}!", ephemeral: true); 
                            return; 
                        }
                        
                        var missingUsersText = new List<string>(); 
                        foreach (var userId in missingUserIds) 
                        { 
                            var user = Context.Guild.GetUser(userId); 
                            missingUsersText.Add(user?.Mention ?? $"`{userId}` (Ukjent/Forlatt?)"); 
                        }
                        
                        var embedBuilder = new EmbedBuilder()
                            .WithTitle($"Manglende innsjekkinger for '{rolle.Name}'")
                            .WithDescription($"Brukere med rollen '{rolle.Name}' som **ikke** logget inn den {targetDate:dd. MMMM yyyy}:\n\n{string.Join("\n", missingUsersText)}")
                            .WithColor(Color.Orange)
                            .WithTimestamp(DateTimeOffset.Now);
                            
                        if (embedBuilder.Description.Length > 4096) 
                        { 
                            embedBuilder.Description = embedBuilder.Description.Substring(0, 4090) + "..."; 
                        }
                        
                        await FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogError(ex, "Error comparing sign-ins for role '{RoleName}' on {TargetDate}", rolle.Name, targetDate); 
                        await FollowupAsync("En feil oppstod under sammenligning av data.", ephemeral: true); 
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ShowMissing command");
                await FollowupAsync("Det oppstod en feil under utførelsen av kommandoen.", ephemeral: true);
            }
        }

        private bool TryParseDate(string? dateString, out DateTime targetDate)
        {
            targetDate = DateTime.Today;
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return true;
            }

            string[] formats = { "dd-MM-yyyy", "yyyy-MM-dd" };
            return DateTime.TryParseExact(dateString, formats, 
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out targetDate);
        }
    }
}
