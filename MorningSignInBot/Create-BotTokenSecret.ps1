# Script to securely create a Discord bot token secret in Docker Swarm
# This avoids showing the token in command history

# Prompt for the token with a secure string input
$secureToken = Read-Host "Enter your Discord bot token" -AsSecureString

# Convert the secure string to plain text for Docker Secret creation
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
$token = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

# Check if the secret already exists and remove it if it does
$secretExists = docker secret ls --filter name=discord_bot_token -q
if ($secretExists) {
    Write-Host "Removing existing discord_bot_token secret..."
    docker secret rm discord_bot_token
}

# Create the secret
Write-Host "Creating new discord_bot_token secret..."
$token | docker secret create discord_bot_token -

# Verify the secret was created
Write-Host "Verifying secret creation..."
docker secret ls --filter name=discord_bot_token

# Clean up the token variable
$token = $null
[System.GC]::Collect()

Write-Host "`nSecret created successfully!"
Write-Host "You can now deploy your stack with:"
Write-Host "docker stack deploy -c docker-compose.yml signin-bot"

