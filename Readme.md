# Morning Sign-In Bot - Console Setup

The bot has been simplified to run as a console application. The WPF launcher has been removed in favor of a more straightforward configuration approach using appsettings.json and environment variables.

## Configuration

Configure the bot using either:

1. appsettings.json:
```json
{
  "Discord": {
    "BotToken": "YOUR_BOT_TOKEN",
    "TargetChannelId": "YOUR_CHANNEL_ID",
    "SignInHour": 8,
    "SignInMinute": 0,
    "Guilds": [
      {
        "GuildId": YOUR_GUILD_ID,
        "AdminRoleId": YOUR_ADMIN_ROLE_ID,
        "GuildName": "Your Guild Name"
      }
    ]
  }
}
```

2. Environment Variables:
```
Discord__BotToken=YOUR_BOT_TOKEN
Discord__TargetChannelId=YOUR_CHANNEL_ID
Discord__SignInHour=8
Discord__SignInMinute=0
```

## Running the Bot

1. Navigate to the ConsoleHost directory
2. Run `dotnet run`

The bot will start and display its status in the console. Use Ctrl+C to gracefully shut down the bot.

## Health Checks

The bot includes a health check endpoint at `http://localhost:8080/health` which returns the current status of the bot.

## Logs

Logs are written to both the console and file:
- Console: Real-time logging output
- File: Written to /app/Logs/botlog-.txt (configurable in appsettings.json)

# Morning Sign-In Bot (Norwegian)

A simple Discord bot written in C# using Discord.Net for handling daily work sign-ins.

## Features

- Posts a sign-in message every weekday morning at a configured time.
- Automatically deletes the previous day's sign-in message to reduce clutter.
- Provides buttons for signing in ("Kontor" and "Hjemmekontor").
- Stores sign-in records (User ID, Username, Timestamp (UTC), Type) in a local SQLite database.
- Prevents duplicate sign-ins per day.
- Provides admin commands to view sign-ins (optionally filtered by role/date), delete entries, list missing users by role, and trigger the message manually. Uses Autocomplete for date suggestions.
- Skips posting on weekends (Saturday/Sunday) and Norwegian public holidays (using NordicHolidays.NET library).
- Uses Norwegian language for user interactions.

## Setup

### Local Development Setup

1.  **Prerequisites:**
    - .NET 8 SDK (or newer)
    - Git (optional, for version control)
2.  **Clone the Repository:**
    ```bash
    git clone <your-repository-url>
    cd MorningSignInBot
    ```
3.  **Install Packages:** If you haven't already, run `dotnet restore`. (Requires packages like Discord.Net, EFCore.Sqlite, Serilog, NordicHolidays.NET).
4.  **Configure:**
    - **Bot Token (Secret):** Initialize user secrets (`dotnet user-secrets init`) and set your token (`dotnet user-secrets set "Discord:BotToken" "YOUR_NEW_BOT_TOKEN"`). Remove the token from `appsettings.json`.
    - **`appsettings.json`:**
      - `Discord:TargetChannelId`: Set the ID of the channel where the daily message should be posted.
      - `Discord:SignInHour`: Hour (0-23) to post the message (e.g., 8 for 8 AM).
      - `Discord:SignInMinute`: Minute (0-59) to post the message (e.g., 0 for :00).
      - `Discord:Guilds`: Configuration for servers the bot will operate in (see Multi-Server Setup below)
      - `Database:Path`: (Optional) Change the relative path/filename for the SQLite DB file (default: `Data/signins.db`).
      - `Serilog`: (Optional) Adjust logging levels and file paths/retention.
5.  **Database Migration:** Apply the Entity Framework Core migrations to create the database file:
    ```bash
    dotnet ef database update
    ```
    (This command needs to be run from the directory containing the `.csproj` file).
6.  **Run the Bot:**
    ```bash
    dotnet run
    ```
7.  **Invite Bot:** Generate an OAuth2 URL (via Discord Developer Portal -> Your App -> OAuth2 -> URL Generator) with scopes `bot` and `applications.commands` and necessary permissions (`View Channels`, `Send Messages`, **`Manage Messages`**) and invite it to your server. Ensure the bot has permission to view members if using the `/logg mangler` or `/logg vis [rolle]` commands (requires Server Members Intent enabled in Developer Portal).

### Docker Swarm Deployment

The bot can be deployed using Docker Swarm for improved secrets management and container orchestration.

1. **Prerequisites:**
   - Docker Desktop with Swarm mode enabled
   - PowerShell 5.1 or later

2. **Initialize Docker Swarm:**
   ```powershell
   docker swarm init
   ```

3. **Configure Bot Token:**
   There are three ways to provide the bot token:
   
   a) **Production (Recommended):**
   ```powershell
   echo "your-bot-token" | docker secret create discord_bot_token -
   ```
   
   b) **Development:**
   - Leave token empty and input via console (enabled by `stdin_open: true` in docker-compose.yml)
   - Or use the secret method above with a development token
   
   c) **Change Existing Token:**
   ```powershell
   # Remove existing secret
   docker secret rm discord_bot_token
   # Create new secret
   echo "your-new-bot-token" | docker secret create discord_bot_token -
   # Update the service
   docker service update --force signin-bot_signin-bot
   ```

