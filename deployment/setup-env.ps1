<#
.SYNOPSIS
    Interactive .env file generator for HybridIdP deployment.
    
.DESCRIPTION
    This script interactively prompts for deployment configuration and generates
    a .env file with secure, randomly-generated passwords.
    
.NOTES
    Run from the deployment/ directory: .\setup-env.ps1
#>

param(
    [switch]$Force  # Overwrite existing .env without confirmation
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Colors for output
function Write-Title { param($msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Info { param($msg) Write-Host "[INFO] $msg" -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host "[WARN] $msg" -ForegroundColor Yellow }

# Generate a secure random password
function New-SecurePassword {
    param(
        [int]$Length = 24,
        [switch]$SqlSafe  # Avoid characters that cause SQL issues
    )
    
    $chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    if (-not $SqlSafe) {
        $chars += "!@#%^&*_-+=" # Removed $ to avoid variable interpolation issues
    } else {
        # SQL Server SA password requirements: uppercase, lowercase, number, special
        $chars += "!@#_-+" # Removed $
    }
    
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    
    $password = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
    
    # Ensure complexity: at least one uppercase, lowercase, digit, and special
    $hasUpper = $password -cmatch '[A-Z]'
    $hasLower = $password -cmatch '[a-z]'
    $hasDigit = $password -match '\d'
    $hasSpecial = $password -match '[!@#$%^&*_\-+=/]'
    
    if (-not ($hasUpper -and $hasLower -and $hasDigit -and $hasSpecial)) {
        return New-SecurePassword -Length $Length -SqlSafe:$SqlSafe
    }
    
    return $password
}

# Prompt user for input with default value
function Read-PromptWithDefault {
    param(
        [string]$Prompt,
        [string]$Default,
        [switch]$Secret
    )
    
    $displayDefault = if ($Default) { " [$Default]" } else { "" }
    $fullPrompt = "${Prompt}${displayDefault}: "
    
    if ($Secret) {
        Write-Host $fullPrompt -NoNewline
        $secureInput = Read-Host -AsSecureString
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureInput)
        $value = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    } else {
        $value = Read-Host -Prompt $fullPrompt.TrimEnd(": ")
    }
    
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }
    return $value
}

# Prompt for choice selection
function Read-Choice {
    param(
        [string]$Prompt,
        [string[]]$Choices,
        [int]$DefaultIndex = 0
    )
    
    Write-Host "`n$Prompt"
    for ($i = 0; $i -lt $Choices.Length; $i++) {
        $marker = if ($i -eq $DefaultIndex) { "[*]" } else { "[ ]" }
        Write-Host "  $($i + 1). $marker $($Choices[$i])"
    }
    
    $selection = Read-Host -Prompt "Enter choice (1-$($Choices.Length)) [default: $($DefaultIndex + 1)]"
    
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $Choices[$DefaultIndex]
    }
    
    $idx = [int]$selection - 1
    if ($idx -ge 0 -and $idx -lt $Choices.Length) {
        return $Choices[$idx]
    }
    
    return $Choices[$DefaultIndex]
}

