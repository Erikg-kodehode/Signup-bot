#!/bin/bash

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Print colored message
print_message() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Check prerequisites
check_prerequisites() {
    # Check if Docker is installed
    if ! command -v docker &> /dev/null; then
        print_message $RED "Error: Docker is not installed. Please install Docker first."
        exit 1
    fi

    # Check if Docker Compose is installed
    if ! command -v docker-compose &> /dev/null; then
        print_message $RED "Error: Docker Compose is not installed. Please install Docker Compose first."
        exit 1
    fi

    # Check if Docker daemon is running
    if ! docker info &> /dev/null; then
        print_message $RED "Error: Docker daemon is not running. Please start Docker first."
        exit 1
    fi
}

# Validate environment file
validate_env_file() {
    local env_file=$1
    local required_vars=("DISCORD_BOT_TOKEN" "DISCORD_CHANNEL_ID" "DISCORD_GUILD_ID" "DISCORD_ADMIN_ROLE_ID")
    local missing_vars=()

    while IFS= read -r line || [[ -n "$line" ]]; do
        if [[ $line =~ ^[^#]*= ]]; then
            var_name=$(echo "$line" | cut -d'=' -f1)
            var_value=$(echo "$line" | cut -d'=' -f2)
            if [[ " ${required_vars[@]} " =~ " ${var_name} " ]] && [[ "$var_value" == "your-"* || -z "$var_value" ]]; then
                missing_vars+=("$var_name")
            fi
        fi
    done < "$env_file"

    if [ ${#missing_vars[@]} -ne 0 ]; then
        print_message $YELLOW "Warning: The following required variables need to be configured in $env_file:"
        for var in "${missing_vars[@]}"; do
            print_message $YELLOW "- $var"
        done
        return 1
    fi
    return 0
}

# Setup environment
setup_environment() {
    local env_type=$1
    local source_file=".env.example"
    local target_file=".env"
    
    if [ "$env_type" == "dev" ]; then
        target_file=".env.dev"
    fi

    if [ -f "$target_file" ]; then
        print_message $YELLOW "Warning: $target_file already exists. Do you want to overwrite it? (y/N)"
        read -r answer
        if [ "$answer" != "y" ]; then
            print_message $CYAN "Setup cancelled."
            exit 0
        fi
    fi

    cp "$source_file" "$target_file"
    print_message $GREEN "$target_file created from example template."
    print_message $CYAN "Please edit $target_file with your configuration values."
    
    # Open the file in the default editor if available
    if [ -n "$EDITOR" ]; then
        print_message $CYAN "Opening $target_file in editor..."
        $EDITOR "$target_file"
    fi
}

# Start bot
start_bot() {
    local env_type=$1
    local env_file=".env"
    local compose_file="docker-compose.yml"
    
    if [ "$env_type" == "dev" ]; then
        env_file=".env.dev"
        compose_file="docker-compose.dev.yml"
    fi

    if [ ! -f "$env_file" ]; then
        print_message $RED "Error: $env_file not found. Run './setup.sh setup $env_type' first."
        exit 1
    fi

    # Validate environment file
    if ! validate_env_file "$env_file"; then
        print_message $RED "Please configure the environment file before starting the bot."
        exit 1
    fi

    print_message $CYAN "Starting bot in $env_type mode..."
    if [ "$env_type" == "dev" ]; then
        docker-compose -f $compose_file up --build
    else
        docker-compose -f $compose_file up --build -d
    fi
}

# Backup data
backup_data() {
    local backup_dir="backups"
    local timestamp=$(date +%Y%m%d_%H%M%S)
    
    mkdir -p "$backup_dir"
    
    print_message $CYAN "Creating backup..."
    
    # Stop containers if running
    if docker-compose ps | grep -q "Up"; then
        print_message $YELLOW "Stopping containers for backup..."
        docker-compose down
    fi
    
    if [ -f "data/signinbot.db" ]; then
        cp "data/signinbot.db" "$backup_dir/signinbot_$timestamp.db"
        print_message $GREEN "Database backed up to $backup_dir/signinbot_$timestamp.db"
    fi
    
    if [ -f ".env" ]; then
        cp ".env" "$backup_dir/env_$timestamp.backup"
        print_message $GREEN "Configuration backed up to $backup_dir/env_$timestamp.backup"
    fi

    # Create tar archive of the backup
    tar -czf "$backup_dir/backup_$timestamp.tar.gz" "$backup_dir/signinbot_$timestamp.db" "$backup_dir/env_$timestamp.backup" 2>/dev/null
    print_message $GREEN "Backup archive created: backup_$timestamp.tar.gz"

    # Restart containers if they were running
    if [ "$?" -eq 0 ]; then
        print_message $YELLOW "Restarting containers..."
        docker-compose up -d
    fi
}

# Show help
show_help() {
    print_message $CYAN "MorningSignInBot Setup Script"
    print_message $CYAN "\nUsage:"
    print_message $CYAN "  ./setup.sh setup [dev|prod]  - Create environment file"
    print_message $CYAN "  ./setup.sh start [dev|prod]  - Start the bot"
    print_message $CYAN "  ./setup.sh backup           - Backup data and configuration"
    print_message $CYAN "  ./setup.sh help             - Show this help message"
}

# Check prerequisites before executing commands
check_prerequisites

# Main script execution
case "$1" in
    "setup")
        setup_environment "${2:-prod}"
        ;;
    "start")
        start_bot "${2:-prod}"
        ;;
    "backup")
        backup_data
        ;;
    "help"|*)
        show_help
        ;;
esac

