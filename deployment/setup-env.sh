#!/bin/bash
#
# Interactive .env file generator for HybridIdP deployment.
#
# This script interactively prompts for deployment configuration and generates
# a .env file with secure, randomly-generated passwords.
#
# Run from the deployment/ directory: ./setup-env.sh
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_PATH="$SCRIPT_DIR/.env"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_title() { echo -e "\n${CYAN}=== $1 ===${NC}"; }
print_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
print_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }

# Generate a secure random password
generate_password() {
    local length=${1:-24}
    local sql_safe=${2:-false}
    
    if [ "$sql_safe" = true ]; then
        # SQL-safe: avoid problematic characters (including $ to prevent shell interpolation)
        LC_ALL=C tr -dc 'A-Za-z0-9!@#_+-' < /dev/urandom | head -c "$length"
    else
        LC_ALL=C tr -dc 'A-Za-z0-9!@#%^&_*+=-' < /dev/urandom | head -c "$length"
    fi
    
    echo
}

# Prompt user for input with default value
prompt_with_default() {
    local prompt="$1"
    local default="$2"
    local result
    
    if [ -n "$default" ]; then
        read -rp "$prompt [$default]: " result
        echo "${result:-$default}"
    else
        read -rp "$prompt: " result
        echo "$result"
    fi
}

# Prompt for choice selection
prompt_choice() {
    local __resultvar=$1
    local prompt=$2
    shift 2
    local choices=("$@")
    local default_index=0
    
    # Print to stderr to ensure visibility even if stdout is redirected
    echo "" >&2
    echo "$prompt" >&2
    for i in "${!choices[@]}"; do
        local marker="[ ]"
        if [ "$i" -eq "$default_index" ]; then
            marker="[*]"
        fi
        echo "  $((i+1)). $marker ${choices[$i]}" >&2
    done
    
    read -rp "Enter choice (1-${#choices[@]}) [default: 1]: " selection
    
    local selected_value
    if [ -z "$selection" ]; then
        selected_value="${choices[$default_index]}"
    else
        local idx=$((selection - 1))
        if [ "$idx" -ge 0 ] && [ "$idx" -lt "${#choices[@]}" ]; then
            selected_value="${choices[$idx]}"
        else
            selected_value="${choices[$default_index]}"
        fi
    fi
    
    eval $__resultvar="'$selected_value'"
}

