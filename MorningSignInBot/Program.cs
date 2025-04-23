using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // Needed for ConfigurationBuilder
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MorningSignInBot.Configuration;
using MorningSignInBot.Data;
using MorningSignInBot.Services;
using Serilog;
using System;
using System.IO; // Needed for Directory.GetCurrentDirectory()
using System.Threading.Tasks;

namespace MorningSignInBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // --- MODIFICATION START ---
            // Build configuration separately first for bootstrap logging
            var bootstrapConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables() // Also read env variables for bootstrap if needed
                .Build();

            // Configure Serilog for bootstrap logging first, using the built config
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(bootstrapConfig) // Use the separately built config
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();
            // --- MODIFICATION END ---

            try
            {
                Log.Information("Starting host builder...");

                IHost host = Host.CreateDefaultBuilder(args) // CreateDefaultBuilder will load its own config (appsettings, env vars, user secrets, etc.)
                    .UseSerilog((context, services, configuration) => configuration // Configure Serilog using the host's context and config
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services)
                        .Enrich.FromLogContext())
                    .ConfigureServices((hostContext, services) =>
                    {
                        // Configurations
                        services.Configure<DiscordSettings>(hostContext.Configuration.GetSection("Discord"));
                        services.Configure<DatabaseSettings>(hostContext.Configuration.GetSection("Database"));

                        // Database
                        services.AddDbContext<SignInContext>();

                        // Discord Client
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

                        // Interaction Service
                        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));

                        // Custom Services
                        services.AddSingleton<INotificationService, NotificationService>();

                        // Background Worker
                        services.AddHostedService<Worker>();
                    })
                    .Build();

                // Apply Migrations (remains the same)
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
                        return; // Exit if migration fails
                    }
                }

                // Run Host (remains the same)
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