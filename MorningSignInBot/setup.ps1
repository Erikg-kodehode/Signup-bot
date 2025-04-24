<#
.SYNOPSIS
    Setup script for MorningSignInBot
.DESCRIPTION
    Helps setup and manage the MorningSignInBot environment for both development and production
#>

param(
    [Parameter(Position = 0)]
    [ValidateSet("setup", "start", "backup", "restore", "logs", "status", "help")]
    [string]$Command = "help",
    [Parameter(Position = 1)]
    [string]$Parameter
)

# Colors for output
$Colors = @{
    Success = 'Green'
    Info    = 'Cyan'
    Warning = 'Yellow'
    Error   = 'Red'
}

# Version information
$Version = "1.0.0"

function Write-ColorMessage {
    param(
        [string]$Message,
        [string]$Color
    )
    Write-Host $Message -ForegroundColor $Color
}

function Test-Prerequisites {
    # Check if Docker is installed
    if (-not (Get-Command "docker" -ErrorAction SilentlyContinue)) {
        Write-ColorMessage "Error: Docker is not installed or not in PATH. Please install Docker first." $Colors.Error
        return $false
    }

    # Check if Docker Compose is installed
    if (-not (Get-Command "docker-compose" -ErrorAction SilentlyContinue)) {
        Write-ColorMessage "Error: Docker Compose is not installed or not in PATH. Please install Docker Compose first." $Colors.Error
        return $false
    }

    # Check if Docker daemon is running
    try {
        docker info | Out-Null
    }
    catch {
        Write-ColorMessage "Error: Docker daemon is not running. Please start Docker first." $Colors.Error
        return $false
    }

    return $true
}

function Test-Environment {
    param(
        [string]$EnvType,
        [string]$CommandName
    )
    
    if ($EnvType -and $EnvType -notin @("dev", "prod")) {
        Write-ColorMessage "Error: Invalid environment type '$EnvType' for $CommandName command. Use 'dev' or 'prod'." $Colors.Error
        return $false
    }
    return $true
}

function Test-EnvFile {
    param(
        [string]$EnvFile
    )
    
    if (-not (Test-Path $EnvFile)) {
        Write-ColorMessage "Error: Environment file '$EnvFile' not found." $Colors.Error
        return $false
    }

    $requiredVars = @(
        "DISCORD_BOT_TOKEN",
        "DISCORD_CHANNEL_ID",
        "DISCORD_GUILD_ID",
        "DISCORD_ADMIN_ROLE_ID"
    )

    $content = Get-Content $EnvFile -Raw
    $missingVars = @()

    foreach ($var in $requiredVars) {
        if ($content -notmatch "$var=.+") {
            $missingVars += $var
        }
    }

    if ($missingVars.Count -gt 0) {
        Write-ColorMessage "Error: Missing required variables in $EnvFile:" $Colors.Error
        foreach ($var in $missingVars) {
            Write-ColorMessage "  - $var" $Colors.Error
        }
        return $false
    }

    return $true
}

function Test-DockerComposeFile {
    param(
        [string]$ComposeFile,
        [string]$EnvType
    )
    
    if (-not (Test-Path $ComposeFile)) {
        Write-ColorMessage "Error: Docker Compose file '$ComposeFile' not found." $Colors.Error
        return $false
    }

    try {
        # Validate docker-compose file
        $result = docker-compose -f $ComposeFile config --quiet
        if ($LASTEXITCODE -ne 0) {
            Write-ColorMessage "Error: Invalid Docker Compose configuration in $ComposeFile" $Colors.Error
            return $false
        }
    }
    catch {
        Write-ColorMessage "Error validating Docker Compose file: $_" $Colors.Error
        return $false
    }

    return $true
}

function Initialize-Directories {
    $requiredDirs = @("data", "logs")
    
    foreach ($dir in $requiredDirs) {
        if (-not (Test-Path $dir)) {
            Write-ColorMessage "Creating required directory: $dir" $Colors.Info
            New-Item -ItemType Directory -Path $dir | Out-Null
        }
    }
}

