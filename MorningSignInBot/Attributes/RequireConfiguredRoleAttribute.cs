// --- Add these using directives ---
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection; // For GetRequiredService
using Microsoft.Extensions.Options; // For IOptions
// ---------------------------------
using MorningSignInBot.Configuration; // Existing
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MorningSignInBot.Attributes
{
    public class RequireConfiguredRoleAttribute : PreconditionAttribute
    {
        // Made synchronous by removing async and returning Task.FromResult
        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            // Ensure services is not null before using it
            if (services == null)
            {
                return Task.FromResult(PreconditionResult.FromError("Internal error: Service provider not available."));
            }

            // Get settings using the injected services
            var settings = services.GetRequiredService<IOptions<DiscordSettings>>()?.Value;
            if (settings == null)
            {
                return Task.FromResult(PreconditionResult.FromError("Internal error: Discord settings not loaded."));
            }


            if (context.Guild == null)
            {
                return Task.FromResult(PreconditionResult.FromError("Denne kommandoen kan kun brukes i en server."));
            }

            var guildConfig = settings.Guilds.FirstOrDefault(g => g.GuildId == context.Guild.Id);

            if (guildConfig == null)
            {
                // Provide a more specific error message
                return Task.FromResult(PreconditionResult.FromError($"Denne serveren (ID: {context.Guild.Id}) er ikke konfigurert for admin-kommandoer i bot-innstillingene."));
            }

            if (context.User is not IGuildUser guildUser)
            {
                return Task.FromResult(PreconditionResult.FromError("Kunne ikke hente brukerinformasjon for serveren."));
            }

            // Check if the user has the configured admin role ID
            if (guildUser.RoleIds.Contains(guildConfig.AdminRoleId))
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }
            else
            {
                // Mention the required role ID for easier debugging if needed (optional)
                // return Task.FromResult(PreconditionResult.FromError($"Du har ikke påkrevd admin-rolle (ID: {guildConfig.AdminRoleId}).")); 
                return Task.FromResult(PreconditionResult.FromError("Du har ikke tillatelse til å bruke denne kommandoen."));
            }
        }
    }
}
