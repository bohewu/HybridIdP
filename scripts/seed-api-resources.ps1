<##
Ensure API resources and associations exist in the Postgres DB used for E2E.

This helper executes `setup-test-api-resources.sql` from the repository
against the running Postgres container discovered in docker-compose.

Usage (from repo root):
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\seed-api-resources.ps1

If a custom DB container name or credentials are required use the same
environment variables used in the Postgres runner (DATABASE_PROVIDER,
ConnectionStrings__PostgreSqlConnection) or pass the PgUser/PgDb vars.
#>

[CmdletBinding()]
param(
    [string]$ComposeFile = 'docker-compose.yml',
    [string]$PgUser = 'user',
    [string]$PgDb = 'hybridauth_idp',
    [string]$SqlFile = 'setup-test-api-resources.sql'
)

function Write-Info($m){ Write-Host $m -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host $m -ForegroundColor Green }
function Write-Warn($m){ Write-Host $m -ForegroundColor Yellow }

$root = (Get-Location).Path

Write-Info 'Locating Postgres container (docker-compose -> postgres-service or postgres image)...'
# Prefer explicit service name 'postgres-service'
$pgContainer = docker ps --filter "name=postgres-service" --format '{{.Names}}' 2>$null | Select-Object -First 1
# Backwards compatibility: old name 'db-service'
if (-not $pgContainer) { $pgContainer = docker ps --filter "name=db-service" --format '{{.Names}}' 2>$null | Select-Object -First 1 }
if (-not $pgContainer) { $pgContainer = docker ps --filter "ancestor=postgres" --format '{{.Names}}' 2>$null | Select-Object -First 1 }

if (-not $pgContainer) { Write-Warn 'Could not find Postgres container. Ensure docker-compose is running'; exit 3 }

Write-Info "Using container: $pgContainer"

$sqlPath = Join-Path $root $SqlFile
if (-not (Test-Path $sqlPath)) { Write-Warn "SQL file not found: $sqlPath"; exit 2 }

Write-Info "Applying SQL: $SqlFile to database $PgDb as user $PgUser"

# Use docker exec and pipe SQL file contents into psql
Get-Content $sqlPath -Raw | docker exec -i $pgContainer psql -U $PgUser -d $PgDb -q -v ON_ERROR_STOP=1
$exit = $LASTEXITCODE
if ($exit -ne 0) { Write-Warn "Seeding failed (exit $exit)"; exit $exit }

Write-Ok 'API resources seeded/updated successfully.'
exit 0
