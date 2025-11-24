<##
A helper script to run the E2E flow using PostgreSQL as the database provider.

This script automates the common steps used in a local Postgres-based E2E run:
- (optionally) bring up docker-compose services
- locate the running Postgres container
- create the target DB and enable pgcrypto (required by repo seed SQL)
- set environment variables for the current PowerShell session so child processes inherit them
- run the Postgres EF migrations (Infrastructure.Migrations.Postgres)
- (optionally) start Web.IdP and TestClient in separate windows (uses start-e2e-dev.ps1)
- wait for readiness using e2e/wait-for-idp-ready.ps1
- install Playwright deps + browsers and run tests

This is intentionally conservative (does not obliterate data) and requires `docker` and `dotnet ef` available in PATH.

Usage (examples):
  # From repo root - ensure Docker Desktop running
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -TimeoutSeconds 300

  # Run migrations + run tests only (assumes DB/container already present)
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -RunMigrations -RunTests

#>

[CmdletBinding()]
param(
    [switch]$UpCompose,
    [switch]$StartServices,
    [switch]$SkipMigrations,
    [switch]$SkipTests,
    [switch]$Headed,
    [switch]$WebOnly,
    [int]$TimeoutSeconds = 180,
    [switch]$SeedApiResources,
    [string]$ComposeFile = 'docker-compose.yml',
    [string]$PgUser = 'user',
    [string]$PgPassword = 'password',
    [string]$PgDb = 'hybridauth_idp',
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$IdpUrl = 'https://localhost:7035',
    [string]$TestClientUrl = 'https://localhost:7001'
)

function Write-Info($msg){ Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg){ Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg){ Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host $msg -ForegroundColor Red }

$root = (Get-Location).Path

if ($UpCompose) {
    Write-Info "Bringing up services using $ComposeFile..."
    docker-compose -f $ComposeFile up -d
    if ($LASTEXITCODE -ne 0) { Write-Err "docker-compose up failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    Start-Sleep -Seconds 3
}

# Locate Postgres container (search for service name containing 'db-service' first, else use ancestor image postgres)
Write-Info 'Locating PostgreSQL container...'
$pgContainer = docker ps --filter "name=db-service" --format '{{.Names}}' 2>$null | Select-Object -First 1
if (-not $pgContainer) {
    $pgContainer = docker ps --filter "ancestor=postgres" --format '{{.Names}}' 2>$null | Select-Object -First 1
}

if (-not $pgContainer) {
    Write-Err 'Could not find a running Postgres container. Ensure docker-compose is up and the Postgres container is running.'
    exit 4
}

Write-Ok "Found Postgres container: $pgContainer"

# Create DB if it doesn't exist, and enable pgcrypto
Write-Info "Ensuring database '$PgDb' exists and pgcrypto extension is enabled..."

# Create DB (silently ignore errors if it already exists)
docker exec -i $pgContainer psql -U $PgUser -d postgres -c "CREATE DATABASE \"$PgDb\";" 2>$null | Out-Null

# Enable / validate pgcrypto inside the target DB
Write-Info 'Checking for pgcrypto extension in the target DB...'
$checkCmd = 'psql -U ' + $PgUser + ' -d ' + $PgDb + ' -tAc "SELECT exists(SELECT 1 FROM pg_extension WHERE extname=''pgcrypto'');"'
$checkOut = docker exec -i $pgContainer bash -c $checkCmd 2>$null | Out-String
$checkOut = $checkOut.Trim()
if ($checkOut -eq 't' -or $checkOut -eq 'true') {
    Write-Ok "pgcrypto already present in database '$PgDb'."
} else {
    Write-Warn "pgcrypto extension not present — attempting to create it (this requires CREATE EXTENSION privileges)."
    $createCmd = 'psql -U ' + $PgUser + ' -d ' + $PgDb + ' -c "CREATE EXTENSION IF NOT EXISTS pgcrypto;"'
    $createOut = docker exec -i $pgContainer bash -c $createCmd 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "pgcrypto extension created successfully."
    } else {
        Write-Warn "Failed to create pgcrypto automatically. psql output: $createOut"
        Write-Warn "You may need to create pgcrypto with a superuser account inside the Postgres container. Example commands (adjust container name if necessary):"
        Write-Host "  docker exec -it $pgContainer psql -U $PgUser -d $PgDb -c \"CREATE EXTENSION IF NOT EXISTS pgcrypto;\"" -ForegroundColor Yellow
        Write-Host "  # or, if 'user' is not a superuser, run as the postgres superuser:" -ForegroundColor Yellow
        Write-Host "  docker exec -it $pgContainer psql -U postgres -d $PgDb -c \"CREATE EXTENSION IF NOT EXISTS pgcrypto;\"" -ForegroundColor Yellow
        Write-Err 'pgcrypto is required for some DB seed operations; fix and re-run this helper (you can re-run with -SkipMigrations to skip migrating if you only want to prepare the DB).' 
        exit 5
    }
}

