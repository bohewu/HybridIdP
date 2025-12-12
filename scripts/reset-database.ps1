<#
.SYNOPSIS
重置 HybridIdP 資料庫到乾淨狀態，用於 E2E 測試前的環境準備

.DESCRIPTION
此腳本會:
1. 清理所有測試資料 (Persons, ApplicationUsers, Roles, Claims, ApiResources 等)
2. 保留必要的系統資料或透過 DataSeeder 重新建立
3. 可選擇完全重建資料庫 (預設: 否)

.PARAMETER DropDatabase
設為 $true 會完全刪除並重建資料庫 (預設: $false，只清理資料)

.PARAMETER Provider
資料庫類型: SqlServer 或 PostgreSQL (預設: SqlServer)

.PARAMETER SkipSeeder
設為 $true 會跳過執行 DataSeeder (預設: $false)

.EXAMPLE
# 只清理資料，保留資料庫結構
.\ci\reset-database.ps1

# 完全重建資料庫
.\ci\reset-database.ps1 -DropDatabase $true

# 指定 PostgreSQL
.\ci\reset-database.ps1 -Provider PostgreSQL

# 清理資料但跳過 seeder
.\ci\reset-database.ps1 -SkipSeeder $true
#>

param(
    [bool]$DropDatabase = $false,
    [ValidateSet("SqlServer", "PostgreSQL")]
    [string]$Provider = "SqlServer",
    [bool]$SkipSeeder = $false
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

# Database configuration
$SqlServerContainer = "hybrididp-mssql-service-1"
$PostgresContainer = "hybrididp-postgres-service-1"
$SqlServerPassword = "YourStrong!Passw0rd"
$PostgresUser = "user"
$DatabaseName = "hybridauth_idp"

Write-Host "================================" -ForegroundColor Cyan
Write-Host " HybridIdP 資料庫重置腳本" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Provider: $Provider" -ForegroundColor Yellow
Write-Host "DropDatabase: $DropDatabase" -ForegroundColor Yellow
Write-Host "SkipSeeder: $SkipSeeder" -ForegroundColor Yellow
Write-Host ""

function Invoke-SqlServerQuery {
    param([string]$Query)
    $result = docker exec $SqlServerContainer /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P $SqlServerPassword -d $DatabaseName -C -Q $Query 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "SQL Error: $result" -ForegroundColor Red
        return $false
    }
    return $true
}

function Invoke-PostgresQuery {
    param([string]$Query)
    $result = docker exec $PostgresContainer psql -U $PostgresUser -d $DatabaseName -c $Query 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "PostgreSQL Error: $result" -ForegroundColor Red
        return $false
    }
    return $true
}

function Test-ContainerRunning {
    param([string]$ContainerName)
    $container = docker ps --filter "name=$ContainerName" --format "{{.Names}}" 2>&1
    return $container -eq $ContainerName
}

# Check Docker container
$ContainerName = if ($Provider -eq "SqlServer") { $SqlServerContainer } else { $PostgresContainer }
if (-not (Test-ContainerRunning $ContainerName)) {
    Write-Host "Error: Docker container '$ContainerName' is not running." -ForegroundColor Red
    Write-Host "Please run: docker-compose -f docker-compose.dev.yml up -d" -ForegroundColor Yellow
    exit 1
}

Write-Host "Docker container '$ContainerName' is running." -ForegroundColor Green

