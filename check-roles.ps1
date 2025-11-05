# Quick check: Verify role has permissions
$baseUrl = "https://localhost:7035"

# Login as admin
$loginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method GET -SessionVariable session -SkipCertificateCheck
$token = if ($loginPage.Content -match 'name="__RequestVerificationToken".*?value="([^"]+)"') { $Matches[1] } else { throw "No token" }
Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method POST -WebSession $session -SkipCertificateCheck `
    -Body @{Email="admin@hybridauth.local"; Password="Admin@123"; __RequestVerificationToken=$token; RememberMe="false"} `
    -MaximumRedirection 0 -ErrorAction SilentlyContinue | Out-Null

# Get all roles
Write-Host "=== All Roles ===" -ForegroundColor Cyan
$rolesResponse = Invoke-RestMethod -Uri "$baseUrl/api/admin/roles?skip=0&take=100" -Method GET -WebSession $session -SkipCertificateCheck
if ($rolesResponse.items) {
    $rolesResponse.items | ForEach-Object {
        Write-Host "Role: $($_.name) (ID: $($_.id))" -ForegroundColor Yellow
        Write-Host "  Permissions: $($_.permissions -join ', ')" -ForegroundColor Gray
        Write-Host "  User Count: $($_.userCount)" -ForegroundColor Gray
    }
}

# Get users with their roles
Write-Host "`n=== All Users ===" -ForegroundColor Cyan
$usersResponse = Invoke-RestMethod -Uri "$baseUrl/api/admin/users?skip=0&take=100" -Method GET -WebSession $session -SkipCertificateCheck
if ($usersResponse.users) {
    $usersResponse.users | ForEach-Object {
        Write-Host "User: $($_.email) (ID: $($_.id))" -ForegroundColor Yellow
        Write-Host "  Roles: $($_.roles -join ', ')" -ForegroundColor Gray
    }
}
