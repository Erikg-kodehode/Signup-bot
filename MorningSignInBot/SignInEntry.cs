using System;
using System.ComponentModel.DataAnnotations; // Required for [Key]

namespace MorningSignInBot
{
    public class SignInEntry
    {
        [Key] // Marks Id as the primary key
        public int Id { get; set; }

        public ulong UserId { get; set; }

        [MaxLength(100)] // Optional: Limit username length
        public required string Username { get; set; } // Use required keyword (C# 11+) or ensure it's set in constructor

        public DateTime Timestamp { get; set; }

        [MaxLength(20)] // Optional: Limit type length
        public required string SignInType { get; set; } // e.g., "Kontor", "Hjemmekontor"

        // Parameterless constructor required by EF Core
        public SignInEntry() { }

        // Constructor for easier creation
        public SignInEntry(ulong userId, string username, DateTime timestamp, string signInType)
        {
            UserId = userId;
            Username = username ?? throw new ArgumentNullException(nameof(username));
            Timestamp = timestamp;
            SignInType = signInType ?? throw new ArgumentNullException(nameof(signInType));
        }
    }
}