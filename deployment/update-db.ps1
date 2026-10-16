<#
.SYNOPSIS
    Updates the database schema for a remote or local database using Entity Framework Core migrations.
    
.DESCRIPTION
    This script sets the necessary environment variables and invokes the application's
    --migrate-only entrypoint. It does not start the web server or seed data.
    It supports both SQL Server and PostgreSQL and requires the .NET SDK.
    
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

$previousEnvironment = @{}
foreach ($name in @('DATABASE_PROVIDER', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT',
        'ConnectionStrings__SqlServerConnection', 'ConnectionStrings__PostgreSqlConnection')) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {

Write-Host "🚀 Starting Database Update Process..." -ForegroundColor Cyan
Write-Host "Provider: $Provider" -ForegroundColor Gray

# 1. Set Environment Variables
Write-Host "`nStep 1: Setting Environment Variables..." -ForegroundColor Yellow
$env:DATABASE_PROVIDER = $Provider
$env:ASPNETCORE_ENVIRONMENT = "Production" # Ensure we don't accidentally pick up Development settings if they conflict
$env:DOTNET_ENVIRONMENT = "Production"

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

# 3. Check for the .NET SDK
Write-Host "`nStep 3: Checking prerequisites..." -ForegroundColor Yellow
    dotnet --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The .NET SDK is required to run the schema migration."
    }

Push-Location $MigrationPath
try {

# 4. Execute Update
Write-Host "`nStep 4: Executing Database Update..." -ForegroundColor Yellow
Write-Host "Command: dotnet run --project ..\Web.IdP --no-launch-profile -- --migrate-only" -ForegroundColor DarkGray

    dotnet run --project ..\Web.IdP --no-launch-profile -- --migrate-only
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ Database update completed successfully!" -ForegroundColor Green
    }
    else {
        Write-Error "`n❌ Database update failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
