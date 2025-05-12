using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // Keep this
using MorningSignInBot.Configuration;
using MorningSignInBot.Data;
using MorningSignInBot.Services;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MorningSignInBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // --- ADD THIS LINE ---
            Console.WriteLine($"---> Reading TEST_CHECK env var: {Environment.GetEnvironmentVariable("TEST_CHECK")}");
            // ---------------------

            // Bootstrap config for Serilog
            var bootstrapConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables() // Reads environment variables including those from Docker
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(bootstrapConfig)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting host builder...");

                IHost host = Host.CreateDefaultBuilder(args)
                    .UseSerilog((context, services, configuration) => configuration
                        .ReadFrom.Configuration(context.Configuration)
                        .Enrich.FromLogContext())
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.Configure<DiscordSettings>(hostContext.Configuration.GetSection("Discord"));
                        services.Configure<DatabaseSettings>(hostContext.Configuration.GetSection("Database")); // Configure DatabaseSettings

                        // Register DbContext using options from DI, including path config
                        services.AddDbContext<SignInContext>((serviceProvider, options) =>
                        {
                            var dbSettings = serviceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
                            string relativePath = dbSettings.Path;
                            string basePath = AppContext.BaseDirectory;
                            string dbPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

                            string? directory = Path.GetDirectoryName(dbPath);
                            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                            {
                                try { Directory.CreateDirectory(directory); } catch { /* Log */ }
                            }
                            options.UseSqlite($"Data Source={dbPath}");
                        });


                        var discordConfig = new DiscordSocketConfig
                        {
                            GatewayIntents = GatewayIntents.Guilds
                                       | GatewayIntents.GuildMessages
                                       | GatewayIntents.GuildMembers
                                       | GatewayIntents.MessageContent,
                            LogLevel = LogSeverity.Info
                        };
                        services.AddSingleton(discordConfig);
                        services.AddSingleton<DiscordSocketClient>();
                        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
                        services.AddSingleton<INotificationService, NotificationService>();
                        services.AddHostedService<Worker>();
                    })
                    .Build();

                Log.Information("Applying database migrations...");
                var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
                using (var scope = scopeFactory.CreateScope())
                {
                    try
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                        await dbContext.Database.MigrateAsync();
                        Log.Information("Database migrations applied or database up-to-date.");
                    }
                    catch (Exception ex)
                    {
                        Log.Fatal(ex, "Database migration failed.");
                        Environment.Exit(1);
                    }
                }

                Log.Information("Starting application host...");
                await host.RunAsync();

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application host terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
