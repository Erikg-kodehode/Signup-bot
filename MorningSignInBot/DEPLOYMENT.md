# Docker Swarm Deployment Instructions

This document outlines the steps to deploy the Morning Sign-In Bot in both development and production environments using Docker Swarm for secrets management.

## Prerequisites

- Docker Desktop installed with Swarm mode enabled
- PowerShell 5.1 or later
- Git for version control (optional)

## Development Environment Setup

1. **Create a development .env file**

   Create a `.env` file with your development configuration values:

   ```powershell
   @"
   DOTNET_ENVIRONMENT=Development
   TZ=Europe/Oslo
   DISCORD_CHANNEL_ID=your_channel_id
   DISCORD_GUILD_ID=your_guild_id
   DISCORD_ADMIN_ROLE_ID=your_admin_role_id
   DISCORD_GUILD_NAME=Your Development Server
   SIGNIN_HOUR=8
   SIGNIN_MINUTE=0
   "@ | Out-File -FilePath .env -Encoding UTF8
   ```

2. **Initialize Docker Swarm** (if not already done)

   ```powershell
   docker swarm init
   ```

3. **Create a secret for the Discord bot token**

   ```powershell
   echo "your-development-bot-token" | docker secret create discord_bot_token -
   ```

   Note: In development mode, you can also leave the token blank and enter it via console when prompted, as the compose file has `stdin_open` and `tty` settings enabled.

4. **Build and deploy the stack**

   ```powershell
   # Build the image
   docker-compose build

   # Deploy the stack
   docker stack deploy -c docker-compose.yml signin-bot-dev
   ```

5. **Verify the deployment**

   ```powershell
   # Check if the service is running
   docker service ls

   # Check logs
   docker service logs signin-bot-dev_signin-bot
   ```

## Production Environment Setup

1. **Modify docker-compose.yml for production** (optional)

   For production, you may want to remove or comment out the development-only settings:

   ```yaml
   # Comment out these lines
   # stdin_open: true
   # tty: true
   ```

2. **Create a production .env file**

   ```powershell
   @"
   DOTNET_ENVIRONMENT=Production
   TZ=Europe/Oslo
   DISCORD_CHANNEL_ID=your_production_channel_id
   DISCORD_GUILD_ID=your_production_guild_id
   DISCORD_ADMIN_ROLE_ID=your_production_admin_role_id
   DISCORD_GUILD_NAME=Your Production Server
   SIGNIN_HOUR=8
   SIGNIN_MINUTE=0
   "@ | Out-File -FilePath .env -Encoding UTF8
   ```

3. **Initialize Docker Swarm** (if not already done)

   ```powershell
   docker swarm init
   ```

4. **Create a secret for the Discord bot token**

   ```powershell
   # If you already have a secret, remove it first
   docker secret rm discord_bot_token

   # Create the new secret
   echo "your-production-bot-token" | docker secret create discord_bot_token -
   ```

5. **Build and deploy the stack**

   ```powershell
   # Build the image
   docker-compose build

   # Deploy the stack
   docker stack deploy -c docker-compose.yml signin-bot
   ```

6. **Verify the deployment**

   ```powershell
   # Check if the service is running
   docker service ls

   # Check logs
   docker service logs signin-bot_signin-bot
   ```

## Managing Your Deployment

### Updating the Bot Token

To update the bot token:

```powershell
# Remove the existing secret
docker secret rm discord_bot_token

# Create a new secret
echo "your-new-bot-token" | docker secret create discord_bot_token -

# Update the service to use the new secret
docker service update --force signin-bot_signin-bot
```

### Viewing Logs

```powershell
# View logs in real-time
docker service logs -f signin-bot_signin-bot

# View only the last 100 lines
docker service logs --tail 100 signin-bot_signin-bot
```

### Managing the Stack

```powershell
# List all stacks
docker stack ls

# List all services in a stack
docker stack services signin-bot

# Remove the stack
docker stack rm signin-bot
```

### Troubleshooting

1. **Check if the service is running**

   ```powershell
   docker service ls
   ```

2. **Check service details**

   ```powershell
   docker service inspect signin-bot_signin-bot
   ```

3. **Check secrets**

   ```powershell
   docker secret ls
   ```

4. **Check volume status**

   ```powershell
   docker volume ls
   ```

5. **If the bot can't connect to Discord**

   Verify the secret was created correctly:
   
   ```powershell
   docker secret ls
   ```
   
   Check the logs for authentication errors:
   
   ```powershell
   docker service logs signin-bot_signin-bot
   ```

## Backup and Restore

To back up data:

```powershell
# Find the container ID
$containerId = docker ps --filter name=signin-bot -q

# Copy data from the container
docker cp ${containerId}:/app/data ./backup-data
docker cp ${containerId}:/app/logs ./backup-logs
```

To restore from backup:

```powershell
# Find the container ID
$containerId = docker ps --filter name=signin-bot -q

# Copy data to the container
docker cp ./backup-data/. ${containerId}:/app/data
docker cp ./backup-logs/. ${containerId}:/app/logs
```

## Security Considerations

- The bot token is stored securely in Docker Swarm secrets
- The token is mounted as a file, not exposed in environment variables
- Only non-sensitive configuration is stored in environment variables
- Logs and data are persisted in Docker volumes

