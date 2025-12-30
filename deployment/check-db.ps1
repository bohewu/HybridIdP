<#
.SYNOPSIS
    Diagnose database connectivity for HybridIdP.
    
.DESCRIPTION
    This script reads the deployment/.env file (if available) or prompts for
    database details, then tests TCP connectivity to the database server.
    
    It supports identifying SQL Server and PostgreSQL connection strings.
    
.NOTES
    Run from deployment/ directory.
#>

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvPath = Join-Path $ScriptDir ".env"

function Write-Info { param($msg) Write-Host "[INFO] $msg" -ForegroundColor Green }
function Write-ErrorMsg { param($msg) Write-Host "[ERROR] $msg" -ForegroundColor Red }
function Write-Warn { param($msg) Write-Host "[WARN] $msg" -ForegroundColor Yellow }

Write-Host "HybridIdP Database Connection Tester" -ForegroundColor Cyan
Write-Host "===================================="

$hostName = ""
$port = 0
$dbType = ""

# 1. Try to read from .env
if (Test-Path $EnvPath) {
    Write-Info "Found .env file at: $EnvPath"
    $envLines = Get-Content $EnvPath
    
    # Simple regex parsing for connection strings in .env
    # Format: ConnectionStrings__SqlServerConnection='Server=...;...'
    
    foreach ($line in $envLines) {
        if ($line -match "ConnectionStrings__SqlServerConnection=['`"]?Server=([^;]+).*") {
            $fullServer = $matches[1]
            if ($fullServer -match ",") {
                $parts = $fullServer -split ","
                $hostName = $parts[0]
                $port = [int]$parts[1]
            } else {
                $hostName = $fullServer
                $port = 1433 # Default MSSQL
            }
            $dbType = "SQL Server"
            break
        }
        elseif ($line -match "ConnectionStrings__PostgreSqlConnection=['`"]?Host=([^;]+);Port=([^;]+).*") {
            $hostName = $matches[1]
            $port = [int]$matches[2]
            $dbType = "PostgreSQL"
            break
        }
    }
} else {
    Write-Warn ".env file not found."
}

# 2. If not found, prompt user
if (-not $hostName) {
    Write-Warn "Could not parse database connection details from .env."
    $hostName = Read-Host "Enter Database Host (e.g. localhost, db.internal)"
    $portInput = Read-Host "Enter Database Port (Default: 1433 for SQL, 5432 for Pg)"
    if ([string]::IsNullOrWhiteSpace($portInput)) {
        $port = 1433
    } else {
        $port = [int]$portInput
    }
}

if (-not $hostName) {
    Write-ErrorMsg "No host provided. Exiting."
    exit 1
}

Write-Info "Testing connection to [$hostName] on port [$port]..."

try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $connectTask = $tcp.ConnectAsync($hostName, $port)
    $completed = $connectTask.Wait(3000) # 3 second timeout
    
    if ($completed -and $tcp.Connected) {
        Write-Info "SUCCESS: Successfully connected to $hostName:$port"
        $tcp.Close()
        exit 0
    } else {
        Write-ErrorMsg "FAILURE: Could not connect to $hostName:$port (Timeout)"
        Write-Host "Suggestions:"
        Write-Host "  1. Check if the address is correct."
        Write-Host "  2. Try disabling firewall on the DB server momentarily."
        Write-Host "  3. If using Docker, ensure you are on the same network or using the host's IP."
        exit 1
    }
} catch {
    Write-ErrorMsg "FAILURE: Could not connect to $hostName:$port"
    Write-ErrorMsg $_.Exception.Message
    exit 1
}
