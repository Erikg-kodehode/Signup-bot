# update-database.ps1
# Script to run Entity Framework Core database migrations for MorningSignInBot
# Created on $(Get-Date -Format "yyyy-MM-dd")

[CmdletBinding()]
param(
    [string]$ProjectPath = "MorningSignInBot.Data",
    [string]$StartupProjectPath = "MorningSignInBot",
    [string]$MigrationName = "",
    [switch]$DetailedOutput
)

# Function to display messages with timestamp
function Write-Log {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [ValidateSet("Info", "Warning", "Error")]
        [string]$Level = "Info"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "Info" { "White" }
        "Warning" { "Yellow" }
        "Error" { "Red" }
    }
    
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

# Function to resolve project file paths
function Resolve-ProjectPath {
    param (
        [string]$PathInput,
        [string]$ProjectRoot
    )

    # Strip quotes if present
    $PathInput = $PathInput.Trim('"', "'")
    
    # Handle different path formats
    if ($PathInput -match '\.csproj$') {
        # Already a .csproj file path
        $fullPath = $PathInput
    } else {
        # Try as a project name (add .csproj)
        $fullPath = "$PathInput\$PathInput.csproj"
        
        # If that doesn't exist, try just adding .csproj
        if (-not (Test-Path -Path (Join-Path -Path $ProjectRoot -ChildPath $fullPath))) {
            $fullPath = "$PathInput.csproj"
        }
    }
    
    # Convert to absolute path if it's a relative path
    if (-not [System.IO.Path]::IsPathRooted($fullPath)) {
        $fullPath = Join-Path -Path $ProjectRoot -ChildPath $fullPath
    }
    
    # Verify the path exists
    if (Test-Path -Path $fullPath) {
        Write-Log "Project path resolved to: $fullPath" -Level "Info"
        return $fullPath
    } else {
        Write-Log "Project path not found: $fullPath" -Level "Warning"
        
        # Try one more approach - direct search in project root
        $possiblePath = Get-ChildItem -Path $ProjectRoot -Recurse -Filter "$PathInput.csproj" -File | Select-Object -First 1 -ExpandProperty FullName
        if ($possiblePath) {
            Write-Log "Found project by name: $possiblePath" -Level "Info"
            return $possiblePath
        }
        
        return $null
    }
}
    
# Main Script Execution
try {
    # Save the current directory to return to it at the end
    $originalDirectory = Get-Location
    
    # Get the script directory and set it as the current directory
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = $scriptDirectory
    Set-Location -Path $projectRoot
    Write-Log "Working directory set to: $projectRoot"
    
    # Resolve project paths using the new function
    Write-Log "Resolving data project path: $ProjectPath"
    $resolvedProjectPath = Resolve-ProjectPath -PathInput $ProjectPath -ProjectRoot $projectRoot
    
    if (-not $resolvedProjectPath) {
        Write-Log "Project path '$ProjectPath' not found. Please provide a valid path." -Level "Error"
        throw "Project path not found."
    }
    
    $ProjectPath = $resolvedProjectPath
    Write-Log "Resolved data project path: $ProjectPath"
    
    # Resolve startup project path
    Write-Log "Resolving startup project path: $StartupProjectPath"
    $resolvedStartupPath = Resolve-ProjectPath -PathInput $StartupProjectPath -ProjectRoot $projectRoot
    
    if (-not $resolvedStartupPath) {
        Write-Log "Startup project path '$StartupProjectPath' not found. Please provide a valid path." -Level "Error"
        throw "Startup project path not found."
    }
    
    $StartupProjectPath = $resolvedStartupPath
    Write-Log "Resolved startup project path: $StartupProjectPath"
    
    # Build the command
    $command = "dotnet ef database update"
    
    # Add migration name if specified
    if ($MigrationName) {
        $command += " $MigrationName"
    }
    
    # Add project and startup project parameters
    $command += " --project `"$ProjectPath`" --startup-project `"$StartupProjectPath`""
    
    # Add verbose flag if needed
    if ($DetailedOutput) {
        $command += " --verbose"
    }
    
    # Execute the command
    Write-Log "Executing: $command"
    Invoke-Expression $command
    
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Database update completed successfully!" -Level "Info"
    }
    else {
        Write-Log "Database update failed with exit code: $LASTEXITCODE" -Level "Error"
    }
}
catch {
    Write-Log "An error occurred: $_" -Level "Error"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" -Level "Error"
    exit 1
}
finally {
    # Return to the original directory
    Set-Location -Path $originalDirectory
}

