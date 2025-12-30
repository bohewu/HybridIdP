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

# 4. Test HTTP connectivity - Try /health endpoint first if available
Write-Info "Step 2: Testing HTTP connectivity..."

# Derive base URL and try /health endpoint
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"
$healthUrl = "$baseUrl/health"

Write-Info "Trying health endpoint: $healthUrl"
try {
    $healthResponse = Invoke-WebRequest -Uri $healthUrl -Method GET -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
    Write-Info "Health endpoint responded! Status: $($healthResponse.StatusCode)"
    Write-Host ""
    Write-Info "=== Legacy Auth Connectivity Test Complete ==="
    Write-Info "The legacy system is healthy and reachable."
    exit 0
} catch {
    $healthStatus = $_.Exception.Response.StatusCode.value__
    if ($healthStatus -eq 200) {
        Write-Info "Health endpoint healthy."
    } elseif ($healthStatus -eq 404) {
        Write-Warn "No /health endpoint found. Testing login URL directly..."
    } else {
        Write-Warn "Health endpoint returned: $healthStatus. Testing login URL directly..."
    }
}

# Fallback: Test the login URL directly
Write-Info "Testing login endpoint: $loginUrl"
try {
    $response = Invoke-WebRequest -Uri $loginUrl -Method HEAD -TimeoutSec 10 -UseBasicParsing -ErrorAction SilentlyContinue
    Write-Info "HTTP HEAD request successful. Status: $($response.StatusCode)"
} catch {
    try {
        $response = Invoke-WebRequest -Uri $loginUrl -Method GET -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        Write-Info "HTTP GET request successful. Status: $($response.StatusCode)"
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

# 5. Offer Docker network test
Write-Host "=== Docker Network Test (Recommended) ===" -ForegroundColor Yellow
Write-Host "Since the legacy system is typically only accessible from within Docker,"
Write-Host "we'll test using a curl container connected to the same network as idp-service."
Write-Host ""

# Detect container name and its networks
$idpContainer = docker ps --filter "name=idp-service" --format "{{.Names}}" 2>$null | Select-Object -First 1
$networkName = ""

if ($idpContainer) {
    # Get all networks the idp-service is connected to
    $detectedNetworks = docker inspect $idpContainer --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}' 2>$null
    $networkArray = ($detectedNetworks.Trim() -split ' ') | Where-Object { $_ -ne '' }
    
    if ($networkArray.Count -gt 0) {
        Write-Info "Detected idp-service container: $idpContainer"
        Write-Info "Connected networks: $($networkArray -join ', ')"
        
        if ($networkArray.Count -gt 1) {
            Write-Host ""
            Write-Host "Multiple networks detected. Choose one:"
            for ($i = 0; $i -lt $networkArray.Count; $i++) {
                Write-Host "  $($i + 1)) $($networkArray[$i])"
            }
            $netChoice = Read-Host "Enter choice [1-$($networkArray.Count)]"
            
            if ($netChoice -match '^\d+$' -and [int]$netChoice -ge 1 -and [int]$netChoice -le $networkArray.Count) {
                $networkName = $networkArray[[int]$netChoice - 1]
            } else {
                $networkName = $networkArray[0]
                Write-Warn "Invalid choice, using first network: $networkName"
            }
        } else {
            $networkName = $networkArray[0]
        }
    }
} else {
    Write-Warn "idp-service container not found. Will try to detect network from docker-compose files."
    
    # Fallback: Try to detect the network name from docker-compose
    if (Test-Path (Join-Path $ScriptDir "docker-compose.splithost-nginx-nodb.yml")) {
        $networkName = "deployment_backend"
    } elseif (Test-Path (Join-Path $ScriptDir "docker-compose.nginx.yml")) {
        $networkName = "deployment_default"
    }
}

if (-not $networkName) {
    Write-Host "Could not auto-detect network."
    Write-Host "To find your network name, run: docker network ls"
    $networkName = Read-Host "Enter Docker network name"
}

if (-not $networkName) {
    Write-ErrorMsg "No network name provided. Cannot run Docker test."
} else {
    Write-Host ""
    Write-Host "Command to run:" -ForegroundColor Cyan
    Write-Host "  docker run --rm --network $networkName curlimages/curl -v $healthUrl"
    Write-Host ""
    $runTest = Read-Host "Run this test now? (Y/n)"
    
    if ($runTest -ne "n" -and $runTest -ne "N") {
        Write-Info "Running: docker run --rm --network $networkName curlimages/curl -v $healthUrl"
        docker run --rm --network $networkName curlimages/curl -v $healthUrl
    } else {
        Write-Info "Skipping Docker network test."
    }
}

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Ensure LegacyAuth__Secret is correctly configured."
Write-Host "  2. Test actual login with a legacy user account."
exit 0
