# cleanup-e2e-test-data.ps1
# Cleans up all e2e-* prefixed test data from the database
# Use this script periodically to remove orphaned test data from failed test runs

Write-Host "🧹 Cleaning up E2E test data..." -ForegroundColor Cyan

# Configuration
$IdPUrl = "https://localhost:7035"
$AdminEmail = "admin@hybridauth.local"
$AdminPassword = "Admin@123"

# Function to authenticate and get cookies
function Get-AuthSession {
    $loginUrl = "$IdPUrl/Account/Login"
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    
    try {
        # Get the login page first (to get any tokens/cookies)
        $response = Invoke-WebRequest -Uri $loginUrl -SessionVariable session -SkipCertificateCheck -ErrorAction Stop
        
        # Perform login
        $loginBody = @{
            "Input.Login" = $AdminEmail
            "Input.Password" = $AdminPassword
            "Input.RememberMe" = "false"
        }
        
        $response = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $loginBody -SessionVariable session -SkipCertificateCheck -ErrorAction Stop
        
        return $session
    }
    catch {
        Write-Host "❌ Failed to authenticate: $_" -ForegroundColor Red
        return $null
    }
}

# Get authenticated session
Write-Host "🔐 Authenticating as admin..." -ForegroundColor Yellow
$session = Get-AuthSession

if ($null -eq $session) {
    Write-Host "❌ Authentication failed. Make sure the IdP is running and credentials are correct." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Authenticated successfully" -ForegroundColor Green

# Clean up users
Write-Host "`n👤 Cleaning up E2E test users..." -ForegroundColor Yellow
try {
    $usersResponse = Invoke-RestMethod -Uri "$IdPUrl/api/admin/users?search=e2e-&take=1000" -WebSession $session -SkipCertificateCheck
    $users = if ($usersResponse.items) { $usersResponse.items } else { @() }
    
    $deletedUsers = 0
    foreach ($user in $users) {
        if ($user.email -like "e2e-*") {
            try {
                Invoke-RestMethod -Uri "$IdPUrl/api/admin/users/$($user.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction SilentlyContinue | Out-Null
                $deletedUsers++
                Write-Host "  ✓ Deleted user: $($user.email)" -ForegroundColor Gray
            }
            catch {
                Write-Host "  ⚠ Failed to delete user $($user.email): $_" -ForegroundColor DarkYellow
            }
        }
    }
    Write-Host "✅ Deleted $deletedUsers e2e test users" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to fetch users: $_" -ForegroundColor Red
}

# Clean up roles
Write-Host "`n👥 Cleaning up E2E test roles..." -ForegroundColor Yellow
try {
    $rolesResponse = Invoke-RestMethod -Uri "$IdPUrl/api/admin/roles?search=e2e-&take=1000" -WebSession $session -SkipCertificateCheck
    $roles = if ($rolesResponse.items) { $rolesResponse.items } else { @() }
    
    $deletedRoles = 0
    foreach ($role in $roles) {
        if ($role.name -like "e2e-*") {
            try {
                Invoke-RestMethod -Uri "$IdPUrl/api/admin/roles/$($role.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction SilentlyContinue | Out-Null
                $deletedRoles++
                Write-Host "  ✓ Deleted role: $($role.name)" -ForegroundColor Gray
            }
            catch {
                Write-Host "  ⚠ Failed to delete role $($role.name): $_" -ForegroundColor DarkYellow
            }
        }
    }
    Write-Host "✅ Deleted $deletedRoles e2e test roles" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to fetch roles: $_" -ForegroundColor Red
}

# Clean up clients
Write-Host "`n🔑 Cleaning up E2E test clients..." -ForegroundColor Yellow
try {
    $clientsResponse = Invoke-RestMethod -Uri "$IdPUrl/api/admin/clients?search=e2e-&take=1000" -WebSession $session -SkipCertificateCheck
    $clients = if ($clientsResponse.items) { $clientsResponse.items } else { @() }
    
    $deletedClients = 0
    foreach ($client in $clients) {
        if ($client.clientId -like "e2e-*") {
            try {
                Invoke-RestMethod -Uri "$IdPUrl/api/admin/clients/$($client.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction SilentlyContinue | Out-Null
                $deletedClients++
                Write-Host "  ✓ Deleted client: $($client.clientId)" -ForegroundColor Gray
            }
            catch {
                Write-Host "  ⚠ Failed to delete client $($client.clientId): $_" -ForegroundColor DarkYellow
            }
        }
    }
    Write-Host "✅ Deleted $deletedClients e2e test clients" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to fetch clients: $_" -ForegroundColor Red
}

# Clean up scopes
Write-Host "`n🔍 Cleaning up E2E test scopes..." -ForegroundColor Yellow
try {
    $scopesResponse = Invoke-RestMethod -Uri "$IdPUrl/api/admin/scopes?search=e2e-&take=1000" -WebSession $session -SkipCertificateCheck
    $scopes = if ($scopesResponse.items) { $scopesResponse.items } else { @() }
    
    $deletedScopes = 0
    foreach ($scope in $scopes) {
        if ($scope.name -like "e2e-*") {
            try {
                Invoke-RestMethod -Uri "$IdPUrl/api/admin/scopes/$($scope.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction SilentlyContinue | Out-Null
                $deletedScopes++
                Write-Host "  ✓ Deleted scope: $($scope.name)" -ForegroundColor Gray
            }
            catch {
                Write-Host "  ⚠ Failed to delete scope $($scope.name): $_" -ForegroundColor DarkYellow
            }
        }
    }
    Write-Host "✅ Deleted $deletedScopes e2e test scopes" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to fetch scopes: $_" -ForegroundColor Red
}

# Clean up API resources
Write-Host "`n🌐 Cleaning up E2E test API resources..." -ForegroundColor Yellow
try {
    $resourcesResponse = Invoke-RestMethod -Uri "$IdPUrl/api/admin/resources?search=e2e-&take=1000" -WebSession $session -SkipCertificateCheck
    $resources = if ($resourcesResponse.items) { $resourcesResponse.items } else { @() }
    
    $deletedResources = 0
    foreach ($resource in $resources) {
        if ($resource.name -like "e2e-*") {
            try {
                Invoke-RestMethod -Uri "$IdPUrl/api/admin/resources/$($resource.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction SilentlyContinue | Out-Null
                $deletedResources++
                Write-Host "  ✓ Deleted API resource: $($resource.name)" -ForegroundColor Gray
            }
            catch {
                Write-Host "  ⚠ Failed to delete API resource $($resource.name): $_" -ForegroundColor DarkYellow
            }
        }
    }
    Write-Host "✅ Deleted $deletedResources e2e test API resources" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to fetch API resources: $_" -ForegroundColor Red
}

Write-Host "`n🎉 E2E test data cleanup completed!" -ForegroundColor Green
Write-Host "📊 Summary:" -ForegroundColor Cyan
Write-Host "   Users: $deletedUsers" -ForegroundColor White
Write-Host "   Roles: $deletedRoles" -ForegroundColor White
Write-Host "   Clients: $deletedClients" -ForegroundColor White
Write-Host "   Scopes: $deletedScopes" -ForegroundColor White
Write-Host "   API Resources: $deletedResources" -ForegroundColor White
