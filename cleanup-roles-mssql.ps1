# Cleanup E2E test roles in SQL Server container used by dev docker-compose
Write-Host "🧹 Running MSSQL roles cleanup (hybridauth_idp DB) — removing e2e_* roles..." -ForegroundColor Cyan

$saPassword = "YourStrong!Passw0rd"
$server = "mssql-service"
$database = "hybridauth_idp"

$sql = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- Show counts before cleanup
SELECT 'RolesBeforeCleanup' as q, COUNT(*) FROM [AspNetRoles] WHERE [Name] LIKE 'e2e_%';

-- First remove role claims for E2E test roles
DELETE FROM [AspNetRoleClaims]
WHERE [RoleId] IN (
    SELECT [Id] FROM [AspNetRoles] WHERE [Name] LIKE 'e2e_%'
);

-- Remove user role assignments for E2E test roles
DELETE FROM [AspNetUserRoles]
WHERE [RoleId] IN (
    SELECT [Id] FROM [AspNetRoles] WHERE [Name] LIKE 'e2e_%'
);

-- Remove E2E test roles (those starting with e2e_)
DELETE FROM [AspNetRoles]
WHERE [Name] LIKE 'e2e_%';

-- Show counts after cleanup for verification
SELECT 'RolesRemaining' as q, COUNT(*) FROM [AspNetRoles];
SELECT 'E2ERolesRemaining' as q, COUNT(*) FROM [AspNetRoles] WHERE [Name] LIKE 'e2e_%';
"@

Write-Host "Executing against mssql://${server}/${database}..." -ForegroundColor Yellow
docker run --rm --network hybrididp_default mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S $server -U sa -P $saPassword -d $database -Q $sql -W -s ","

Write-Host "🎉 MSSQL roles cleanup finished." -ForegroundColor Green
