# Cleanup all users except admin@hybridauth.local

$deleteUserRoles = @"
DELETE FROM "AspNetUserRoles" 
WHERE "UserId" IN (
    SELECT "Id" FROM "AspNetUsers" 
    WHERE "Email" != 'admin@hybridauth.local'
);
"@

$deleteUsers = @"
DELETE FROM "AspNetUsers" 
WHERE "Email" != 'admin@hybridauth.local';
"@

$verifyUsers = @"
SELECT "Email", "UserName" FROM "AspNetUsers";
"@

Write-Host "Deleting user roles..." -ForegroundColor Yellow
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $deleteUserRoles

Write-Host "`nDeleting users..." -ForegroundColor Yellow
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $deleteUsers

Write-Host "`nVerifying remaining users:" -ForegroundColor Green
docker exec -i hybrididp-postgres-service-1 psql -U user -d hybridauth_idp -c $verifyUsers
