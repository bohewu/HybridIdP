<#
.SYNOPSIS
    Updates the database schema for a remote or local database using Entity Framework Core migrations.
    
.DESCRIPTION
    This script sets the necessary environment variables and executes 'dotnet ef database update'
    from the correct migration directory. It supports both SQL Server and PostgreSQL.
    
.PARAMETER Provider
    The database provider to use. specific 'SqlServer' or 'PostgreSQL'.
    
.PARAMETER ConnectionString
    The full connection string to the database.
    If provided, it overrides Host/Port/User/Password/Database parameters.

.EXAMPLE
    .\update-db.ps1 -Provider SqlServer -ConnectionString "Server=192.168.1.10,1433;Database=hybridauth_idp;User Id=sa;Password=StrongPassword!;TrustServerCertificate=True;Encrypt=True"
    
.EXAMPLE
    .\update-db.ps1 -Provider PostgreSQL -ConnectionString "Host=192.168.1.10;Port=5432;Database=hybridauth_idp;Username=postgres;Password=password"
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("SqlServer", "PostgreSQL")]
    [string]$Provider,

    [Parameter(Mandatory=$true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Starting Database Update Process..." -ForegroundColor Cyan
Write-Host "Provider: $Provider" -ForegroundColor Gray
Write-Host "Connection String: $ConnectionString" -ForegroundColor DarkGray

# 1. Set Environment Variables
Write-Host "`nStep 1: Setting Environment Variables..." -ForegroundColor Yellow
$env:DATABASE_PROVIDER = $Provider
$env:ASPNETCORE_ENVIRONMENT = "Production" # Ensure we don't accidentally pick up Development settings if they conflict

if ($Provider -eq "SqlServer") {
    $env:ConnectionStrings__SqlServerConnection = $ConnectionString
    $MigrationProject = "Infrastructure.Migrations.SqlServer"
}
elseif ($Provider -eq "PostgreSQL") {
    $env:ConnectionStrings__PostgreSqlConnection = $ConnectionString
    $MigrationProject = "Infrastructure.Migrations.Postgres"
}

# 2. Navigate to Migration Project
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$MigrationPath = Join-Path $RepoRoot $MigrationProject

if (-not (Test-Path $MigrationPath)) {
    Write-Error "Migration project directory not found: $MigrationPath"
    exit 1
}

Write-Host "`nStep 2: Navigating to $MigrationProject..." -ForegroundColor Yellow
Push-Location $MigrationPath

# 3. Check for EF Tool
Write-Host "`nStep 3: Checking prerequisites..." -ForegroundColor Yellow
try {
    dotnet ef --version | Out-Null
}
catch {
    Write-Warning "dotnet ef tool not found globally. Attempting to restore local tools..."
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to invoke dotnet ef. Please install it: dotnet tool install --global dotnet-ef"
        Pop-Location
        exit 1
    }
}

# 4. Execute Update
Write-Host "`nStep 4: Executing Database Update..." -ForegroundColor Yellow
Write-Host "Command: dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext" -ForegroundColor DarkGray

try {
    dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ Database update completed successfully!" -ForegroundColor Green
    }
    else {
        Write-Error "`n❌ Database update failed with exit code $LASTEXITCODE"
    }
}
catch {
    Write-Error "`n❌ An unexpected error occurred: $_"
}
finally {
    Pop-Location
    # Clean up env vars (optional, but good for local session hygiene if user dot-sources)
    $env:DATABASE_PROVIDER = $null
    $env:ASPNETCORE_ENVIRONMENT = $null
    $env:ConnectionStrings__SqlServerConnection = $null
    $env:ConnectionStrings__PostgreSqlConnection = $null
}
