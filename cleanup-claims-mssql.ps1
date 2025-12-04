# Cleanup E2E test claims in SQL Server container used by dev docker-compose
Write-Host "🧹 Running MSSQL claims cleanup (hybridauth_idp DB) — removing e2e_* claims..." -ForegroundColor Cyan

$saPassword = "YourStrong!Passw0rd"
$server = "mssql-service"
$database = "hybridauth_idp"

$sql = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- Show counts before cleanup
SELECT 'ClaimsBeforeCleanup' as q, COUNT(*) FROM [Core.Application.IApplicationDbContext.UserClaims] WHERE [Name] LIKE 'e2e_%';

-- First remove references in ScopeClaims table
DELETE FROM [ScopeClaims]
WHERE [UserClaimId] IN (
    SELECT [Id] FROM [Core.Application.IApplicationDbContext.UserClaims] WHERE [Name] LIKE 'e2e_%'
);

-- Remove E2E test claims (those starting with e2e_)
DELETE FROM [Core.Application.IApplicationDbContext.UserClaims]
WHERE [Name] LIKE 'e2e_%';

-- Show counts after cleanup for verification
SELECT 'ClaimsRemaining' as q, COUNT(*) FROM [Core.Application.IApplicationDbContext.UserClaims];
SELECT 'E2EClaimsRemaining' as q, COUNT(*) FROM [Core.Application.IApplicationDbContext.UserClaims] WHERE [Name] LIKE 'e2e_%';
"@

Write-Host "Executing against mssql://${server}/${database}..." -ForegroundColor Yellow
docker run --rm --network hybrididp_default mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S $server -U sa -P $saPassword -d $database -Q $sql -W -s ","

Write-Host "🎉 MSSQL claims cleanup finished." -ForegroundColor Green
