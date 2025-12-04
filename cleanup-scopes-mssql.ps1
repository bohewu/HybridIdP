# Cleanup E2E test scopes in SQL Server container used by dev docker-compose
Write-Host "🧹 Running MSSQL scopes cleanup (hybridauth_idp DB) — removing e2e_* and diag-scope_* scopes..." -ForegroundColor Cyan

$saPassword = "YourStrong!Passw0rd"
$server = "mssql-service"
$database = "hybridauth_idp"

$sql = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- Show counts before cleanup
SELECT 'E2EScopesBeforeCleanup' as q, COUNT(*) FROM [OpenIddictScopes] WHERE [Name] LIKE 'e2e_%';
SELECT 'DiagScopesBeforeCleanup' as q, COUNT(*) FROM [OpenIddictScopes] WHERE [Name] LIKE 'diag-scope_%';

-- Remove E2E test scopes (those starting with e2e_ or diag-scope_)
DELETE FROM [OpenIddictScopes]
WHERE [Name] LIKE 'e2e_%' OR [Name] LIKE 'diag-scope_%';

-- Show counts after cleanup for verification
SELECT 'ScopesRemaining' as q, COUNT(*) FROM [OpenIddictScopes];
SELECT 'E2EScopesRemaining' as q, COUNT(*) FROM [OpenIddictScopes] WHERE [Name] LIKE 'e2e_%';
SELECT 'DiagScopesRemaining' as q, COUNT(*) FROM [OpenIddictScopes] WHERE [Name] LIKE 'diag-scope_%';
"@

Write-Host "Executing against mssql://${server}/${database}..." -ForegroundColor Yellow
docker run --rm --network hybrididp_default mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S $server -U sa -P $saPassword -d $database -Q $sql -W -s ","

Write-Host "🎉 MSSQL scopes cleanup finished." -ForegroundColor Green
