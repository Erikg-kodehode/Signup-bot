using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // Required for Log.Information etc. usage below
using MorningSignInBot.Configuration; // Use correct namespace
using MorningSignInBot.Data;         // Use correct namespace
using MorningSignInBot.Services;    // Use correct namespace
using Serilog;
using System;
using System.Threading.Tasks; // Required for Task

namespace MorningSignInBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Configure Serilog for bootstrap logging first
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Host.CreateDefaultBuilder(args).Build().Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting host builder...");

                IHost host = Host.CreateDefaultBuilder(args)
                    .UseSerilog((context, services, configuration) => configuration
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
                                       | GatewayIntents.MessageContent, // Ensure necessary intents
                            LogLevel = LogSeverity.Info // Let Serilog handle filtering
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

                // Apply Migrations
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

                // Run Host
                Log.Information("Starting application host...");
                await host.RunAsync();

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application host terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush(); // Ensure logs are flushed on exit
            }
        }
    }
}