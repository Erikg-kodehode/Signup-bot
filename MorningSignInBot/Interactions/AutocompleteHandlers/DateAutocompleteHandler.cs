using Discord;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Ensure this namespace matches the folder structure
namespace MorningSignInBot.Interactions.AutocompleteHandlers
{
    public class DateAutocompleteHandler : AutocompleteHandler
    {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(
            IInteractionContext context,
            IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter,
            IServiceProvider services)
        {
            try
            {
                string? userInput = (autocompleteInteraction.Data.Current.Value as string)?.ToLowerInvariant();
                var suggestions = new List<AutocompleteResult>();

                suggestions.Add(new AutocompleteResult("I dag", DateTime.Today.ToString("yyyy-MM-dd")));
                suggestions.Add(new AutocompleteResult("I går", DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")));
                suggestions.Add(new AutocompleteResult("Format: dd-MM-yyyy", "dd-MM-yyyy")); // Suggest formats
                suggestions.Add(new AutocompleteResult("Format: yyyy-MM-dd", "yyyy-MM-dd"));

                // Basic filtering example (optional)
                // if (!string.IsNullOrWhiteSpace(userInput))
                // {
                //     suggestions = suggestions
                //         .Where(s => s.Name.ToLowerInvariant().Contains(userInput))
                //         .ToList();
                // }

                return Task.FromResult(AutocompletionResult.FromSuccess(suggestions.Take(25)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating date suggestions: {ex.Message}");
                // Consider logging errors properly here if ILogger is injected/available
                return Task.FromResult(AutocompletionResult.FromError(ex));
            }
        }
    }
}