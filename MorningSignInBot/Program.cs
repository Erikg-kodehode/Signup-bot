using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            var bootstrapConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
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
                        // .ReadFrom.Services(services) // Keep this commented out to avoid CS1503 for now
                        .Enrich.FromLogContext())
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.Configure<DiscordSettings>(hostContext.Configuration.GetSection("Discord"));
                        services.Configure<DatabaseSettings>(hostContext.Configuration.GetSection("Database"));

                        services.AddDbContext<SignInContext>();

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
                        Environment.Exit(1); // Exit if migration fails
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