# Main script
Write-Host @"

  _    _       _          _     _ ___     _____  
 | |  | |     | |        (_)   | |_  |   |  __ \ 
 | |__| |_   _| |__  _ __ _  __| | | | __| |__) |
 |  __  | | | | '_ \| '__| |/ _` | | |/ /|  ___/ 
 | |  | | |_| | |_) | |  | | (_| | |   < | |     
 |_|  |_|\__, |_.__/|_|  |_|\__,_| |_|\_\|_|     
          __/ |                   |______|       
         |___/                                   
                                                 
    Environment Setup Wizard
"@ -ForegroundColor Cyan

$envPath = Join-Path $ScriptDir ".env"

# Check for existing .env
if (Test-Path $envPath) {
    if (-not $Force) {
        Write-Warn "Existing .env file found at: $envPath"
        $overwrite = Read-Host "Overwrite? (y/N)"
        if ($overwrite -ne "y" -and $overwrite -ne "Y") {
            Write-Info "Creating backup and continuing..."
            $backupPath = "$envPath.backup.$(Get-Date -Format 'yyyyMMdd_HHmmss')"
            Copy-Item $envPath $backupPath
            Write-Info "Backup created: $backupPath"
        }
    }
}

Write-Title "Deployment Mode"
$deploymentMode = Read-Choice -Prompt "Select deployment mode:" -Choices @(
    "Nginx Reverse Proxy (Recommended - includes SSL termination)",
    "Internal/Load Balancer (No SSL container - external LB handles SSL)",
    "Split-Host + Nginx + Internal DB (Docker DB included)",
    "Split-Host + Nginx + External DB (External DB server)"
) -DefaultIndex 0

$useNginx = $deploymentMode -like "*Nginx*"
$useSplitHost = $deploymentMode -like "*Split-Host*"
$useExternalDb = $deploymentMode -like "*External DB*"

if ($useSplitHost) {
    Write-Title "Split-Host Network Configuration"
    Write-Info "For Split-Host mode, we can bind to a specific internal IP for security."
    $internalIp = Read-PromptWithDefault -Prompt "Internal IP to bind to (Host B IP)" -Default "0.0.0.0"
    
    Write-Info "We need to trust the external Reverse Proxy (Host A) to correctly parse headers."
    $proxyHostIp = Read-PromptWithDefault -Prompt "External Reverse Proxy IP (Host A IP)" -Default ""
}

Write-Title "Database Configuration"
$dbProvider = Read-Choice -Prompt "Select database provider:" -Choices @(
    "SqlServer (Microsoft SQL Server 2022)",
    "PostgreSQL (PostgreSQL 17)"
) -DefaultIndex 0

$useSqlServer = $dbProvider -like "*SqlServer*"

# If using external DB, prompt for connection details
if ($useExternalDb) {
    Write-Title "External Database Connection"
    Write-Warn "You selected external database. Please provide connection details."
    
    if ($useSqlServer) {
        $externalDbHost = Read-PromptWithDefault -Prompt "SQL Server Host (e.g., db.example.com,1433)" -Default "localhost,1433"
        $externalDbName = Read-PromptWithDefault -Prompt "Database Name" -Default "hybridauth_idp"
        $externalDbUser = Read-PromptWithDefault -Prompt "Database User" -Default "idp_app"
        $externalDbPassword = Read-PromptWithDefault -Prompt "Database Password" -Default "" -Secret
        if ([string]::IsNullOrWhiteSpace($externalDbPassword)) {
            $externalDbPassword = New-SecurePassword -Length 24 -SqlSafe
            Write-Info "Generated random password: $externalDbPassword"
        }
    } else {
        $externalDbHost = Read-PromptWithDefault -Prompt "PostgreSQL Host" -Default "localhost"
        $externalDbPort = Read-PromptWithDefault -Prompt "PostgreSQL Port" -Default "5432"
        $externalDbName = Read-PromptWithDefault -Prompt "Database Name" -Default "hybridauth_idp"
        $externalDbUser = Read-PromptWithDefault -Prompt "Database User" -Default "idp_app"
        $externalDbPassword = Read-PromptWithDefault -Prompt "Database Password" -Default "" -Secret
        if ([string]::IsNullOrWhiteSpace($externalDbPassword)) {
            $externalDbPassword = New-SecurePassword -Length 24
            Write-Info "Generated random password: $externalDbPassword"
        }
    }
}

Write-Title "Generating Secure Passwords"
$mssqlPassword = New-SecurePassword -Length 24 -SqlSafe
$postgresPassword = New-SecurePassword -Length 24
$encryptionCertPassword = New-SecurePassword -Length 20
$signingCertPassword = New-SecurePassword -Length 20

Write-Info "Random passwords generated successfully."

Write-Title "Redis Configuration"
$useRedis = Read-Choice -Prompt "Enable Redis for distributed caching?" -Choices @(
    "Yes (Recommended for production)",
    "No (Use in-memory caching)"
) -DefaultIndex 0
$redisEnabled = $useRedis -like "*Yes*"

Write-Title "Proxy Configuration"
$proxyEnabled = if ($useNginx) { "true" } else {
    $proxyChoice = Read-Choice -Prompt "Is there a reverse proxy/load balancer in front?" -Choices @("Yes", "No") -DefaultIndex 1
    if ($proxyChoice -eq "Yes") { "true" } else { "false" }
}

$knownProxies = "172.16.0.0/12;192.168.0.0/16;10.0.0.0/8"
if ($useSplitHost -and $proxyHostIp) {
    # If specific proxy IP is known, we can be more specific, or just append it to ensure it's trusted
    # For strict security, we might want ONLY this IP, but for ease of use with Docker networks, we keep the CIDRs too or just add it.
    # Let's use the specific IP if provided to be explicit in the config.
    $knownProxies = "$proxyHostIp;172.16.0.0/12;192.168.0.0/16;10.0.0.0/8"
}

Write-Title "Optional: External Services"

# Email Settings
Write-Host "`nEmail (SMTP) Configuration (press Enter to skip for later):"
$smtpHost = Read-PromptWithDefault -Prompt "SMTP Host" -Default ""
if ($smtpHost) {
    $smtpPort = Read-PromptWithDefault -Prompt "SMTP Port" -Default "587"
    $smtpEnableSsl = Read-PromptWithDefault -Prompt "Enable SSL (true/false)" -Default "true"
    $smtpUsername = Read-PromptWithDefault -Prompt "SMTP Username" -Default ""
    $smtpPassword = Read-PromptWithDefault -Prompt "SMTP Password" -Default ""
    $smtpFromAddress = Read-PromptWithDefault -Prompt "From Address" -Default "noreply@example.com"
    $smtpFromName = Read-PromptWithDefault -Prompt "From Name" -Default "HybridIdP"
}

