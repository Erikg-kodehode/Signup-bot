using Discord; // <-- Added this using statement
using Discord.Interactions;
using System;

namespace MorningSignInBot.Interactions.Modals
{
    public class ShowSignInsDateModal : IModal
    {
        public string Title => "Vis Innsjekkinger for Dato";

        public const string CustomId = "show_signins_date_modal";

        [InputLabel("Dato (ÅÅÅÅ-MM-DD)")]
        [ModalTextInput("date_input", TextInputStyle.Short, "f.eks. 2025-04-23", maxLength: 10, minLength: 10)]
        public string DateString { get; set; } = string.Empty;
    }
}