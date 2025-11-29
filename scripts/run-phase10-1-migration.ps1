<#
.SYNOPSIS
    Phase 10.1 Migration Script - Add Person Entity and Backfill Data
.DESCRIPTION
    This script:
    1. Applies the Phase10_1_AddPersonEntity migration to the database
    2. Runs the backfill script to create Person records for existing users
    3. Verifies the migration was successful
.PARAMETER DatabaseProvider
    Database provider to use: "SqlServer" or "PostgreSQL" (default: "SqlServer")
.PARAMETER SkipBackfill
    Skip the data backfill step (only apply schema migration)
.EXAMPLE
    .\run-phase10-1-migration.ps1
.EXAMPLE
    .\run-phase10-1-migration.ps1 -DatabaseProvider PostgreSQL
.EXAMPLE
    .\run-phase10-1-migration.ps1 -SkipBackfill
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("SqlServer", "PostgreSQL")]
    [string]$DatabaseProvider = "SqlServer",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBackfill
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host " Phase 10.1: Person Entity Migration" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Determine paths
$rootPath = Split-Path -Parent $PSScriptRoot
$webIdpPath = Join-Path $rootPath "Web.IdP"
$scriptsPath = Join-Path $rootPath "scripts"

# Set migration project based on provider
if ($DatabaseProvider -eq "PostgreSQL") {
    $migrationProject = "Infrastructure.Migrations.Postgres"
    $backfillScript = Join-Path $scriptsPath "phase10-1-backfill-persons-postgres.sql"
    $connectionStringKey = "PostgreSqlConnection"
} else {
    $migrationProject = "Infrastructure.Migrations.SqlServer"
    $backfillScript = Join-Path $scriptsPath "phase10-1-backfill-persons-sqlserver.sql"
    $connectionStringKey = "SqlServerConnection"
}

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Database Provider: $DatabaseProvider" -ForegroundColor Gray
Write-Host "  Migration Project: $migrationProject" -ForegroundColor Gray
Write-Host "  Backfill Script: $backfillScript" -ForegroundColor Gray
Write-Host ""

# Step 1: Apply schema migration
Write-Host "[Step 1/3] Applying schema migration..." -ForegroundColor Green

try {
    $env:DatabaseProvider = $DatabaseProvider
    
    Push-Location $rootPath
    dotnet ef database update --project $migrationProject --startup-project Web.IdP --context ApplicationDbContext
    
    if ($LASTEXITCODE -ne 0) {
        throw "Migration failed with exit code $LASTEXITCODE"
    }
    
    Write-Host "  ✓ Schema migration applied successfully" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "  ✗ Schema migration failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
    $env:DatabaseProvider = $null
}

# Step 2: Run backfill script
if (-not $SkipBackfill) {
    Write-Host "[Step 2/3] Running data backfill..." -ForegroundColor Green
    
    if (-not (Test-Path $backfillScript)) {
        Write-Host "  ✗ Backfill script not found: $backfillScript" -ForegroundColor Red
        exit 1
    }
    
    try {
        # Read connection string from appsettings
        $appsettingsPath = Join-Path $webIdpPath "appsettings.Development.json"
        $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json
        $connectionString = $appsettings.ConnectionStrings.$connectionStringKey
        
        if ($DatabaseProvider -eq "PostgreSQL") {
            # PostgreSQL: Use psql
            Write-Host "  Executing PostgreSQL backfill script..." -ForegroundColor Gray
            
            # Parse connection string (simplified)
            if ($connectionString -match "Host=([^;]+).*Port=([^;]+).*Database=([^;]+).*Username=([^;]+).*Password=([^;]+)") {
                $host = $matches[1]
                $port = $matches[2]
                $database = $matches[3]
                $username = $matches[4]
                $password = $matches[5]
                
                $env:PGPASSWORD = $password
                psql -h $host -p $port -d $database -U $username -f $backfillScript
                $env:PGPASSWORD = $null
                
                if ($LASTEXITCODE -ne 0) {
                    throw "PostgreSQL backfill script failed"
                }
            } else {
                throw "Could not parse PostgreSQL connection string"
            }
        } else {
            # SQL Server: Use sqlcmd
            Write-Host "  Executing SQL Server backfill script..." -ForegroundColor Gray
            
            sqlcmd -S localhost,1433 -U sa -P "YourStrong!Passw0rd" -d hybridauth_idp -i $backfillScript -C
            
            if ($LASTEXITCODE -ne 0) {
                throw "SQL Server backfill script failed"
            }
        }
        
        Write-Host "  ✓ Data backfill completed successfully" -ForegroundColor Green
        Write-Host ""
    } catch {
        Write-Host "  ✗ Data backfill failed: $_" -ForegroundColor Red
        Write-Host "  Note: You can manually run the backfill script later." -ForegroundColor Yellow
        Write-Host ""
    }
} else {
    Write-Host "[Step 2/3] Skipping data backfill (--SkipBackfill specified)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Verify migration
Write-Host "[Step 3/3] Verifying migration..." -ForegroundColor Green

try {
    Push-Location $rootPath
    
    # Build and run basic tests
    dotnet test --filter "FullyQualifiedName~PersonEntityTests" --no-build --verbosity quiet
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Tests passed successfully" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Some tests failed, but migration was applied" -ForegroundColor Yellow
    }
    
    Write-Host ""
} catch {
    Write-Host "  ⚠ Could not run tests: $_" -ForegroundColor Yellow
    Write-Host ""
} finally {
    Pop-Location
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host " Phase 10.1 Migration Complete! ✓" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor Yellow
Write-Host "  • Person table created" -ForegroundColor Gray
Write-Host "  • PersonId column added to AspNetUsers" -ForegroundColor Gray
Write-Host "  • Foreign key relationship established" -ForegroundColor Gray

if (-not $SkipBackfill) {
    Write-Host "  • Existing users linked to Person records" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Review Person records in database" -ForegroundColor Gray
Write-Host "  2. Proceed with Phase 10.2 (Services & API)" -ForegroundColor Gray
Write-Host ""