if ($DropDatabase) {
    Write-Host ""
    Write-Host "=== 重建資料庫 ===" -ForegroundColor Magenta
    
    if ($Provider -eq "SqlServer") {
        Write-Host "Dropping and recreating SQL Server database..." -ForegroundColor Yellow
        docker exec $SqlServerContainer /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P $SqlServerPassword -C -Q "IF EXISTS (SELECT name FROM sys.databases WHERE name = N'$DatabaseName') BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];"
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Failed to drop/create database" -ForegroundColor Red
            exit 1
        }
        
        Write-Host "Database recreated. Applying migrations..." -ForegroundColor Yellow
        Push-Location (Join-Path $RepoRoot "Infrastructure.Migrations.SqlServer")
        $env:DATABASE_PROVIDER = "SqlServer"
        dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
        Pop-Location
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Migration failed" -ForegroundColor Red
            exit 1
        }
    }
    else {
        Write-Host "Dropping and recreating PostgreSQL database..." -ForegroundColor Yellow
        docker exec $PostgresContainer psql -U $PostgresUser -d postgres -c "SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '$DatabaseName' AND pid <> pg_backend_pid();"
        docker exec $PostgresContainer psql -U $PostgresUser -d postgres -c "DROP DATABASE IF EXISTS $DatabaseName;"
        docker exec $PostgresContainer psql -U $PostgresUser -d postgres -c "CREATE DATABASE $DatabaseName;"
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Failed to drop/create database" -ForegroundColor Red
            exit 1
        }
        
        Write-Host "Database recreated. Applying migrations..." -ForegroundColor Yellow
        Push-Location (Join-Path $RepoRoot "Infrastructure.Migrations.Postgres")
        $env:DATABASE_PROVIDER = "PostgreSQL"
        dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
        Pop-Location
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Migration failed" -ForegroundColor Red
            exit 1
        }
    }
    
    Write-Host "Database structure recreated successfully!" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "=== 清理資料 (保留資料庫結構) ===" -ForegroundColor Magenta
    
    # Define cleanup order (respecting FK constraints)
    # Order: child tables first, parent tables last
    
    if ($Provider -eq "SqlServer") {
        $cleanupQueries = @(
            # Session and Auth related
            "DELETE FROM UserSessions;",
            "DELETE FROM LoginHistories;",
            
            # OpenIddict tokens and authorizations  
            "DELETE FROM OpenIddictTokens;",
            "DELETE FROM OpenIddictAuthorizations;",
            
            # Ownership tables (must be before Persons/Users)
            "DELETE FROM ClientOwnerships;",
            "DELETE FROM ScopeOwnerships;",
            
            # Scope related
            "DELETE FROM ScopeClaims;",
            "DELETE FROM ScopeExtensions;",
            "DELETE FROM ClientRequiredScopes;",
            "DELETE FROM ApiResourceScopes;",
            
            # API Resources
            "DELETE FROM ApiResources;",
            
            # User Claims (custom, not Identity)
            "DELETE FROM UserClaims;",
            
            # ASP.NET Identity tables (in correct order)
            "DELETE FROM AspNetUserTokens;",
            "DELETE FROM AspNetUserLogins;",
            "DELETE FROM AspNetUserClaims;",
            "DELETE FROM AspNetUserRoles;",
            "DELETE FROM AspNetUsers;",
            
            # Roles (keep system roles or delete all)
            "DELETE FROM AspNetRoleClaims;",
            "DELETE FROM AspNetRoles;",
            
            # Person table
            "DELETE FROM Persons;",
            
            # OpenIddict Applications and Scopes
            "DELETE FROM OpenIddictApplications;",
            "DELETE FROM OpenIddictScopes;",
            
            # Audit (optional - keep for debugging)
            "DELETE FROM AuditEvents;",
            
            # Resources (localization)
            "DELETE FROM Resources;",
            
            # Settings and Security Policies
            "DELETE FROM Settings;",
            "DELETE FROM SecurityPolicies;"
        )
        
        foreach ($query in $cleanupQueries) {
            Write-Host "  Executing: $query" -ForegroundColor Gray
            $result = Invoke-SqlServerQuery $query
            if (-not $result) {
                Write-Host "  Warning: Query may have failed, continuing..." -ForegroundColor Yellow
            }
        }
    }
    else {
        # PostgreSQL queries (table names are quoted for case-sensitivity)
        $cleanupQueries = @(
            'DELETE FROM "UserSessions";',
            'DELETE FROM "LoginHistories";',
            'DELETE FROM "OpenIddictTokens";',
            'DELETE FROM "OpenIddictAuthorizations";',
            'DELETE FROM "ClientOwnerships";',
            'DELETE FROM "ScopeOwnerships";',
            'DELETE FROM "ScopeClaims";',
            'DELETE FROM "ScopeExtensions";',
            'DELETE FROM "ClientRequiredScopes";',
            'DELETE FROM "ApiResourceScopes";',
            'DELETE FROM "ApiResources";',
            'DELETE FROM "UserClaims";',
            'DELETE FROM "AspNetUserTokens";',
            'DELETE FROM "AspNetUserLogins";',
            'DELETE FROM "AspNetUserClaims";',
            'DELETE FROM "AspNetUserRoles";',
            'DELETE FROM "AspNetUsers";',
            'DELETE FROM "AspNetRoleClaims";',
            'DELETE FROM "AspNetRoles";',
            'DELETE FROM "Persons";',
            'DELETE FROM "OpenIddictApplications";',
            'DELETE FROM "OpenIddictScopes";',
            'DELETE FROM "AuditEvents";',
            'DELETE FROM "Resources";',
            'DELETE FROM "Settings";',
            'DELETE FROM "SecurityPolicies";'
        )
        
        foreach ($query in $cleanupQueries) {
            Write-Host "  Executing: $query" -ForegroundColor Gray
            $result = Invoke-PostgresQuery $query
            if (-not $result) {
                Write-Host "  Warning: Query may have failed, continuing..." -ForegroundColor Yellow
            }
        }
    }
    
    Write-Host "Data cleanup completed!" -ForegroundColor Green
}

