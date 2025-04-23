# Morning Sign-In Bot (Norwegian)

A simple Discord bot written in C# using Discord.Net for handling daily work sign-ins.

## Features

- Posts a sign-in message every weekday morning at a configured time.
- Provides buttons for signing in ("Kontor" and "Hjemmekontor").
- Stores sign-in records (User ID, Username, Timestamp (UTC), Type) in a local SQLite database.
- Prevents duplicate sign-ins per day.
- Provides admin commands to view sign-ins by date and trigger the message manually.
- Skips posting on weekends (Saturday/Sunday).
- Uses Norwegian language for user interactions.

## Setup

1.  **Prerequisites:**
    - .NET 8 SDK (or newer)
    - Git (optional, for version control)
2.  **Clone the Repository:**
    ```bash
    git clone <your-repository-url>
    cd MorningSignInBot
    ```
3.  **Configure:**
    - **Bot Token (Secret):** Initialize user secrets (`dotnet user-secrets init`) and set your token (`dotnet user-secrets set "Discord:BotToken" "YOUR_NEW_BOT_TOKEN"`). Remove the token from `appsettings.json`.
    - **`appsettings.json`:**
      - `Discord:TargetChannelId`: Set the ID of the channel where the daily message should be posted.
      - `Discord:SignInHour`: Hour (0-23) to post the message (e.g., 8 for 8 AM).
      - `Discord:SignInMinute`: Minute (0-59) to post the message (e.g., 0 for :00).
      - `Database:Path`: (Optional) Change the relative path/filename for the SQLite DB file (default: `Data/signins.db`).
      - `Serilog`: (Optional) Adjust logging levels and file paths/retention.
4.  **Database Migration:** Apply the Entity Framework Core migrations to create the database file:
    ```bash
    dotnet ef database update
    ```
    (This command needs to be run from the directory containing the `.csproj` file).
5.  **Run the Bot:**
    ```bash
    dotnet run
    ```
6.  **Invite Bot:** Generate an OAuth2 URL (via Discord Developer Portal -> Your App -> OAuth2 -> URL Generator) with scopes `bot` and `applications.commands` and necessary permissions (`View Channels`, `Send Messages`) and invite it to your server.

## Configuration Files

- **`appsettings.json`**: Main configuration for non-sensitive settings.
- **User Secrets (`secrets.json`)**: Used during development for the `Discord:BotToken`.
- **Environment Variables (Production)**: For production, set `Discord__BotToken` (note double underscore) and potentially override other settings using environment variables (e.g., `Discord__TargetChannelId`, `Database__Path`).

## Admin Commands

These commands require the user to have the specific role configured in `Interactions/AdminCommands.cs` (replace the placeholder `123456789012345678` with your Role ID or use `[RequireRole("YourRoleName")]`).

- `/innsjekk vis [dato]`

  - Viser hvem som logget seg inn på en gitt dato.
  - `dato`: Valgfri. Dato i formatet `ÅÅÅÅ-MM-DD`. Hvis utelatt, vises dagens innsjekkinger.
  - Eksempel: `/innsjekk vis dato:2025-04-23`
  - Eksempel: `/innsjekk vis` (viser for i dag)

- `/innsjekk sendnå`

  - Tvinger botten til å sende dagens innsjekkingsmelding umiddelbart.
  - Nyttig for testing eller hvis den planlagte sendingen feilet.

- `/innsjekk slett [bruker] [dato]`

  - **(Planlagt)** Sletter en spesifikk innsjekking.

- `/innsjekk mangler [rolle] [dato]`
  - **(Planlagt)** Viser hvem med en gitt rolle som _ikke_ sjekket inn.

## Database

- Uses SQLite.
- The database file (`signins.db` by default) is located relative to the execution directory (usually `bin/Debug/.../Data/signins.db` during development). Configure the path in `appsettings.json` (`Database:Path`) if needed.
