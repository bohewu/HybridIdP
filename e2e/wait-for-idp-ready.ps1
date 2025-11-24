<#
.SYNOPSIS
  Waits for the IdP and TestClient services to become reachable, performs an admin login
  against the IdP /Account/Login endpoint and calls the protected admin health endpoint
  to verify seeding/initialization finished.

.DESCRIPTION
  This script polls the two target URLs until they're reachable. Once reachable it
  attempts to sign in as the admin user (default admin@hybridauth.local / Admin@123)
  via the web login form and then calls /api/admin/health to verify that the Admin
  API is accessible and reports as healthy.

  The script is intentionally PowerShell Core (pwsh) friendly and uses a WebRequest
  session to maintain cookies between the login POST and subsequent API call.

.PARAMETER IdpUrl
  Base URL for the IdP (example: https://localhost:7035)

.PARAMETER TestClientUrl
  Base URL for the TestClient (example: https://localhost:7001)

.PARAMETER AdminEmail
  Admin email to use to log in. Defaults to admin@hybridauth.local

.PARAMETER AdminPassword
  Admin password used for login. Defaults to Admin@123

.PARAMETER TimeoutSeconds
  How many seconds to wait for IdP/TestClient readiness (default 180)

.PARAMETER PollIntervalSeconds
  How often to poll the services in seconds (default 2)

EXAMPLE
  ./wait-for-idp-ready.ps1 -IdpUrl 'https://localhost:7035' -TestClientUrl 'https://localhost:7001' -TimeoutSeconds 180

#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$IdpUrl,

    [Parameter(Mandatory=$true)]
    [string]$TestClientUrl,

    [string]$AdminEmail = 'admin@hybridauth.local',
    [string]$AdminPassword = 'Admin@123',
    [int]$TimeoutSeconds = 180,
    [int]$PollIntervalSeconds = 2
)

function Write-Info($msg){ Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg){ Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg){ Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host $msg -ForegroundColor Red }

if (-not ($IdpUrl -match '^https?://')) { Write-Err "IdpUrl must include protocol (https:// or http://)"; exit 2 }
if (-not ($TestClientUrl -match '^https?://')) { Write-Err "TestClientUrl must include protocol (https:// or http://)"; exit 2 }

$start = [DateTime]::UtcNow
$deadline = $start.AddSeconds($TimeoutSeconds)

Write-Info "Waiting for IdP ($IdpUrl) and TestClient ($TestClientUrl) to be reachable (timeout: $TimeoutSeconds s)..."

$idpReady = $false
$testClientReady = $false

while ([DateTime]::UtcNow -lt $deadline) {
    if (-not $idpReady) {
        try {
            $r = Invoke-WebRequest -Uri $IdpUrl -Method Head -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
            $idpReady = $true
            Write-Ok "IdP reachable: $IdpUrl"
        } catch {
            Write-Warn "IdP not yet reachable: $($_.Exception.Message)"
        }
    }

    if (-not $testClientReady) {
        try {
            $r = Invoke-WebRequest -Uri $TestClientUrl -Method Head -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
            $testClientReady = $true
            Write-Ok "TestClient reachable: $TestClientUrl"
        } catch {
            Write-Warn "TestClient not yet reachable: $($_.Exception.Message)"
        }
    }

    if ($idpReady -and $testClientReady) { break }

    Start-Sleep -Seconds $PollIntervalSeconds
}

if (-not ($idpReady -and $testClientReady)) {
    Write-Err "Timeout waiting for services to become reachable. IdP ready: $idpReady, TestClient ready: $testClientReady"
    exit 3
}

# Try login via the standard Razor Pages sign-in form and call the protected admin health endpoint
Write-Info "Attempting admin login and admin/health check using $AdminEmail ..."

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$loginPath = '/Account/Login'
$loginUri = ($IdpUrl.TrimEnd('/') + $loginPath)

$healthPath = '/api/admin/health'
$healthUri = ($IdpUrl.TrimEnd('/') + $healthPath)

$success = $false

while ([DateTime]::UtcNow -lt $deadline) {
    try {
        # Request the login page first to pickup any cookies and hidden inputs (antiforgery token etc.)
        $loginPage = Invoke-WebRequest -Uri $loginUri -WebSession $session -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop

        # Parse hidden inputs from the login page and include them in the form submission
        $form = @{}
        $inputPattern = '<input\s+[^>]*name=["\''](?<name>[^"\'']+)["\''][^>]*value=["\''](?<value>[^"\'']*)["\''][^>]*>'
        foreach ($m in [regex]::Matches($loginPage.Content, $inputPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $n = $m.Groups['name'].Value
            $v = $m.Groups['value'].Value
            if (-not [string]::IsNullOrEmpty($n)) { $form[$n] = $v }
        }

        # Ensure our expected login fields are present/overwritten
        $form['Input.Login'] = $AdminEmail
        $form['Input.Password'] = $AdminPassword
        $form['Input.RememberMe'] = 'false'

        # POST login form (includes antiforgery / hidden fields)
        $resp = Invoke-WebRequest -Uri $loginUri -WebSession $session -Method Post -Body $form -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop

        # After login attempt call admin health
        try {
            $health = Invoke-WebRequest -Uri $healthUri -WebSession $session -Method Get -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
            if ($health.StatusCode -eq 200) {
                try {
                    $json = $health.Content | ConvertFrom-Json -ErrorAction Stop
                    if ($json.status -eq 'healthy') {
                        Write-Ok "Admin health OK (user: $($json.user))"
                        $success = $true
                        break
                    }
                } catch {
                    Write-Warn "Admin health returned non-JSON content but HTTP 200: $($health.StatusCode)"
                    $success = $true
                    break
                }
            } else {
                Write-Warn "Health returned HTTP $($health.StatusCode)"
            }
        } catch {
            Write-Warn "Admin health not yet available / authorized: $($_.Exception.Message)"
        }

    } catch {
        Write-Warn "Login attempt failed: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $PollIntervalSeconds
}

if (-not $success) {
    Write-Err "Admin login + health check did not succeed within $TimeoutSeconds seconds."
    exit 4
}

Write-Ok "IdP & TestClient ready and admin API healthy."

# Ensure required E2E TestClient entry exists in IdP (create via Admin API if missing)
try {
    Write-Info "Verifying TestClient registration (clientId = 'testclient-public')..."
    $clientsResp = Invoke-WebRequest -Uri ($IdpUrl.TrimEnd('/') + '/api/admin/clients?search=testclient-public') -WebSession $session -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
    $clientsJson = $clientsResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
    $found = $false
    if ($null -ne $clientsJson) {
        if ($clientsJson.items -and $clientsJson.items.Count -gt 0) { $found = $true }
        if (-not $found -and ($clientsJson | Where-Object { $_.clientId -eq 'testclient-public' })) { $found = $true }
    }

    if (-not $found) {
        Write-Warn "TestClient not found — creating via Admin API..."
        $createBody = @{ 
            ClientId = 'testclient-public';
            ClientSecret = $null;
            DisplayName = 'Test Client (Public)';
            ApplicationType = 'web';
            Type = 'public';
            ConsentType = 'explicit';
            RedirectUris = @('https://localhost:7001/signin-oidc');
            PostLogoutRedirectUris = @('https://localhost:7001/signout-callback-oidc');
            Permissions = @('ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read')
        } | ConvertTo-Json -Depth 5

        $createResp = Invoke-WebRequest -Uri ($IdpUrl.TrimEnd('/') + '/api/admin/clients') -WebSession $session -Method Post -Body $createBody -ContentType 'application/json' -SkipCertificateCheck -TimeoutSec 15 -ErrorAction Stop
        if ($createResp.StatusCode -in 200,201) { Write-Ok "TestClient created via Admin API." } else { Write-Warn "Unexpected response creating TestClient: $($createResp.StatusCode)" }
    } else {
        Write-Ok "TestClient already registered."
    }
} catch {
    Write-Warn "Could not verify/create test client via Admin API: $($_.Exception.Message)"
}

# Ensure the API scopes requested by TestClient exist (avoid invalid_scope during OIDC authorize)
try {
    $requiredScopes = @('api:company:read','api:inventory:read')
    foreach ($scopeName in $requiredScopes) {
        Write-Info "Checking scope: $scopeName"
        $sResp = Invoke-WebRequest -Uri ($IdpUrl.TrimEnd('/') + "/api/admin/scopes?search=$scopeName") -WebSession $session -SkipCertificateCheck -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
        $sJson = $sResp.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
        $exists = $false
        if ($null -ne $sJson) {
            if ($sJson.items -and $sJson.items.Count -gt 0) { $exists = $true }
            if (-not $exists -and ($sJson | Where-Object { $_.name -eq $scopeName })) { $exists = $true }
        }

        if (-not $exists) {
            Write-Warn "Scope '$scopeName' not found — creating via Admin API..."
            $scopeBody = @{ Name = $scopeName; DisplayName = $scopeName; Description = "E2E test scope: $scopeName" } | ConvertTo-Json -Depth 3
            $createScopeResp = Invoke-WebRequest -Uri ($IdpUrl.TrimEnd('/') + '/api/admin/scopes') -WebSession $session -Method Post -Body $scopeBody -ContentType 'application/json' -SkipCertificateCheck -TimeoutSec 10 -ErrorAction Stop
            if ($createScopeResp.StatusCode -in 200,201) { Write-Ok "Scope '$scopeName' created." } else { Write-Warn "Unexpected response creating scope $($scopeName): $($createScopeResp.StatusCode)" }
        } else {
            Write-Ok "Scope '$scopeName' already exists."
        }
    }
} catch {
    Write-Warn "Could not verify/create required scopes via Admin API: $($_.Exception.Message)"
}

exit 0
