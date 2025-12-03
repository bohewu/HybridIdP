# Cleanup non-admin users/persons in Postgres container used by dev docker-compose
Write-Host "🧹 Running Postgres cleanup (hybridauth_idp DB) — removing non-admin users and persons..." -ForegroundColor Cyan

$psqlUser = 'user'
$psqlDb = 'hybridauth_idp'
$container = 'hybrididp-postgres-service-1'

$deletePersons = @"
DELETE FROM "Persons"
WHERE "Id" NOT IN (
    SELECT "PersonId" FROM "AspNetUsers" WHERE "Email" = 'admin@hybridauth.local' AND "PersonId" IS NOT NULL
);
"@

$deleteUserRoles = @"
DELETE FROM "AspNetUserRoles"
WHERE "UserId" IN (
    SELECT "Id" FROM "AspNetUsers" WHERE "Email" != 'admin@hybridauth.local'
);
"@

$deleteUsers = @"
DELETE FROM "AspNetUsers"
WHERE "Email" != 'admin@hybridauth.local';
"@

$verifyUsers = @"
SELECT "Email", "UserName", "PersonId" FROM "AspNetUsers";
"@

$verifyPersons = @"
SELECT "Id", "FirstName", "LastName", "EmployeeId", "NationalId", "PassportNumber", "ResidentCertificateNumber", "IdentityVerifiedAt" FROM "Persons";
"@

Write-Host "Deleting persons (except admin's person) ..." -ForegroundColor Yellow
docker exec -i $container psql -U $psqlUser -d $psqlDb -c $deletePersons

Write-Host "Deleting user roles ..." -ForegroundColor Yellow
docker exec -i $container psql -U $psqlUser -d $psqlDb -c $deleteUserRoles

Write-Host "Deleting users (except admin) ..." -ForegroundColor Yellow
docker exec -i $container psql -U $psqlUser -d $psqlDb -c $deleteUsers

Write-Host "Verifying remaining users:" -ForegroundColor Green
docker exec -i $container psql -U $psqlUser -d $psqlDb -c $verifyUsers

Write-Host "Verifying remaining persons:" -ForegroundColor Green
docker exec -i $container psql -U $psqlUser -d $psqlDb -c $verifyPersons

Write-Host "🎉 Postgres cleanup finished." -ForegroundColor Green
