# Setup Test API Resources and Scopes via Admin API
# This script creates API resources and scopes, then tests the JWT aud claim

$baseUrl = "https://localhost:7035"
$adminEmail = "admin@hybridauth.local"
$adminPassword = "Admin@123"

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
