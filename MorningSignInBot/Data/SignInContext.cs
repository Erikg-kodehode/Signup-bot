using Microsoft.EntityFrameworkCore;
using System; // Keep essential usings
using System.IO;

namespace MorningSignInBot.Data
{
    public class SignInContext : DbContext
    {
        // --- Ensure BOTH DbSets are present and uncommented ---
        public DbSet<SignInEntry> SignIns { get; set; }
        // -----------------------------------------------------

        // Primary constructor for DI and EF Core tools
        public SignInContext(DbContextOptions<SignInContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // --- Ensure configuration for BOTH entities is present and uncommented ---
            modelBuilder.Entity<SignInEntry>(entity =>
            {
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.UserId);
            });
            // -----------------------------------------------------------------------
        }
    }
}