# Turnstile
Write-Host "`nCloudflare Turnstile (Bot Protection) - press Enter to skip:"
$turnstileSiteKey = Read-PromptWithDefault -Prompt "Turnstile Site Key" -Default ""
if ($turnstileSiteKey) {
    $turnstileSecretKey = Read-PromptWithDefault -Prompt "Turnstile Secret Key" -Default ""
}

Write-Title "Generating .env file"

# Build the .env content
$envContent = @"
# HybridIdP Deployment Environment Variables
# Generated by setup-env.ps1 at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
# =============================================================================

# ASP.NET Core Environment
ASPNETCORE_ENVIRONMENT=Production

# Database Provider: SqlServer or PostgreSQL
DATABASE_PROVIDER=$(if ($useSqlServer) { "SqlServer" } else { "PostgreSQL" })

# Database Connection Strings
$(if ($useExternalDb) {
"# External database connection (user-provided)"
if ($useSqlServer) {
"ConnectionStrings__SqlServerConnection=Server=$externalDbHost;Database=$externalDbName;User Id=$externalDbUser;Password=$externalDbPassword;Encrypt=True;TrustServerCertificate=True"
} else {
"ConnectionStrings__PostgreSqlConnection=Host=$externalDbHost;Port=$externalDbPort;Database=$externalDbName;Username=$externalDbUser;Password=$externalDbPassword"
}
} else {
"# Docker internal database (mssql-service, postgres-service are Docker Compose service names)"
"ConnectionStrings__SqlServerConnection=Server=mssql-service;Database=hybridauth_idp;User Id=sa;Password=$mssqlPassword;Encrypt=True;TrustServerCertificate=True"
"ConnectionStrings__PostgreSqlConnection=Host=postgres-service;Port=5432;Database=hybridauth_idp;Username=user;Password=$postgresPassword"
})
ConnectionStrings__RedisConnection=redis-service:6379

$(if (-not $useExternalDb) {
"# Database Credentials (for Docker container initialization)"
"MSSQL_SA_PASSWORD=$mssqlPassword"
"POSTGRES_USER=user"
"POSTGRES_PASSWORD=$postgresPassword"
"POSTGRES_DB=hybridauth_idp"
})

# Redis Configuration
Redis__Enabled=$($redisEnabled.ToString().ToLower())

# Proxy Configuration
Proxy__Enabled=$proxyEnabled
Proxy__KnownProxies=$knownProxies

# Network Binding (Split-Host)
INTERNAL_IP=$(if ($internalIp) { $internalIp } else { "0.0.0.0" })

# OpenIddict Certificates
# These passwords protect the PFX files in deployment/certs/
ENCRYPTION_CERT_PASSWORD=$encryptionCertPassword
SIGNING_CERT_PASSWORD=$signingCertPassword
"@

# Add optional SMTP config
if ($smtpHost) {
    $envContent += @"

# Email Settings (SMTP)
EmailSettings__SmtpHost=$smtpHost
EmailSettings__SmtpPort=$smtpPort
EmailSettings__SmtpEnableSsl=$smtpEnableSsl
EmailSettings__SmtpUsername=$smtpUsername
EmailSettings__SmtpPassword=$smtpPassword
EmailSettings__FromAddress=$smtpFromAddress
EmailSettings__FromName=$smtpFromName
"@
}

