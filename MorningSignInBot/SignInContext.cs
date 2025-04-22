using Microsoft.EntityFrameworkCore;
using System; // Required for Environment, Environment.SpecialFolder, Path

namespace MorningSignInBot
{
    public class SignInContext : DbContext
    {
        public DbSet<SignInEntry> SignIns { get; set; }

        public string DbPath { get; }

        public SignInContext()
        {
            // Simple path for the database file in the application's directory
            // Consider placing this in a more standard location if needed
            DbPath = System.IO.Path.Join(AppContext.BaseDirectory, "signins.db");
            Console.WriteLine($"Database path: {DbPath}"); // Log path for debugging
        }

        // The following configures EF Core to create a Sqlite database file locally
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Optional: Add indexes for faster querying by date or user ID
            modelBuilder.Entity<SignInEntry>()
               .HasIndex(e => e.Timestamp);
            modelBuilder.Entity<SignInEntry>()
               .HasIndex(e => e.UserId);
        }
    }
}