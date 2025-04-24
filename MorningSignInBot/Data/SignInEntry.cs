using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MorningSignInBot.Data
{
    public class SignInEntry
    {
        [Key]
        public int Id { get; set; }
        public ulong UserId { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public required string Username { get; set; }
        public DateTime Timestamp { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(20)]
        public required string SignInType { get; set; }

        public SignInEntry() { }

        [SetsRequiredMembers]
        public SignInEntry(ulong userId, string username, DateTime timestamp, string signInType)
        {
            UserId = userId;
            Username = username ?? throw new ArgumentNullException(nameof(username));
            Timestamp = timestamp;
            SignInType = signInType ?? throw new ArgumentNullException(nameof(signInType));
        }
    }
}