# Add optional Turnstile config
if ($turnstileSiteKey) {
    $envContent += @"

# Cloudflare Turnstile (Bot Protection)
Turnstile__SiteKey=$turnstileSiteKey
Turnstile__SecretKey=$turnstileSecretKey
"@
}

# Add token and rate limiting defaults
$envContent += @"

# Token Security Options
TokenOptions__AccessTokenLifetimeMinutes=15
TokenOptions__RefreshTokenLifetimeMinutes=20160
TokenOptions__RefreshTokenReuseLeewaySeconds=60

# Rate Limiting (Production Defaults)
RateLimiting__Enabled=true
RateLimiting__LoginPermitLimit=5
RateLimiting__LoginWindowSeconds=60
"@

# Write the file
$envContent | Set-Content -Path $envPath -Encoding UTF8 -NoNewline
Write-Info ".env file created at: $envPath"

Write-Title "Certificate Generation"
$certsDir = Join-Path $ScriptDir "certs"
if (-not (Test-Path $certsDir)) {
    New-Item -ItemType Directory -Path $certsDir -Force | Out-Null
    Write-Info "Created certs directory: $certsDir"
}

# Check for existing certificates
$encryptionPfx = Join-Path $certsDir "encryption.pfx"
$signingPfx = Join-Path $certsDir "signing.pfx"

if ((Test-Path $encryptionPfx) -and (Test-Path $signingPfx)) {
    Write-Warn "Certificates already exist in $certsDir"
    Write-Warn "If you want to regenerate, delete them and run this script again."
} else {
    # Default to generating with OpenSSL
    $opensslPath = (Get-Command openssl -ErrorAction SilentlyContinue).Source
    if (-not $opensslPath) {
        Write-Warn "OpenSSL not found in PATH. Please install OpenSSL or generate certificates manually."
        Write-Host "Place 'encryption.pfx' and 'signing.pfx' in: $certsDir"
    } else {
        Write-Info "Generating certificates with OpenSSL..."
        Push-Location $certsDir
        try {
            # Encryption certificate
            & openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes `
                -keyout encryption.key -out encryption.crt `
                -subj "/CN=HybridIdP Encryption" 2>$null
            
            & openssl pkcs12 -export -out encryption.pfx `
                -inkey encryption.key -in encryption.crt `
                -password pass:$encryptionCertPassword 2>$null
            
            # Signing certificate
            & openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes `
                -keyout signing.key -out signing.crt `
                -subj "/CN=HybridIdP Signing" 2>$null
            
            & openssl pkcs12 -export -out signing.pfx `
                -inkey signing.key -in signing.crt `
                -password pass:$signingCertPassword 2>$null
            
            # Cleanup key/crt files (optional, keep only pfx)
            Remove-Item -Path "*.key", "*.crt" -Force -ErrorAction SilentlyContinue
            
            Write-Info "Certificates generated successfully!"
        } finally {
            Pop-Location
        }
    }
}

Write-Title "Setup Complete!"

$composeFile = if ($useSplitHost) {
    if ($useExternalDb) { "docker-compose.splithost-nginx-nodb.yml" } else { "docker-compose.splithost-nginx.yml" }
} elseif ($useNginx) {
    "docker-compose.nginx.yml"
} else {
    "docker-compose.internal.yml"
}

$accessUrl = if ($useSplitHost) {
    "- HTTP: http://localhost:8080 (via Nginx gateway, behind external RP)"
} elseif ($useNginx) {
    "- HTTPS: https://localhost (via Nginx)"
} else {
    "- HTTP: http://localhost:8080 (behind your LB)"
}

Write-Host @"

Next steps:
1. Review the generated .env file: $envPath
2. Ensure certificates exist in: $certsDir
$(if ($useSplitHost) {
"3. Edit nginx/splithost-gateway.conf to set allowed proxy IPs"
"4. Start the application:" 
} else {
"3. Start the application:"
})

   docker compose -f $composeFile --env-file .env up -d

$(if ($useSplitHost) { "5." } else { "4." }) Access the application:
   $accessUrl

For more details, see docs/DEPLOYMENT_GUIDE.md

"@ -ForegroundColor Green
