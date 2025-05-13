using MorningSignInBot.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseDiagnosticTool
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddDbContext<SignInContext>(options =>
                        options.UseSqlite("Data Source=signin.db"));
                })
                .Build();

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SignInContext>();
                await db.Database.EnsureCreatedAsync();

                Console.WriteLine("Database diagnostic tool running...");
                Console.WriteLine($"Database path: {db.Database.GetConnectionString()}");
                Console.WriteLine($"Database exists: {await db.Database.CanConnectAsync()}");

                // Add more diagnostic information as needed
            }
        }
    }
}
