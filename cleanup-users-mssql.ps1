# Cleanup non-admin users/persons in SQL Server container used by dev docker-compose
Write-Host "🧹 Running MSSQL cleanup (hybridauth_idp DB) — removing non-admin users and persons..." -ForegroundColor Cyan

$saPassword = "YourStrong!Passw0rd"
$server = "mssql-service"
$database = "hybridauth_idp"

$sql = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- Remove user role entries for non-admin users
DELETE FROM [AspNetUserRoles]
WHERE [UserId] IN (
    SELECT [Id] FROM [AspNetUsers] WHERE [Email] != 'admin@hybridauth.local'
);

-- Remove non-admin users
DELETE FROM [AspNetUsers]
WHERE [Email] != 'admin@hybridauth.local';

-- Remove persons not linked to admin
DELETE FROM [Persons]
WHERE [Id] NOT IN (
    SELECT [PersonId] FROM [AspNetUsers] WHERE [Email] = 'admin@hybridauth.local' AND [PersonId] IS NOT NULL
);

-- Show counts for verification
SELECT 'UsersRemaining' as q, COUNT(*) FROM [AspNetUsers];
SELECT 'PersonsRemaining' as q, COUNT(*) FROM [Persons];
"@

Write-Host "Executing against mssql://${server}/${database}..." -ForegroundColor Yellow
docker run --rm --network hybrididp_default mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S $server -U sa -P $saPassword -d $database -Q $sql -W -s ","

Write-Host "🎉 MSSQL cleanup finished." -ForegroundColor Green