# Main script
cat << 'EOF'

  _    _       _          _     _ ___     _____  
 | |  | |     | |        (_)   | |_  |   |  __ \ 
 | |__| |_   _| |__  _ __ _  __| | | | __| |__) |
 |  __  | | | | '_ \| '__| |/ _` | | |/ /|  ___/ 
 | |  | | |_| | |_) | |  | | (_| | |   < | |     
 |_|  |_|\__, |_.__/|_|  |_|\__,_| |_|\_\|_|     
          __/ |                   |______|       
         |___/                                   
                                                 
    Environment Setup Wizard (Linux)

EOF

# Check for existing .env
if [ -f "$ENV_PATH" ]; then
    print_warn "Existing .env file found at: $ENV_PATH"
    read -rp "Overwrite? (y/N): " overwrite
    if [[ ! "$overwrite" =~ ^[Yy]$ ]]; then
        print_info "Creating backup and continuing..."
        backup_path="$ENV_PATH.backup.$(date +%Y%m%d_%H%M%S)"
        cp "$ENV_PATH" "$backup_path"
        print_info "Backup created: $backup_path"
    fi
fi

print_title "Deployment Mode"
deployment_mode=""
prompt_choice "deployment_mode" "Select deployment mode:" \
    "Nginx Reverse Proxy (Recommended - includes SSL termination)" \
    "Internal/Load Balancer (No SSL container - external LB handles SSL)" \
    "Split-Host + Nginx + Internal DB (Docker DB included)" \
    "Split-Host + Nginx + External DB (External DB server)"

use_nginx=false
use_split_host=false
use_external_db=false
[[ "$deployment_mode" == *"Nginx"* ]] && use_nginx=true
[[ "$deployment_mode" == *"Split-Host"* ]] && use_split_host=true
[[ "$deployment_mode" == *"External DB"* ]] && use_external_db=true

internal_ip="0.0.0.0"
proxy_host_ip=""

if [ "$use_split_host" = true ]; then
    print_title "Split-Host Network Configuration"
    print_info "For Split-Host mode, we can bind to a specific internal IP for security."
    internal_ip=$(prompt_with_default "Internal IP to bind to (Host B IP)" "0.0.0.0")
    
    print_info "We need to trust the external Reverse Proxy (Host A) to correctly parse headers."
    proxy_host_ip=$(prompt_with_default "External Reverse Proxy IP (Host A IP)" "")
fi

print_title "Database Configuration"
db_provider=""
prompt_choice "db_provider" "Select database provider:" \
    "SqlServer (Microsoft SQL Server 2022)" \
    "PostgreSQL (PostgreSQL 17)"

use_sqlserver=false
[[ "$db_provider" == *"SqlServer"* ]] && use_sqlserver=true

# If using external DB, prompt for connection details
if [ "$use_external_db" = true ]; then
    print_title "External Database Connection"
    print_warn "You selected external database. Please provide connection details."
    
    if [ "$use_sqlserver" = true ]; then
        external_db_host=$(prompt_with_default "SQL Server Host (e.g., db.example.com,1433)" "localhost,1433")
        external_db_name=$(prompt_with_default "Database Name" "hybridauth_idp")
        external_db_user=$(prompt_with_default "Database User" "idp_app")
        read -rsp "Database Password (press Enter to generate): " external_db_password
        echo
        if [ -z "$external_db_password" ]; then
            external_db_password=$(generate_password 24 true)
            print_info "Generated random password: $external_db_password"
        fi
    else
        external_db_host=$(prompt_with_default "PostgreSQL Host" "localhost")
        external_db_port=$(prompt_with_default "PostgreSQL Port" "5432")
        external_db_name=$(prompt_with_default "Database Name" "hybridauth_idp")
        external_db_user=$(prompt_with_default "Database User" "idp_app")
        read -rsp "Database Password (press Enter to generate): " external_db_password
        echo
        if [ -z "$external_db_password" ]; then
            external_db_password=$(generate_password 24)
            print_info "Generated random password: $external_db_password"
        fi
    fi
fi

print_title "Generating Secure Passwords"
mssql_password=$(generate_password 24 true)
postgres_password=$(generate_password 24)
encryption_cert_password=$(generate_password 20)
signing_cert_password=$(generate_password 20)

print_info "Random passwords generated successfully:"
echo "  - MSSQL_SA_PASSWORD: $mssql_password" >&2
echo "  - POSTGRES_PASSWORD: $postgres_password" >&2
echo "  - ENCRYPTION_CERT_PASSWORD: $encryption_cert_password" >&2
echo "  - SIGNING_CERT_PASSWORD: $signing_cert_password" >&2

print_title "Redis Configuration"
redis_choice=""
prompt_choice "redis_choice" "Enable Redis for distributed caching?" \
    "Yes (Recommended for production)" \
    "No (Use in-memory caching)"

redis_enabled=true
[[ "$redis_choice" == *"No"* ]] && redis_enabled=false

print_title "Proxy Configuration"
if [ "$use_nginx" = true ]; then
    proxy_enabled="true"
else
    proxy_choice=""
    prompt_choice "proxy_choice" "Is there a reverse proxy/load balancer in front?" \
        "No" \
        "Yes"
    [[ "$proxy_choice" == *"Yes"* ]] && proxy_enabled="true" || proxy_enabled="false"
fi

known_proxies="172.16.0.0/12;192.168.0.0/16;10.0.0.0/8"
if [ "$use_split_host" = true ] && [ -n "$proxy_host_ip" ]; then
    known_proxies="$proxy_host_ip;172.16.0.0/12;192.168.0.0/16;10.0.0.0/8"
fi

print_title "Optional: External Services"

# Email Settings
echo -e "\nEmail (SMTP) Configuration (press Enter to skip for later):"
smtp_host=$(prompt_with_default "SMTP Host" "")
if [ -n "$smtp_host" ]; then
    smtp_port=$(prompt_with_default "SMTP Port" "587")
    smtp_enable_ssl=$(prompt_with_default "Enable SSL (true/false)" "true")
    smtp_username=$(prompt_with_default "SMTP Username" "")
    smtp_password=$(prompt_with_default "SMTP Password" "")
    smtp_from_address=$(prompt_with_default "From Address" "noreply@example.com")
    smtp_from_name=$(prompt_with_default "From Name" "HybridIdP")
fi

# Turnstile
echo -e "\nCloudflare Turnstile (Bot Protection) - press Enter to skip:"
turnstile_site_key=$(prompt_with_default "Turnstile Site Key" "")
if [ -n "$turnstile_site_key" ]; then
    turnstile_secret_key=$(prompt_with_default "Turnstile Secret Key" "")
fi

print_title "Fido2 / WebAuthn Configuration"
print_info "Configuration for Passkey/WebAuthn support."
fido2_domain=$(prompt_with_default "Server Domain (e.g. localhost, idp.example.com)" "localhost")
fido2_origins=$(prompt_with_default "Allowed Origins (comma-separated)" "https://localhost:7035")

print_title "Branding Configuration"
app_name=$(prompt_with_default "Application Name" "HybridAuth")
product_name=$(prompt_with_default "Product Name" "HybridAuth IdP")
copyright=$(prompt_with_default "Copyright Text" "© $(date +%Y)")

print_title "Advanced: OpenIddict Issuer"
print_info "Optional: Set a fixed issuer URI. Recommended for production behind reverse proxy."
oidc_issuer=$(prompt_with_default "Issuer URI (leave empty for auto-detect)" "")

print_title "Generating .env file"

# Determine DATABASE_PROVIDER value
db_provider_value="SqlServer"
[ "$use_sqlserver" = false ] && db_provider_value="PostgreSQL"

# Build the .env content
if [ "$use_external_db" = true ]; then
    # External DB connection
    if [ "$use_sqlserver" = true ]; then
        db_connection_content="# External database connection (user-provided)
ConnectionStrings__SqlServerConnection='Server=$external_db_host;Database=$external_db_name;User Id=$external_db_user;Password=$external_db_password;Encrypt=True;TrustServerCertificate=True'"
    else
        db_connection_content="# External database connection (user-provided)
ConnectionStrings__PostgreSqlConnection='Host=$external_db_host;Port=$external_db_port;Database=$external_db_name;Username=$external_db_user;Password=$external_db_password'"
    fi
    db_credentials_content=""
else
    # Docker internal DB
    db_connection_content="# Docker internal database (mssql-service, postgres-service are Docker Compose service names)
ConnectionStrings__SqlServerConnection='Server=mssql-service;Database=hybridauth_idp;User Id=sa;Password=$mssql_password;Encrypt=True;TrustServerCertificate=True'
ConnectionStrings__PostgreSqlConnection='Host=postgres-service;Port=5432;Database=hybridauth_idp;Username=user;Password=$postgres_password'"
    db_credentials_content="
# Database Credentials (for Docker container initialization)
MSSQL_SA_PASSWORD='$mssql_password'
POSTGRES_USER='user'
POSTGRES_PASSWORD='$postgres_password'
POSTGRES_DB='hybridauth_idp'"
fi

cat > "$ENV_PATH" << EOF
# HybridIdP Deployment Environment Variables
# Generated by setup-env.sh at $(date '+%Y-%m-%d %H:%M:%S')
# =============================================================================

# ASP.NET Core Environment
ASPNETCORE_ENVIRONMENT=Production

# Database Provider: SqlServer or PostgreSQL
DATABASE_PROVIDER=$db_provider_value

# Database Connection Strings
$db_connection_content
ConnectionStrings__RedisConnection='redis-service:6379'
$db_credentials_content

# Redis Configuration
Redis__Enabled=$redis_enabled

# Proxy Configuration
Proxy__Enabled=$proxy_enabled
Proxy__KnownProxies=$known_proxies

# Network Binding (Split-Host)
INTERNAL_IP=$internal_ip

# OpenIddict Certificates
# These passwords protect the PFX files in deployment/certs/
ENCRYPTION_CERT_PASSWORD='$encryption_cert_password'
SIGNING_CERT_PASSWORD='$signing_cert_password'
EOF

# Add optional SMTP config
if [ -n "$smtp_host" ]; then
    cat >> "$ENV_PATH" << EOF

# Email Settings (SMTP)
EmailSettings__SmtpHost='$smtp_host'
EmailSettings__SmtpPort='$smtp_port'
EmailSettings__SmtpEnableSsl='$smtp_enable_ssl'
EmailSettings__SmtpUsername='$smtp_username'
EmailSettings__SmtpPassword='$smtp_password'
EmailSettings__FromAddress='$smtp_from_address'
EmailSettings__FromName='$smtp_from_name'
EOF
fi

# Add optional Turnstile config
if [ -n "$turnstile_site_key" ]; then
    cat >> "$ENV_PATH" << EOF

# Cloudflare Turnstile (Bot Protection)
Turnstile__SiteKey='$turnstile_site_key'
Turnstile__SecretKey='$turnstile_secret_key'
EOF
fi

# Add Fido2 Config
cat >> "$ENV_PATH" << EOF

# Fido2 / WebAuthn Configuration
Fido2__ServerDomain='$fido2_domain'
Fido2__Origins='$fido2_origins'
Fido2__TimestampDriftTolerance=300000
EOF

# Add Branding Config
cat >> "$ENV_PATH" << EOF

# Branding Configuration
Branding__AppName='$app_name'
Branding__ProductName='$product_name'
Branding__Copyright='$copyright'
EOF

# Add OpenIddict Issuer if set
if [ -n "$oidc_issuer" ]; then
    cat >> "$ENV_PATH" << EOF

# OpenIddict Issuer
OpenIddict__Issuer='$oidc_issuer'
EOF
fi

# Add token and rate limiting defaults
cat >> "$ENV_PATH" << EOF

# Token Security Options
TokenOptions__AccessTokenLifetimeMinutes=15
TokenOptions__RefreshTokenLifetimeMinutes=20160
TokenOptions__RefreshTokenReuseLeewaySeconds=60

# Rate Limiting (Production Defaults)
RateLimiting__Enabled=true
RateLimiting__LoginPermitLimit=5
RateLimiting__LoginWindowSeconds=60
EOF

print_info ".env file created at: $ENV_PATH"

print_title "Certificate Generation"
certs_dir="$SCRIPT_DIR/certs"
if [ ! -d "$certs_dir" ]; then
    mkdir -p "$certs_dir"
    print_info "Created certs directory: $certs_dir"
fi

# Check for existing certificates
encryption_pfx="$certs_dir/encryption.pfx"
signing_pfx="$certs_dir/signing.pfx"

if [ -f "$encryption_pfx" ] && [ -f "$signing_pfx" ]; then
    print_warn "Certificates already exist in $certs_dir"
    print_warn "If you want to regenerate, delete them and run this script again."
else
    # Default to generating with OpenSSL
    if command -v openssl &> /dev/null; then
        print_info "Generating certificates with OpenSSL..."
        pushd "$certs_dir" > /dev/null
        
        # Encryption certificate
        openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
            -keyout encryption.key -out encryption.crt \
            -subj "/CN=HybridIdP Encryption" 2>/dev/null
        
        openssl pkcs12 -export -out encryption.pfx \
            -inkey encryption.key -in encryption.crt \
            -password "pass:$encryption_cert_password" 2>/dev/null
        
        # Signing certificate
        openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
            -keyout signing.key -out signing.crt \
            -subj "/CN=HybridIdP Signing" 2>/dev/null
        
        openssl pkcs12 -export -out signing.pfx \
            -inkey signing.key -in signing.crt \
            -password "pass:$signing_cert_password" 2>/dev/null
        
        # Cleanup key/crt files
        rm -f *.key *.crt
        
        popd > /dev/null
        print_info "Certificates generated successfully!"
    else
        print_warn "OpenSSL not found. Please install OpenSSL or generate certificates manually."
        echo "Place 'encryption.pfx' and 'signing.pfx' in: $certs_dir"
    fi
fi

print_title "Setup Complete!"

# Determine compose file based on deployment mode
if [ "$use_split_host" = true ]; then
    if [ "$use_external_db" = true ]; then
        compose_file="docker-compose.splithost-nginx-nodb.yml"
    else
        compose_file="docker-compose.splithost-nginx.yml"
    fi
elif [ "$use_nginx" = true ]; then
    compose_file="docker-compose.nginx.yml"
else
    compose_file="docker-compose.internal.yml"
fi

# Determine access URL
if [ "$use_split_host" = true ]; then
    access_url="- HTTP: http://localhost:8080 (via Nginx gateway, behind external RP)"
elif [ "$use_nginx" = true ]; then
    access_url="- HTTPS: https://localhost (via Nginx)"
else
    access_url="- HTTP: http://localhost:8080 (behind your LB)"
fi

echo -e "\nNext steps:"
echo "1. Review the generated .env file: $ENV_PATH"
echo "2. Ensure certificates exist in: $certs_dir"

if [ "$use_split_host" = true ]; then
    echo "3. Edit nginx/splithost-gateway.conf to set allowed proxy IPs"
    echo "4. Start the application:"
    echo ""
    echo "   docker compose -f $compose_file --env-file .env up -d"
    echo ""
    echo "5. Access the application:"
else
    echo "3. Start the application:"
    echo ""
    echo "   docker compose -f $compose_file --env-file .env up -d"
    echo ""
    echo "4. Access the application:"
fi
echo "   $access_url"

echo -e "\nFor more details, see docs/DEPLOYMENT_GUIDE.md\n"