4. **Environment Configuration:**
   Create a `.env` file:
   ```powershell
   @"
   DOTNET_ENVIRONMENT=Production  # or Development
   TZ=Europe/Oslo
   DISCORD_CHANNEL_ID=your_channel_id
   DISCORD_GUILD_ID=your_guild_id
   DISCORD_ADMIN_ROLE_ID=your_admin_role_id
   DISCORD_GUILD_NAME=Your Server Name
   SIGNIN_HOUR=8
   SIGNIN_MINUTE=0
   "@ | Out-File -FilePath .env -Encoding UTF8
   ```

5. **Deploy the Stack:**
   ```powershell
   # Build the image
   docker-compose build
   
   # Deploy the stack
   docker stack deploy -c docker-compose.yml signin-bot
   ```

6. **Verify Deployment:**
   ```powershell
   # Check service status
   docker service ls
   
   # View logs
   docker service logs signin-bot_signin-bot
   ```

For detailed deployment instructions, see [DEPLOYMENT.md](./MorningSignInBot/DEPLOYMENT.md).

## Configuration Files

- **`appsettings.json`**: Main configuration for non-sensitive settings.
- **Docker Secrets**: In Docker Swarm deployment, the bot token is stored securely as a Docker secret at `/run/secrets/discord_bot_token`.
- **`.env` File**: Contains environment-specific configuration for Docker deployment.
- **User Secrets (`secrets.json`)**: Used during local development for the `Discord:BotToken`.
- **Environment Variables**: For non-Docker production, set `Discord__BotToken` (note double underscore) and potentially override other settings using environment variables (e.g., `Discord__TargetChannelId`, `Database__Path`).

## Multi-Server Setup

The bot now supports running on multiple Discord servers (guilds) with different admin roles for each. Configure this in `appsettings.json`:

```json
{
  "Discord": {
    "Guilds": [
      {
        "GuildId": 123456789012345678,
        "AdminRoleId": 123456789012345678,
        "GuildName": "Your Server Name"
      },
      {
        "GuildId": 987654321098765432,
        "AdminRoleId": 876543210987654321,
        "GuildName": "Another Server"
      }
    ]
  }
}
```

Each server configuration includes:
- `GuildId`: Discord server ID
- `AdminRoleId`: Role ID that has permission to use admin commands
- `GuildName`: Friendly name for logging (optional)

## Admin Commands

These commands require the user to have the admin role configured for their server in `appsettings.json` under the appropriate guild configuration. Commands start with `/admin`.

- `/admin vis [rolle?] [dato?]`

  - Viser hvem som logget seg inn. Kan filtreres på rolle og/eller dato.
  - `rolle`: Valgfri. Viser kun brukere med denne rollen. (Discord viser liste).
  - `dato`: Valgfri. Dato i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag ("I dag", "I går", formater). Hvis utelatt, vises dagens innsjekkinger.
  - Eksempel (alle i dag): `/admin vis`
  - Eksempel (rolle i dag): `/admin vis rolle:@Ansatt`
  - Eksempel (alle på dato): `/admin vis dato:23-04-2025`
  - Eksempel (rolle på dato): `/admin vis rolle:@Ansatt dato:23-04-2025`

- `/admin sendnå`

  - Tvinger botten til å sende dagens innsjekkingsmelding umiddelbart (vil også forsøke å slette forrige melding først).
  - Nyttig for testing eller hvis den planlagte sendingen feilet.

- `/admin slett [bruker] [dato]`

  - Sletter innsjekkingen for en spesifikk `bruker` på en gitt `dato`.
  - `bruker`: Velg brukeren fra listen Discord viser.
  - `dato`: Skriv inn datoen i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag.
  - Eksempel: `/admin slett bruker:@OlaNordmann dato:23-04-2025`

- `/admin mangler [rolle] [dato?]`
  - Viser hvilke brukere som har den spesifiserte `rolle` men som _ikke_ sjekket inn på den gitte `dato`.
  - `rolle`: Velg rollen fra listen Discord viser.
  - `dato`: Valgfri. Dato i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag. Hvis utelatt, vises for dagens dato.
  - Krever at botten har tilgang til serverens medlemsliste (Server Members Intent).
  - Eksempel: `/admin mangler rolle:@Ansatt dato:23-04-2025`
  - Eksempel: `/admin mangler rolle:@Alle` (viser for i dag)

## Database

- Uses SQLite.
- The database file (`signins.db` by default) is located relative to the execution directory (usually `bin/Debug/.../Data/signins.db` during development). Configure the path in `appsettings.json` (`Database:Path`) if needed.

## State File

- Uses `bot_message_state.json` in the execution directory to store the ID of the last sent sign-in message for deletion purposes.
