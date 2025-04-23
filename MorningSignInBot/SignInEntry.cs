using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis; // Required for SetsRequiredMembers

namespace MorningSignInBot
{
    public class SignInEntry
    {
        [Key] // Marks Id as the primary key
        public int Id { get; set; }

        public ulong UserId { get; set; }

        [MaxLength(100)] // Optional: Limit username length
        public required string Username { get; set; } // Required property

        // Store timestamps in UTC for consistency across timezones/DST changes
        public DateTime Timestamp { get; set; }

        [MaxLength(20)] // Optional: Limit type length
        public required string SignInType { get; set; } // e.g., "Kontor", "Hjemmekontor" - Required property

        // Parameterless constructor required by EF Core
        // Keep it accessible if needed by specific EF Core scenarios, otherwise 'protected' could be used.
        public SignInEntry() { }

        // Constructor used when creating new entries in code
        [SetsRequiredMembers] // Informs compiler this constructor sets all required members
        public SignInEntry(ulong userId, string username, DateTime timestamp, string signInType)
        {
            UserId = userId;
            // Ensure required string properties are not null
            Username = username ?? throw new ArgumentNullException(nameof(username));
            Timestamp = timestamp; // Should be UTC when passed in
            SignInType = signInType ?? throw new ArgumentNullException(nameof(signInType));
        }
    }
}