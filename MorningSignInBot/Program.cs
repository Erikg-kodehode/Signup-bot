using Discord;
using Discord.Interactions; // Required for InteractionService
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore; // Required for EF Core
using Microsoft.Extensions.DependencyInjection; // Required for AddDbContext, AddSingleton
using MorningSignInBot;

// --- Dependency Injection Setup ---
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Add configuration options
        services.Configure<DiscordSettings>(hostContext.Configuration.GetSection("Discord"));

        // Add DbContext factory for SQLite
        // Use AddDbContextPool for potential performance gains if needed, AddDbContext is fine for now.
        services.AddDbContext<SignInContext>(); // EF Core context

        // Discord Client Configuration
        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
                           | GatewayIntents.GuildMessages // Needed for message context
                           | GatewayIntents.GuildMembers // Needed for user info on interaction
                           | GatewayIntents.MessageContent // Often needed with interactions/commands
        };
        services.AddSingleton(discordConfig);
        services.AddSingleton<DiscordSocketClient>();

        // Add Interaction Service
        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));

        // Add the main worker service (which now includes interaction handler setup)
        services.AddHostedService<Worker>();

        // Add our command module(s)
        // services.AddSingleton<AdminCommands>(); // Let InteractionService handle instantiation

    })
    .Build();


// --- Ensure Database is Created ---
// Get the scope factory
var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
// Create a scope to resolve services
using (var scope = scopeFactory.CreateScope())
{
    try
    {
        // Get the DbContext
        var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
        // Apply pending migrations automatically. Creates DB if it doesn't exist.
        await dbContext.Database.MigrateAsync();
        Console.WriteLine("Database migrations applied successfully or database is up-to-date.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while migrating the database: {ex.Message}");
        // Decide if the application should exit if the DB fails to initialize
        return; // Exit if DB migration fails
    }
}


// --- Run the Application ---
await host.RunAsync();