function Remove-OldBackups {
    param(
        [int]$KeepCount = 5
    )

    $backupDir = "backups"
    if (-not (Test-Path $backupDir)) {
        return
    }

    $backups = Get-ChildItem -Path $backupDir -Filter "backup_*.tar.gz" | 
               Sort-Object LastWriteTime -Descending | 
               Select-Object -Skip $KeepCount

    foreach ($backup in $backups) {
        try {
            Remove-Item $backup.FullName -Force
            Write-ColorMessage "Removed old backup: $($backup.Name)" $Colors.Info
        }
        catch {
            Write-ColorMessage "Error removing old backup $($backup.Name): $_" $Colors.Warning
        }
    }
}

function Setup-Environment {
    param([string]$EnvType = "prod")

    if (-not (Test-Environment $EnvType "setup")) {
        return
    }

    $sourceFile = ".env.example"
    $targetFile = if ($EnvType -eq "dev") { ".env.dev" } else { ".env" }

    if (Test-Path $targetFile) {
        Write-ColorMessage "Warning: $targetFile already exists. Do you want to overwrite it? (Y/N)" $Colors.Warning
        $answer = Read-Host
        if ($answer -ne "Y") {
            Write-ColorMessage "Setup cancelled." $Colors.Info
            return
        }
    }

    Copy-Item $sourceFile $targetFile
    Write-ColorMessage "$targetFile created from example template." $Colors.Success
    Write-ColorMessage "Please edit $targetFile with your configuration values." $Colors.Info

    # Try to open in default editor
    try {
        Start-Process $targetFile
    }
    catch {
        Write-ColorMessage "Could not open file in default editor. Please edit $targetFile manually." $Colors.Warning
    }
}

function Start-Bot {
    param([string]$EnvType = "prod")

    if (-not (Test-Environment $EnvType "start")) {
        return
    }

    $envFile = if ($EnvType -eq "dev") { ".env.dev" } else { ".env" }
    $composeFile = if ($EnvType -eq "dev") { "docker-compose.dev.yml" } else { "docker-compose.yml" }

    if (-not (Test-Path $envFile)) {
        Write-ColorMessage "Error: $envFile not found. Run 'setup.ps1 setup $EnvType' first." $Colors.Error
        return
    }

    if (-not (Test-EnvFile $envFile)) {
        return
    }

    if (-not (Test-DockerComposeFile $composeFile $EnvType)) {
        return
    }

    Write-ColorMessage "Starting bot in $EnvType mode..." $Colors.Info
    try {
        if ($EnvType -eq "dev") {
            docker-compose -f $composeFile up --build
        }
        else {
            docker-compose -f $composeFile up --build -d
            
            # Wait a moment for container to start
            Start-Sleep -Seconds 5
            
            # Verify container is running
            $status = docker-compose -f $composeFile ps --quiet
            if (-not $status) {
                Write-ColorMessage "Warning: Container may not have started properly. Check logs for details." $Colors.Warning
                Write-ColorMessage "Use '.\setup.ps1 logs $EnvType' to view the logs." $Colors.Info
            }
            else {
                Write-ColorMessage "Bot started successfully." $Colors.Success
            }
        }
    }
    catch {
        Write-ColorMessage "Error starting bot: $_" $Colors.Error
        Write-ColorMessage "Use '.\setup.ps1 logs $EnvType' to check for errors." $Colors.Info
    }
}

