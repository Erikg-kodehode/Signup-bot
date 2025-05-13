using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MorningSignInBot.Services;
using Serilog;
using System.Threading.Tasks;

namespace ConsoleHost;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Configure Serilog first for early logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting up ConsoleHost");
            var host = CreateHostBuilder(args).Build();
        
        // Get the bot service and start it
        var botService = host.Services.GetRequiredService<IBotService>();
        
        Console.WriteLine("Starting Discord bot...");
        await botService.StartAsync();
        
        Console.WriteLine("Bot is running. Press Ctrl+C to stop.");
        
        // Set up cancellation token to handle Ctrl+C
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) => {
            Console.WriteLine("Shutting down...");
            cts.Cancel();
            e.Cancel = true; // Prevent the process from terminating immediately
        };
        
        try
        {
            // Keep the application running until cancellation is requested
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // This is expected when cancellation is requested
            Log.Information("Operation was canceled");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            // Ensure bot is stopped properly
            await botService.StopAsync();
            
            // Dispose the host
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                host.Dispose();
                
            Log.Information("Bot has been shut down");
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(options => 
                {
                    options.ListenAnyIP(8080);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/health", new HealthCheckOptions
                        {
                            AllowCachingResponses = false
                        });
                    });
                });
            })
            .ConfigureServices(ConfigureServices)
            .UseSerilog((hostContext, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(hostContext.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console();
            });
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // Add bot service
        services.AddSingleton<IBotService, BotService>();
        
        // Add health check service
        services.AddHealthChecks()
            .AddCheck("BotHealthCheck", () => 
            {
                // Get the bot service and check if it's running
                var botService = services.BuildServiceProvider().GetService<IBotService>();
                return botService != null && botService.IsRunning 
                    ? HealthCheckResult.Healthy("Bot is running")
                    : HealthCheckResult.Unhealthy("Bot is not running");
            });
    }
}

