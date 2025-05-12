using Microsoft.EntityFrameworkCore;
using MorningSignInBot.Data;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#nullable enable

namespace MorningSignInBot.Utilities
{
    public class DatabaseDiagnostics
    {
        public static async Task RunDiagnostics(string? databasePath = null)
        {
            // Default to the standard database path if none provided
            databasePath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signins.db");
            
            // Ensure the file exists
            if (!File.Exists(databasePath))
            {
                Console.WriteLine($"Error: Database file not found at {databasePath}");
                return;
            }

            Console.WriteLine($"Analyzing database: {databasePath}");
            
            // Create options with the database path
            var options = new DbContextOptionsBuilder<SignInContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            // Create a context
            using var context = new SignInContext(options);

            try
            {
                // 1. Print applied migrations
                Console.WriteLine("\nApplied Migrations:");
                var migrations = await context.Database.GetAppliedMigrationsAsync();
                if (!migrations.Any())
                {
                    Console.WriteLine("- No migrations applied");
                }
                else
                {
                    foreach (var migration in migrations)
                    {
                        Console.WriteLine($"- {migration}");
                    }
                }


                // 3. Print record counts
                Console.WriteLine("\nRecord Counts:");
                var signInsCount = await context.SignIns.CountAsync();
                Console.WriteLine($"- SignIns: {signInsCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError during diagnosis: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