function Backup-Data {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupDir = "backups"
    
    if (-not (Test-Path $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir
    }

    Write-ColorMessage "Creating backup..." $Colors.Info
    
    # Stop containers if running
    $containersRunning = $false
    if (docker-compose ps) {
        $containersRunning = $true
        Write-ColorMessage "Stopping containers for backup..." $Colors.Warning
        docker-compose down
    }

    try {
        # Backup database
        if (Test-Path "data/signinbot.db") {
            Copy-Item "data/signinbot.db" "$backupDir/signinbot_$timestamp.db"
            Write-ColorMessage "Database backed up to $backupDir/signinbot_$timestamp.db" $Colors.Success
        }

        # Backup configuration
        if (Test-Path ".env") {
            Copy-Item ".env" "$backupDir/env_$timestamp.backup"
            Write-ColorMessage "Configuration backed up to $backupDir/env_$timestamp.backup" $Colors.Success
        }

        # Create archive
        tar -czf "$backupDir/backup_$timestamp.tar.gz" -C $backupDir "signinbot_$timestamp.db" "env_$timestamp.backup"
        Write-ColorMessage "Backup archive created: backup_$timestamp.tar.gz" $Colors.Success

        # Clean up individual backup files after archiving
        Remove-Item "$backupDir/signinbot_$timestamp.db" -ErrorAction SilentlyContinue
        Remove-Item "$backupDir/env_$timestamp.backup" -ErrorAction SilentlyContinue
        
        # Rotate old backups
        Remove-OldBackups -KeepCount 5
    }
    catch {
        Write-ColorMessage "Error during backup: $_" $Colors.Error
    }
    finally {
        # Restart containers if they were running
        if ($containersRunning) {
            Write-ColorMessage "Restarting containers..." $Colors.Warning
            docker-compose up -d
        }
    }
}

function Restore-Data {
    param([string]$BackupFile)

    $backupDir = "backups"
    
    # If no backup file specified, list available backups
    if (-not $BackupFile) {
        if (-not (Test-Path $backupDir)) {
            Write-ColorMessage "No backups found. Backup directory does not exist." $Colors.Warning
            return
        }

        $backups = Get-ChildItem -Path $backupDir -Filter "backup_*.tar.gz" | 
                  Sort-Object LastWriteTime -Descending

        if ($backups.Count -eq 0) {
            Write-ColorMessage "No backups found in $backupDir" $Colors.Warning
            return
        }

        Write-ColorMessage "Available backups:" $Colors.Info
        $backups | ForEach-Object {
            Write-ColorMessage "  $($_.Name)" $Colors.Info
        }
        return
    }

    # Validate backup file exists
    $backupPath = Join-Path $backupDir $BackupFile
    if (-not (Test-Path $backupPath)) {
        Write-ColorMessage "Backup file not found: $BackupFile" $Colors.Error
        return
    }

    Write-ColorMessage "Starting restore process..." $Colors.Info

    # Stop containers if running
    $containersRunning = $false
    if (docker-compose ps) {
        $containersRunning = $true
        Write-ColorMessage "Stopping containers for restore..." $Colors.Warning
        docker-compose down
    }

    try {
        # Create temp directory for extraction
        $tempDir = "temp_restore"
        if (Test-Path $tempDir) {
            Remove-Item -Recurse -Force $tempDir
        }
        New-Item -ItemType Directory -Path $tempDir | Out-Null

        # Extract backup
        tar -xzf $backupPath -C $tempDir

        # Create data directory if it doesn't exist
        if (-not (Test-Path "data")) {
            New-Item -ItemType Directory -Path "data" | Out-Null
        }

        # Restore database
        $dbFile = Get-ChildItem -Path $tempDir -Filter "signinbot_*.db" | Select-Object -First 1
        if ($dbFile) {
            Copy-Item $dbFile.FullName "data/signinbot.db" -Force
            Write-ColorMessage "Database restored successfully" $Colors.Success
        }

        # Restore environment file
        $envFile = Get-ChildItem -Path $tempDir -Filter "env_*.backup" | Select-Object -First 1
        if ($envFile) {
            Copy-Item $envFile.FullName ".env" -Force
            Write-ColorMessage "Environment configuration restored successfully" $Colors.Success
        }
    }
    catch {
        Write-ColorMessage "Error during restore: $_" $Colors.Error
    }
    finally {
        # Cleanup
        if (Test-Path $tempDir) {
            Remove-Item -Recurse -Force $tempDir
        }

        # Restart containers if they were running
        if ($containersRunning) {
            Write-ColorMessage "Restarting containers..." $Colors.Warning
            docker-compose up -d
        }
    }
}

function Show-Logs {
    param([string]$EnvType = "prod")

    if (-not (Test-Environment $EnvType "logs")) {
        return
    }

    $composeFile = if ($EnvType -eq "dev") { "docker-compose.dev.yml" } else { "docker-compose.yml" }
    
    Write-ColorMessage "Showing logs for $EnvType environment..." $Colors.Info
    try {
        docker-compose -f $composeFile logs --tail=100 -f
    }
    catch {
        Write-ColorMessage "Error showing logs: $_" $Colors.Error
    }
}

function Show-Status {
    param([string]$EnvType = "prod")

    if (-not (Test-Environment $EnvType "status")) {
        return
    }

    $composeFile = if ($EnvType -eq "dev") { "docker-compose.dev.yml" } else { "docker-compose.yml" }
    
    Write-ColorMessage "Checking bot status for $EnvType environment..." $Colors.Info
    try {
        # Get container status
        $status = docker-compose -f $composeFile ps --quiet
        if (-not $status) {
            Write-ColorMessage "Bot is not running." $Colors.Warning
            return
        }

        # Show detailed status
        $containers = docker-compose -f $composeFile ps
        Write-ColorMessage "`nContainer Status:" $Colors.Info
        Write-Host $containers

        # Show resource usage
        Write-ColorMessage "`nResource Usage:" $Colors.Info
        docker stats --no-stream $(docker-compose -f $composeFile ps -q)

        # Check logs for any recent errors
        Write-ColorMessage "`nRecent Errors (last hour):" $Colors.Info
        docker-compose -f $composeFile logs --since 1h | Select-String -Pattern "error", "Error", "ERROR" -Context 0,1
    }
    catch {
        Write-ColorMessage "Error checking status: $_" $Colors.Error
    }
}

function Show-Help {
    Write-ColorMessage "MorningSignInBot Setup Script v$Version" $Colors.Info
    Write-ColorMessage "A management script for the Morning Sign-In Discord Bot" $Colors.Info
    
    Write-ColorMessage "`nCommands:" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 setup [dev|prod]  - Create environment file" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 start [dev|prod]  - Start the bot" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 backup           - Backup data and configuration" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 restore [file]   - Restore from backup (lists backups if no file specified)" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 logs [dev|prod]  - Show container logs" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 status [dev|prod]  - Show bot status and health" $Colors.Info
    Write-ColorMessage "  .\setup.ps1 help             - Show this help message" $Colors.Info
    
    Write-ColorMessage "`nExamples:" $Colors.Info
    Write-ColorMessage "  Development:" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 setup dev        - Setup development environment" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 start dev        - Start development environment" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 logs dev         - Show development logs" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 status dev       - Check development bot status" $Colors.Info
    
    Write-ColorMessage "`n  Production:" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 setup prod       - Setup production environment" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 start prod       - Start production environment" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 status prod      - Check production bot status" $Colors.Info
    
    Write-ColorMessage "`n  Backup/Restore:" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 backup           - Create backup of data and configuration" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 restore          - List available backups" $Colors.Info
    Write-ColorMessage "    .\setup.ps1 restore backup_20250424_132600.tar.gz  - Restore specific backup" $Colors.Info
}

# Check prerequisites before executing any commands
if (-not (Test-Prerequisites)) {
    exit 1
}

# Initialize required directories
Initialize-Directories

# Main script execution
switch ($Command) {
    "setup" { Setup-Environment $Parameter }
    "start" { Start-Bot $Parameter }
    "backup" { Backup-Data }
    "restore" { Restore-Data $Parameter }
    "logs" { Show-Logs $Parameter }
    "status" { Show-Status $Parameter }
    "help" { Show-Help }
}