Write-Ok "Postgres DB '$PgDb' prepared (pgcrypto validated)."

# Set env vars for this session so child processes inherit
$conn = "Host=$PgHost;Port=$PgPort;Database=$PgDb;Username=$PgUser;Password=$PgPassword"
$env:DATABASE_PROVIDER = 'PostgreSQL'
$env:ConnectionStrings__PostgreSqlConnection = $conn
Write-Info "Set DATABASE_PROVIDER=PostgreSQL and connection string for this session."

$RunMigrations = -not $SkipMigrations
$RunTests = -not $SkipTests

if ($RunMigrations) {
    Write-Info "Applying EF Core migrations for Postgres (Infrastructure.Migrations.Postgres)..."
    Push-Location -Path (Join-Path $root 'Infrastructure.Migrations.Postgres')
    dotnet ef database update --startup-project ..\Web.IdP
    $efExit = $LASTEXITCODE
    Pop-Location
    if ($efExit -ne 0) { Write-Err "EF migrations failed with exit code $efExit"; exit $efExit }
    Write-Ok 'EF migrations applied successfully.'
}

if ($SeedApiResources) {
    Write-Info 'Seeding API resources (setup-test-api-resources.sql) into the database...'
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\seed-api-resources.ps1" -PgUser $PgUser -PgDb $PgDb
    if ($LASTEXITCODE -ne 0) { Write-Err "seed-api-resources failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    Write-Ok 'API resources seeded.'
}

if ($StartServices) {
    Write-Info 'Starting Web.IdP (and TestClient optionally) in separate windows (scripts/start-e2e-dev.ps1) and inheriting current env vars...'
    $startArgs = @()
    $startArgs += '-InheritEnv'
    if ($WebOnly) { $startArgs += '-WebOnly' }
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\start-e2e-dev.ps1" @startArgs
    Start-Sleep -Seconds 3

    if (-not $WebOnly) {
        Write-Info 'Performing TestClient UI readiness check (home page should include Login/signin-oidc)...'
        & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\check-testclient-ready.ps1" -Url $TestClientUrl -TimeoutSeconds 60
        if ($LASTEXITCODE -ne 0) { Write-Warn 'check-testclient-ready failed — continuing to wait for readiness helper to retry.' }
    }
}

# Wait for IdP/TestClient readiness using existing helper
Write-Info 'Waiting for IdP and TestClient to be reachable + admin API healthy...'
if ($WebOnly) {
    # When running WebOnly, use the IdP URL for the TestClientUrl parameter so the readiness check
    # can still verify the admin API. (No TestClient instance will be started locally.)
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\e2e\wait-for-idp-ready.ps1" -IdpUrl $IdpUrl -TestClientUrl $IdpUrl -TimeoutSeconds $TimeoutSeconds
} else {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\e2e\wait-for-idp-ready.ps1" -IdpUrl $IdpUrl -TestClientUrl $TestClientUrl -TimeoutSeconds $TimeoutSeconds
}
if ($LASTEXITCODE -ne 0) { Write-Err "wait-for-idp-ready failed (exit code $LASTEXITCODE)"; exit $LASTEXITCODE }

if (-not $RunTests) { Write-Ok 'Preparation finished (RunTests is false) — leaving services running.'; exit 0 }

Write-Info 'Installing e2e npm dependencies and Playwright browsers...'
npm --prefix "$root\e2e" install
if ($LASTEXITCODE -ne 0) { Write-Err "npm install failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

npm --prefix "$root\e2e" run install:browsers
if ($LASTEXITCODE -ne 0) { Write-Err "playwright install failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Info 'Running Playwright tests...'
if ($Headed) {
    npm --prefix "$root\e2e" run test:headed
} else {
    npm --prefix "$root\e2e" test
}

exit $LASTEXITCODE
