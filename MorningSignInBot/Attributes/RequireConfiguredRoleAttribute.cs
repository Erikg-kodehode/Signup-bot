using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MorningSignInBot.Attributes
{
    public class RequireConfiguredRoleAttribute : PreconditionAttribute
    {
        public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            var settings = services.GetRequiredService<IOptions<DiscordSettings>>().Value;
            var guild = settings.Guilds.FirstOrDefault(g => g.GuildId == context.Guild?.Id);
            
            if (guild == null)
                return PreconditionResult.FromError("This server is not configured for admin commands.");

            if (context.User is not IGuildUser guildUser)
                return PreconditionResult.FromError("This command can only be used in a server.");

            if (guildUser.RoleIds.Contains(guild.AdminRoleId))
                return PreconditionResult.FromSuccess();

            return PreconditionResult.FromError("You do not have permission to use this command.");
        }
    }
}

