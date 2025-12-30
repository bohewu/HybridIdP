<#
.SYNOPSIS
    Test LegacyAuth endpoint connectivity for HybridIdP.
    
.DESCRIPTION
    This script reads the deployment/.env file (if available) or prompts for
    LegacyAuth endpoint details, then tests HTTP connectivity to the endpoint.
    
.NOTES
    Run from deployment/ directory.
#>

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvPath = Join-Path $ScriptDir ".env"

function Write-Info { param($msg) Write-Host "[INFO] $msg" -ForegroundColor Green }
function Write-ErrorMsg { param($msg) Write-Host "[ERROR] $msg" -ForegroundColor Red }
function Write-Warn { param($msg) Write-Host "[WARN] $msg" -ForegroundColor Yellow }

Write-Host "HybridIdP Legacy Auth Endpoint Tester" -ForegroundColor Cyan
Write-Host "======================================="

$loginUrl = ""

# 1. Try to read from .env
if (Test-Path $EnvPath) {
    Write-Info "Found .env file at: $EnvPath"
    $envLines = Get-Content $EnvPath
    
    foreach ($line in $envLines) {
        if ($line -match "LegacyAuth__LoginUrl=['`"]?(.+?)['`"]?\s*$") {
            $loginUrl = $matches[1].Trim()
            break
        }
    }
} else {
    Write-Warn ".env file not found."
}

# 2. If not found, prompt user
if (-not $loginUrl) {
    Write-Warn "Could not find LegacyAuth__LoginUrl in .env."
    $loginUrl = Read-Host "Enter LegacyAuth Login URL (e.g. https://legacy-system.internal/api/authenticate/login)"
}

if (-not $loginUrl) {
    Write-ErrorMsg "No URL provided. Exiting."
    exit 1
}

Write-Info "Testing connectivity to: $loginUrl"

# Parse URL to get host and port for TCP test
try {
    $uri = [System.Uri]::new($loginUrl)
    $hostName = $uri.Host
    $port = if ($uri.Port -gt 0) { $uri.Port } else { if ($uri.Scheme -eq "https") { 443 } else { 80 } }
    
    Write-Info "Host: $hostName, Port: $port, Scheme: $($uri.Scheme)"
} catch {
    Write-ErrorMsg "Failed to parse URL: $loginUrl"
    Write-ErrorMsg $_.Exception.Message
    exit 1
}

# 3. Test TCP connectivity first
Write-Info "Step 1: Testing TCP connectivity to $hostName`:$port..."
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $connectTask = $tcp.ConnectAsync($hostName, $port)
    $completed = $connectTask.Wait(5000) # 5 second timeout
    
    if ($completed -and $tcp.Connected) {
        Write-Info "TCP connection successful."
        $tcp.Close()
    } else {
        Write-ErrorMsg "TCP connection failed (Timeout)."
        Write-Host "Suggestions:"
        Write-Host "  1. Check if the legacy system is running."
        Write-Host "  2. Verify firewall rules allow traffic on port $port."
        Write-Host "  3. Ensure DNS resolution is working for $hostName."
        exit 1
    }
} catch {
    Write-ErrorMsg "TCP connection failed: $($_.Exception.Message)"
    exit 1
}

# 4. Test HTTP connectivity (OPTIONS or HEAD request)
Write-Info "Step 2: Testing HTTP connectivity..."
try {
    # Use Invoke-WebRequest with Method HEAD or OPTIONS (less invasive)
    # Some servers may reject OPTIONS, so we try HEAD first
    $response = Invoke-WebRequest -Uri $loginUrl -Method HEAD -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
    Write-Info "HTTP HEAD request successful. Status: $($response.StatusCode)"
} catch {
    # HEAD might not be allowed, try a GET (likely to fail auth but that's OK)
    try {
        $response = Invoke-WebRequest -Uri $loginUrl -Method GET -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        Write-Info "HTTP GET request successful (unexpected for login endpoint). Status: $($response.StatusCode)"
    } catch [System.Net.WebException] {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 401 -or $statusCode -eq 403 -or $statusCode -eq 405) {
            Write-Info "HTTP endpoint reachable. Got expected status: $statusCode (Auth required or Method not allowed)"
        } elseif ($statusCode -eq 404) {
            Write-Warn "HTTP 404 - Endpoint not found. Check if the URL path is correct."
            exit 1
        } else {
            Write-Warn "HTTP request failed with status: $statusCode"
            Write-Host "This may still be OK if the endpoint requires POST with credentials."
        }
    } catch {
        Write-ErrorMsg "HTTP request failed: $($_.Exception.Message)"
        Write-Host "Suggestions:"
        Write-Host "  1. Check if the legacy system is responding to HTTP requests."
        Write-Host "  2. Verify SSL certificate is valid (if using HTTPS)."
        Write-Host "  3. Try accessing the URL in a browser or with curl."
        exit 1
    }
}

Write-Host ""
Write-Info "=== Legacy Auth Connectivity Test Complete ==="
Write-Info "The endpoint appears to be reachable."
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Ensure LegacyAuth__Secret is correctly configured."
Write-Host "  2. Test actual login with a legacy user account."
exit 0
