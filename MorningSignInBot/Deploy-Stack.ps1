# Deploy-Stack.ps1
# Script to load environment variables from .env file and deploy Docker stack

# Define the path to the .env file
$envFilePath = ".\.env"

# Check if the .env file exists
if (-not (Test-Path $envFilePath)) {
    Write-Error "Error: .env file not found at $envFilePath"
    exit 1
}

Write-Host "Loading environment variables from .env file..."

# Read the .env file, remove empty lines and comments, and set environment variables
Get-Content $envFilePath | ForEach-Object {
    $line = $_.Trim()
    # Skip empty lines and comments
    if ($line -ne "" -and -not $line.StartsWith("#")) {
        # Parse the variable name and value
        $parts = $line -split "=", 2
        if ($parts.Count -eq 2) {
            $name = $parts[0].Trim()
            $value = $parts[1].Trim()
            
            # Remove surrounding quotes if present
            if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            if ($value.StartsWith("'") -and $value.EndsWith("'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            
            # Set the environment variable
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
            Write-Host "Set $name environment variable"
        }
    }
}

# Ask for the stack name
$stackName = Read-Host -Prompt "Enter the stack name (default: morningsigninbot)"
if (-not $stackName) {
    $stackName = "morningsigninbot"
}

# Ask which compose file to use
Write-Host "Which compose file do you want to use?"
Write-Host "1. docker-compose.yml (production with secrets)"
Write-Host "2. docker-compose.override.yml (development with environment variables)"
$composeFileChoice = Read-Host -Prompt "Enter your choice (1 or 2)"

$composeFiles = ""
if ($composeFileChoice -eq "1") {
    $composeFiles = "-c docker-compose.yml"
} 
elseif ($composeFileChoice -eq "2") {
    Write-Host "Using development configuration..."
    $composeFiles = "-c docker-compose.yml -c docker-compose.override.yml"
    # Remove any existing secrets from the environment
    Remove-Item Env:\DOCKER_SECRET_* -ErrorAction SilentlyContinue
}
else {
    Write-Host "Invalid choice. Using docker-compose.yml as default."
    $composeFiles = "-c docker-compose.yml"
}

# Run the docker stack deploy command
Write-Host "Deploying stack $stackName..."
$deployCommand = "docker stack deploy $composeFiles --prune --resolve-image=always --with-registry-auth $stackName"
Write-Host "Executing: $deployCommand"

try {
    Invoke-Expression $deployCommand
    Write-Host "Stack deployment completed successfully!" -ForegroundColor Green
} 
catch {
    Write-Error "Error deploying stack: $_"
    exit 1
}

