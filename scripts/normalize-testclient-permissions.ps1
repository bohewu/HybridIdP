<##
Normalize TestClient (testclient-public) OpenIddict permissions across database providers.

This script updates the "Permissions" field on the OpenIddictApplications record for
ClientId 'testclient-public' so it is stored as a clean JSON array of strings —
avoiding nested arrays or stringified JSON which can break OpenIddict parsing.

The script supports Postgres and SQL Server when run against the containers launched by
docker-compose in this repo. It will attempt to find Postgres (postgres-service) and MSSQL
(mssql-service) containers automatically.

Usage examples:
  # From repository root, normalize both DBs
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\normalize-testclient-permissions.ps1

  # Normalize only Postgres
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\normalize-testclient-permissions.ps1 -Provider Postgres

  # Normalize only MSSQL
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\normalize-testclient-permissions.ps1 -Provider SqlServer
#>

[CmdletBinding()]
param(
    [ValidateSet('All','Postgres','SqlServer')]
    [string]$Provider = 'All',
    [string]$PgUser = 'user',
    [string]$PgDb = 'hybridauth_idp',
    [string]$MssqlContainer = 'hybrididp-mssql-service-1',
    [string]$SqlSaPassword = 'YourStrong!Passw0rd'
)

function Write-Info($m){ Write-Host $m -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host $m -ForegroundColor Green }
function Write-Warn($m){ Write-Host $m -ForegroundColor Yellow }
function Write-Err($m){ Write-Host $m -ForegroundColor Red }

$canonical = '["ept:authorization","ept:token","ept:logout","gt:authorization_code","gt:refresh_token","response_type:code","scp:openid","scp:profile","scp:email","scp:roles","scp:api:company:read","scp:api:company:write","scp:api:inventory:read"]'

if ($Provider -in @('All','Postgres')) {
    Write-Info 'Normalizing permissions in PostgreSQL...'
    # Prefer explicit service name 'postgres-service' for consistency with mssql-service
    $pgContainer = docker ps --filter "name=postgres-service" --format '{{.Names}}' 2>$null | Select-Object -First 1
    # Backwards compatibility: some environments still use 'db-service'
    if (-not $pgContainer) { $pgContainer = docker ps --filter "name=db-service" --format '{{.Names}}' 2>$null | Select-Object -First 1 }
    if (-not $pgContainer) { $pgContainer = docker ps --filter "ancestor=postgres" --format '{{.Names}}' 2>$null | Select-Object -First 1 }
    if ($pgContainer) {
        Write-Info "Using Postgres container: $pgContainer"
        Write-Info 'Current Permissions (Postgres):'
        # Build a properly-quoted psql command string and run it in the container.
        # Use a double-quoted -c argument and escape double-quotes inside so the PowerShell
        # parser doesn't misinterpret embedded single-quote sequences or the $canonical variable.
        $selectCmd = @"
    psql -U $PgUser -d $PgDb -q -t -c "SELECT \"Permissions\" FROM \"OpenIddictApplications\" WHERE \"ClientId\" = 'testclient-public';"
"@
        docker exec -i $pgContainer bash -c ($selectCmd.Trim()) | Out-Null

        # Construct an update command with the canonical JSON string inserted (wrapped in single quotes for psql)
        # Put the full -c argument inside double quotes and escape internal double-quotes; $canonical
        # is expanded by the outer PowerShell string and will be wrapped in single-quotes for SQL.
        $updateCmd = @"
    psql -U $PgUser -d $PgDb -c "UPDATE \"OpenIddictApplications\" SET \"Permissions\" = '$canonical'::jsonb WHERE \"ClientId\" = 'testclient-public';"
"@
        Write-Info 'Updating Permissions in Postgres to canonical list...'
        docker exec -i $pgContainer bash -c ($updateCmd.Trim())
        if ($LASTEXITCODE -eq 0) { Write-Ok 'Postgres permissions normalized.' } else { Write-Warn 'Postgres update exited with non-zero code (check container logs).' }
    } else {
        Write-Warn 'No Postgres container found. Skipping Postgres normalization.'
    }
}

if ($Provider -in @('All','SqlServer')) {
    Write-Info 'Normalizing permissions in SQL Server (MSSQL)...'
    # Use the configured container name by default; allow fallback to search
    $mssql = $MssqlContainer
    if (-not (docker ps --format '{{.Names}}' | Select-String -SimpleMatch $mssql)) {
        # Try to discover any container with 'mssql' in the name
        $mssql = (docker ps --filter "ancestor=mcr.microsoft.com/mssql/server" --format '{{.Names}}' | Select-Object -First 1)
    }
    if ($mssql) {
        Write-Info "Using MSSQL container: $mssql"
        Write-Info 'Current Permissions (MSSQL):'
        # Use -C to trust server certificate (OC driver 18 requires trusting self-signed certs in some containers)
        docker exec -i $mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "$SqlSaPassword" -d hybridauth_idp -C -Q "SET NOCOUNT ON; SELECT Permissions FROM OpenIddictApplications WHERE ClientId = 'testclient-public';"

        # Make sure the JSON string is wrapped in single-quotes for T-SQL
        # Ensure QUOTED_IDENTIFIER is ON to match DB requirements for updates when applicable
        $sql = "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE OpenIddictApplications SET Permissions = N'$canonical' WHERE ClientId = 'testclient-public';"
        Write-Info 'Updating Permissions in MSSQL to canonical list...'
        docker exec -i $mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "$SqlSaPassword" -d hybridauth_idp -C -Q "$sql"
        if ($LASTEXITCODE -eq 0) { Write-Ok 'MSSQL permissions normalized.' } else { Write-Warn 'MSSQL update exited with non-zero code (check container logs).' }
    } else {
        Write-Warn 'No MSSQL container found. Skipping MSSQL normalization.'
    }
}

Write-Ok 'Normalization completed.'
exit 0
