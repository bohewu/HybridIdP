# Setup Test API Resources and Scopes via Admin API
# This script creates API resources and scopes, then tests the JWT aud claim

$baseUrl = "https://localhost:7035"
$adminEmail = "admin@hybridauth.local"
$adminPassword = "Admin@123"

# Create a session and log in so we can call protected Admin API endpoints
Write-Host "Signing in as admin to obtain Admin API session..." -ForegroundColor Cyan
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginUri = "$baseUrl/Account/Login"
try {
    $loginPage = Invoke-WebRequest -Uri $loginUri -WebSession $session -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
    $form = @{}
    $inputPattern = '<input\s+[^>]*name=["\''](?<name>[^"\'']+)["\''][^>]*value=["\''](?<value>[^"\'']*)["\''][^>]*>'
    foreach ($m in [regex]::Matches($loginPage.Content, $inputPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $n = $m.Groups['name'].Value; $v = $m.Groups['value'].Value
        if (-not [string]::IsNullOrEmpty($n)) { $form[$n] = $v }
    }
    $form['Input.Login'] = $adminEmail
    $form['Input.Password'] = $adminPassword
    $form['Input.RememberMe'] = 'false'
    Invoke-WebRequest -Uri $loginUri -WebSession $session -Method Post -Body $form -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
    Write-Host 'Admin login attempted, session created.' -ForegroundColor Green
}
catch {
    Write-Host "Admin login failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host 'Continuing — admin API calls may fail if not authenticated.' -ForegroundColor Yellow
}
# Ignore SSL certificate errors for local development
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
Add-Type @"
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    public class TrustAllCertsPolicy : ICertificatePolicy {
        public bool CheckValidationResult(
            ServicePoint srvPoint, X509Certificate certificate,
            WebRequest request, int certificateProblem) {
            return true;
        }
    }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy

Write-Host "=== Setting up Test API Resources and Scopes ===" -ForegroundColor Cyan

# Step 1: Create API Scopes
Write-Host "`nStep 1: Creating API scopes..." -ForegroundColor Yellow

$scopes = @(
    @{
        name = "api:company:read"
        displayName = "Read Company Data"
        description = "Allows reading company information"
    },
    @{
        name = "api:company:write"
        displayName = "Write Company Data"
        description = "Allows creating and updating company information"
    },
    @{
        name = "api:inventory:read"
        displayName = "Read Inventory Data"
        description = "Allows reading inventory information"
    }
)

$createdScopeIds = @{}

foreach ($scope in $scopes) {
    Write-Host "  Creating scope: $($scope.name)..." -NoNewline
    
    $body = @{
        name = $scope.name
        displayName = $scope.displayName
        description = $scope.description
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/admin/scopes" `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -WebSession $session `
            -SkipCertificateCheck `
            -ErrorAction SilentlyContinue
        
        $createdScopeIds[$scope.name] = $response.id
        Write-Host " Created (ID: $($response.id))" -ForegroundColor Green
    }
    catch {
        Write-Host " Failed or already exists" -ForegroundColor Yellow
        # Try to get existing scope
        try {
            $allScopes = Invoke-RestMethod -Uri "$baseUrl/api/admin/scopes?take=100" `
                -Method Get `
                -SkipCertificateCheck
            
            $existingScope = $allScopes.items | Where-Object { $_.name -eq $scope.name }
            if ($existingScope) {
                $createdScopeIds[$scope.name] = $existingScope.id
                Write-Host "  Found existing scope (ID: $($existingScope.id))" -ForegroundColor Cyan
            }
        }
        catch {
            Write-Host "  Error: $_" -ForegroundColor Red
        }
    }
}

# Step 2: Create API Resources
Write-Host "`nStep 2: Creating API Resources..." -ForegroundColor Yellow

$apiResources = @(
    @{
        name = "company_api"
        displayName = "Company API"
        description = "Company management and data API"
        baseUrl = "https://api.company.com"
        scopeIds = @($createdScopeIds["api:company:read"], $createdScopeIds["api:company:write"])
    },
    @{
        name = "inventory_api"
        displayName = "Inventory API"
        description = "Inventory management and tracking API"
        baseUrl = "https://api.inventory.com"
        scopeIds = @($createdScopeIds["api:inventory:read"])
    }
)

$createdApiResourceIds = @{}

foreach ($resource in $apiResources) {
    Write-Host "  Creating API Resource: $($resource.name)..." -NoNewline
    
    $body = @{
        name = $resource.name
        displayName = $resource.displayName
        description = $resource.description
        baseUrl = $resource.baseUrl
        scopeIds = $resource.scopeIds | Where-Object { $_ }
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/admin/apiresources" `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -WebSession $session `
            -SkipCertificateCheck
        
        $createdApiResourceIds[$resource.name] = $response.id
        Write-Host " Created (ID: $($response.id))" -ForegroundColor Green
    }
    catch {
        Write-Host " Failed or already exists" -ForegroundColor Yellow
        Write-Host "  Error: $_" -ForegroundColor Red
    }
}

# Step 3: Verify the setup
Write-Host "`nStep 3: Verifying setup..." -ForegroundColor Yellow

try {
    $allResources = Invoke-RestMethod -Uri "$baseUrl/api/admin/apiresources?take=100" `
        -Method Get `
        -SkipCertificateCheck
    
    Write-Host "  Total API Resources: $($allResources.totalCount)" -ForegroundColor Cyan
    foreach ($res in $allResources.items) {
        Write-Host "    - $($res.name) ($($res.scopeCount) scopes)" -ForegroundColor White
    }
}
catch {
    Write-Host "  Failed to verify: $_" -ForegroundColor Red
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "You can now test the JWT aud claim with these scopes:" -ForegroundColor Cyan
Write-Host "  - api:company:read (audience: company_api)" -ForegroundColor White
Write-Host "  - api:company:write (audience: company_api)" -ForegroundColor White
Write-Host "  - api:inventory:read (audience: inventory_api)" -ForegroundColor White

# Step 4: Ensure TestClient has correct permissions (response_type:code and API scopes)
Write-Host "`nStep 4: Ensuring TestClient has canonical permissions via Admin API..." -ForegroundColor Yellow
try {
    $clients = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients?search=testclient-public&take=100" -WebSession $session -SkipCertificateCheck -ErrorAction Stop
    $client = $clients.items | Where-Object { $_.clientId -eq 'testclient-public' } | Select-Object -First 1
    if (-not $client) {
        Write-Host 'TestClient not found via Admin API — creating one now...' -ForegroundColor Yellow
        $createBody = @{ 
            ClientId = 'testclient-public';
            ClientSecret = $null;
            DisplayName = 'Test Client (Public)';
            ApplicationType = 'web';
            Type = 'public';
            ConsentType = 'explicit';
            RedirectUris = @('https://localhost:7001/signin-oidc');
            PostLogoutRedirectUris = @('https://localhost:7001/signout-callback-oidc');
            Permissions = @('ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','response_type:code','scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read')
        } | ConvertTo-Json -Depth 5
        $createResp = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients" -Method Post -Body $createBody -ContentType 'application/json' -WebSession $session -SkipCertificateCheck -ErrorAction Stop
        Write-Host "Created TestClient via Admin API (id: $($createResp.id))" -ForegroundColor Green
    } else {
        Write-Host "Found TestClient (id: $($client.id)) — updating permissions..." -ForegroundColor Cyan
        # Fetch full client object
        $clientDetails = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients/$($client.id)" -WebSession $session -SkipCertificateCheck -ErrorAction Stop
        $existingPerms = $clientDetails.permissions
        $canonical = @('ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','response_type:code','scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read')
        # Merge uniquely
        $merged = ($existingPerms + $canonical) | Select-Object -Unique
        $clientDetails.permissions = $merged
        # Update client using PUT
        $putBody = $clientDetails | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri "$baseUrl/api/admin/clients/$($client.id)" -Method Put -Body $putBody -ContentType 'application/json' -WebSession $session -SkipCertificateCheck -ErrorAction Stop
        Write-Host 'TestClient permissions updated via Admin API.' -ForegroundColor Green
    }
}
catch {
    Write-Host "Failed to ensure TestClient permissions via Admin API: $($_.Exception.Message)" -ForegroundColor Red
}
