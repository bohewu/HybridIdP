# Cleanup all users and persons except admin@hybridauth.local and admin's linked person

Write-Host "🧹 Cleaning up test users and persons..." -ForegroundColor Cyan

# Step 1: Delete persons not linked to admin
$deletePersons = @"
DELETE FROM "Persons"
WHERE "Id" NOT IN (
    SELECT "PersonId" FROM "AspNetUsers"
    WHERE "Email" = 'admin@hybridauth.local' AND "PersonId" IS NOT NULL
);
"@

# Step 2: Delete user roles for non-admin users
$deleteUserRoles = @"
DELETE FROM "AspNetUserRoles"
WHERE "UserId" IN (
    SELECT "Id" FROM "AspNetUsers"
    WHERE "Email" != 'admin@hybridauth.local'
);
"@

# Step 3: Delete non-admin users
$deleteUsers = @"
DELETE FROM "AspNetUsers"
WHERE "Email" != 'admin@hybridauth.local';
"@

# Step 4: Verify remaining data
$verifyUsers = @"
SELECT "Email", "UserName", "PersonId" FROM "AspNetUsers";
"@

$verifyPersons = @"
SELECT "Id", "FirstName", "LastName", "EmployeeId", "NationalId", "PassportNumber", "ResidentCertificateNumber", "IdentityVerifiedAt" FROM "Persons";
"@

Write-Host "`n👤 Deleting persons (except admin's person)..." -ForegroundColor Yellow
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $deletePersons

Write-Host "`n🔑 Deleting user roles..." -ForegroundColor Yellow
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $deleteUserRoles

Write-Host "`n👥 Deleting users (except admin)..." -ForegroundColor Yellow
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $deleteUsers

Write-Host "`n✅ Verifying remaining users:" -ForegroundColor Green
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $verifyUsers

Write-Host "`n✅ Verifying remaining persons:" -ForegroundColor Green
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $verifyPersons

Write-Host "`n🎉 Cleanup completed!" -ForegroundColor Green
