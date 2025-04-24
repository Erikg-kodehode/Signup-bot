# Morning Sign-In Bot (Norwegian)

A simple Discord bot written in C# using Discord.Net for handling daily work sign-ins.

## Features

- Posts a sign-in message every weekday morning at a configured time.
- **Automatically deletes the previous day's sign-in message** to reduce clutter.
- Provides buttons for signing in ("Kontor" and "Hjemmekontor").
- Stores sign-in records (User ID, Username, Timestamp (UTC), Type) in a local SQLite database.
- Prevents duplicate sign-ins per day.
- Provides admin commands to view sign-ins (optionally filtered by role/date), delete entries, list missing users by role, and trigger the message manually. Uses Autocomplete for date suggestions.
- Skips posting on weekends (Saturday/Sunday) and Norwegian public holidays.
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
3.  **Install Packages:** If you haven't already, run `dotnet restore`. (Requires packages like Discord.Net, EFCore.Sqlite, Serilog, Nager.Date).
4.  **Configure:**
    - **Bot Token (Secret):** Initialize user secrets (`dotnet user-secrets init`) and set your token (`dotnet user-secrets set "Discord:BotToken" "YOUR_NEW_BOT_TOKEN"`). Remove the token from `appsettings.json`.
    - **`appsettings.json`:**
      - `Discord:TargetChannelId`: Set the ID of the channel where the daily message should be posted.
      - `Discord:SignInHour`: Hour (0-23) to post the message (e.g., 8 for 8 AM).
      - `Discord:SignInMinute`: Minute (0-59) to post the message (e.g., 0 for :00).
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
7.  **Invite Bot:** Generate an OAuth2 URL (via Discord Developer Portal -> Your App -> OAuth2 -> URL Generator) with scopes `bot` and `applications.commands` and necessary permissions (`View Channels`, `Send Messages`, **`Manage Messages`** (for deleting old messages)) and invite it to your server. Ensure the bot has permission to view members if using the `/logg mangler` or `/logg vis [rolle]` commands (requires Server Members Intent enabled in Developer Portal).

## Configuration Files

- **`appsettings.json`**: Main configuration for non-sensitive settings.
- **User Secrets (`secrets.json`)**: Used during development for the `Discord:BotToken`.
- **Environment Variables (Production)**: For production, set `Discord__BotToken` (note double underscore) and potentially override other settings using environment variables (e.g., `Discord__TargetChannelId`, `Database__Path`).

## Admin Commands

These commands require the user to have the specific role configured in `Interactions/AdminCommands.cs` (**replace the placeholder `123456789012345678` with your Role ID or use `[RequireRole("YourRoleName")]`**). Commands start with `/logg`.

- `/logg vis [rolle?] [dato?]`

  - Viser hvem som logget seg inn. Kan filtreres på rolle og/eller dato.
  - `rolle`: Valgfri. Viser kun brukere med denne rollen. (Discord viser liste).
  - `dato`: Valgfri. Dato i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag ("I dag", "I går", formater). Hvis utelatt, vises dagens innsjekkinger.
  - Eksempel (alle i dag): `/logg vis`
  - Eksempel (rolle i dag): `/logg vis rolle:@Ansatt`
  - Eksempel (alle på dato): `/logg vis dato:23-04-2025`
  - Eksempel (rolle på dato): `/logg vis rolle:@Ansatt dato:23-04-2025`

- `/logg sendnå`

  - Tvinger botten til å sende dagens innsjekkingsmelding umiddelbart (vil også forsøke å slette forrige melding først).
  - Nyttig for testing eller hvis den planlagte sendingen feilet.

- `/logg slett [bruker] [dato]`

  - Sletter innsjekkingen for en spesifikk `bruker` på en gitt `dato`.
  - `bruker`: Velg brukeren fra listen Discord viser.
  - `dato`: Skriv inn datoen i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag.
  - Eksempel: `/logg slett bruker:@OlaNordmann dato:23-04-2025`

- `/logg mangler [rolle] [dato?]`
  - Viser hvilke brukere som har den spesifiserte `rolle` men som _ikke_ sjekket inn på den gitte `dato`.
  - `rolle`: Velg rollen fra listen Discord viser.
  - `dato`: Valgfri. Dato i format `dd-MM-yyyy` eller `yyyy-MM-dd`. Tilbyr forslag. Hvis utelatt, vises for dagens dato.
  - Krever at botten har tilgang til serverens medlemsliste (Server Members Intent).
  - Eksempel: `/logg mangler rolle:@Ansatt dato:23-04-2025`
  - Eksempel: `/logg mangler rolle:@Alle` (viser for i dag)

## Database

- Uses SQLite.
- The database file (`signins.db` by default) is located relative to the execution directory (usually `bin/Debug/.../Data/signins.db` during development). Configure the path in `appsettings.json` (`Database:Path`) if needed.

## State File

- Uses `bot_message_state.json` in the execution directory to store the ID of the last sent sign-in message for deletion purposes.