# Run DataSeeder to restore essential data
if (-not $SkipSeeder) {
    Write-Host ""
    Write-Host "=== 執行 DataSeeder 重建必要資料 ===" -ForegroundColor Magenta
    Write-Host "Starting Web.IdP to trigger DataSeeder..." -ForegroundColor Yellow
    
    # Create a temporary runner to execute DataSeeder
    $seederScript = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
// The actual seeding happens in Web.IdP startup
Console.WriteLine("Seeder invoked via application startup.");
"@
    
    # Run the application briefly to trigger seeding
    Push-Location (Join-Path $RepoRoot "Web.IdP")
    
    # Set environment based on provider
    if ($Provider -eq "PostgreSQL") {
        $env:DATABASE_PROVIDER = "PostgreSQL"
    } else {
        $env:DATABASE_PROVIDER = "SqlServer"
    }
    
    # Use dotnet run with a timeout - it will seed on startup
    Write-Host "Running Web.IdP for database seeding (will stop after a few seconds)..." -ForegroundColor Yellow
    
    $job = Start-Job -ScriptBlock {
        param($workDir, $provider)
        Set-Location $workDir
        $env:DATABASE_PROVIDER = $provider
        dotnet run --no-build 2>&1
    } -ArgumentList (Get-Location), $Provider
    
    # Wait for seeding to complete (usually takes 5-10 seconds)
    Start-Sleep -Seconds 15
    
    # Stop the job
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    
    Pop-Location
    
    Write-Host "DataSeeder completed!" -ForegroundColor Green
    
    # Register TestClient for E2E tests
    Write-Host ""
    Write-Host "=== 註冊 TestClient (E2E 測試用) ===" -ForegroundColor Magenta
    
    if ($Provider -eq "SqlServer") {
        $testClientSql = Join-Path $RepoRoot "create-testclient-mssql.sql"
        if (Test-Path $testClientSql) {
            Get-Content $testClientSql | docker exec -i $SqlServerContainer /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P $SqlServerPassword -d $DatabaseName -C
            Write-Host "TestClient registered!" -ForegroundColor Green
        } else {
            Write-Host "Warning: create-testclient-mssql.sql not found" -ForegroundColor Yellow
        }
    }
    else {
        $testClientSql = Join-Path $RepoRoot "create-testclient.sql"
        if (Test-Path $testClientSql) {
            Get-Content $testClientSql | docker exec -i $PostgresContainer psql -U $PostgresUser -d $DatabaseName
            Write-Host "TestClient registered!" -ForegroundColor Green
        } else {
            Write-Host "Warning: create-testclient.sql not found" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host " 資料庫重置完成!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "已建立的資料:" -ForegroundColor Yellow
Write-Host "  - 系統角色: Admin, User, ApplicationManager" -ForegroundColor White
Write-Host "  - Admin 使用者: admin@hybridauth.local / Admin@123" -ForegroundColor White
Write-Host "  - 預設安全政策和設定" -ForegroundColor White
Write-Host "  - TestClient (E2E 測試用)" -ForegroundColor White
Write-Host ""
Write-Host "如需測試使用者，請以 seedTestUsers=true 啟動應用程式" -ForegroundColor Gray
