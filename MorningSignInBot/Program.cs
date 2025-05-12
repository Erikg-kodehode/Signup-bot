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
            // Add detailed startup logging
            Console.WriteLine($"---> Reading TEST_CHECK env var: {Environment.GetEnvironmentVariable("TEST_CHECK")}");
            Console.WriteLine($"---> Application starting at {DateTime.Now}");
            Console.WriteLine($"---> Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"---> DOTNET_ENVIRONMENT: {Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}");
            Console.WriteLine($"---> Discord__TargetChannelId: {Environment.GetEnvironmentVariable("Discord__TargetChannelId")}");
            Console.WriteLine($"---> Secret exists: {File.Exists("/run/secrets/discord_bot_token")}");
            
            try 
            {
                if (File.Exists("/run/secrets/discord_bot_token")) 
                {
                    Console.WriteLine("---> Bot token secret file found. Length: " + new FileInfo("/run/secrets/discord_bot_token").Length);
                    string secretContent = File.ReadAllText("/run/secrets/discord_bot_token").Trim();
                    Console.WriteLine($"---> Secret content length: {secretContent.Length}, first 5 chars: {(secretContent.Length > 5 ? secretContent.Substring(0, 5) : "too short")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"---> Error reading secret: {ex.Message}");
            }

            // Bootstrap config for Serilog
            Console.WriteLine("---> Building bootstrap configuration");
            string basePath = Directory.GetCurrentDirectory();
            string mainSettingsPath = Path.Combine(basePath, "appsettings.json");
            string envSettingsPath = Path.Combine(basePath, $"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json");
            
            Console.WriteLine($"---> Main settings file exists: {File.Exists(mainSettingsPath)}");
            Console.WriteLine($"---> Environment settings file exists: {File.Exists(envSettingsPath)}");
            
            var bootstrapConfig = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables() // Reads environment variables including those from Docker
                .Build();
                
            Console.WriteLine("---> Configuration built successfully");

            Console.WriteLine("---> Configuring Serilog");
            try 
            {
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(bootstrapConfig)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .CreateBootstrapLogger();
                    
                Console.WriteLine("---> Serilog bootstrap logger created successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"---> Error configuring Serilog: {ex.Message}");
            }

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
                            try
                            {
                                var logger = serviceProvider.GetRequiredService<ILogger<SignInContext>>();
                                var dbSettings = serviceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
                                string relativePath = dbSettings.Path;
                                string basePath = AppContext.BaseDirectory;
                                string dbPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                                
                                logger.LogInformation("Database path configured: {DbPath}", dbPath);
                                Console.WriteLine($"---> Database path: {dbPath}");

                                string? directory = Path.GetDirectoryName(dbPath);
                                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                {
                                    try 
                                    { 
                                        Directory.CreateDirectory(directory);
                                        logger.LogInformation("Created database directory: {Directory}", directory);
                                        Console.WriteLine($"---> Created database directory: {directory}");
                                    } 
                                    catch (Exception ex) 
                                    { 
                                        logger.LogError(ex, "Failed to create database directory: {Directory}", directory);
                                        Console.WriteLine($"---> Error creating database directory: {ex.Message}");
                                    }
                                }
                                options.UseSqlite($"Data Source={dbPath}");
                                logger.LogInformation("Database connection string configured");
                                Console.WriteLine("---> Database connection configured successfully");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"---> Error configuring database: {ex.Message}");
                                throw;
                            }
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
                Console.WriteLine("---> Starting database migrations");
                var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
                using (var scope = scopeFactory.CreateScope())
                {
                    try
                    {
                        Console.WriteLine("---> Retrieving DbContext");
                        var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                        Console.WriteLine("---> Running migrations");
                        await dbContext.Database.MigrateAsync();
                        Log.Information("Database migrations applied or database up-to-date.");
                        Console.WriteLine("---> Database migrations completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Fatal(ex, "Database migration failed.");
                        Console.WriteLine($"---> Database migration failed: {ex.Message}");
                        Console.WriteLine($"---> Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"---> Inner exception: {ex.InnerException.Message}");
                        }
                        Environment.Exit(1);
                    }
                }

                Log.Information("Starting application host...");
                Console.WriteLine("---> Starting application host");
                await host.RunAsync();

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application host terminated unexpectedly.");
                Console.WriteLine($"---> FATAL ERROR: {ex.Message}");
                Console.WriteLine($"---> Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"---> Inner exception: {ex.InnerException.Message}");
                }
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
