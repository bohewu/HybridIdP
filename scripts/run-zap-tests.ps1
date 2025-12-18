#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run OWASP ZAP authenticated security tests against HybridIdP
.DESCRIPTION
    This script starts ZAP daemon and runs security tests.
    Supports two modes: Native Windows ZAP or Docker (via WSL).
.EXAMPLE
    .\run-zap-tests.ps1 -Native
.EXAMPLE
    .\run-zap-tests.ps1 -Docker
#>

param(
    [switch]$Native,
    [switch]$Docker,
    [switch]$SkipCleanup,
    [string]$ZapPath = "C:\Program Files\ZAP\Zed Attack Proxy\zap.bat",
    [string]$WslDistro = "Ubuntu-24.04",
    [int]$ZapPort = 8090,
    [int]$IdpPort = 7035
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " OWASP ZAP Security Test Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Auto-detect mode if not specified
if (-not $Native -and -not $Docker) {
    if (Test-Path $ZapPath) {
        Write-Host " Mode: Native Windows ZAP (auto-detected)" -ForegroundColor Green
        $Native = $true
    } else {
        Write-Host " Mode: Docker via WSL (auto-detected)" -ForegroundColor Green
        $Docker = $true
    }
}

Write-Host ""

# =====================
# NATIVE WINDOWS MODE
# =====================
if ($Native) {
    Write-Host "[1/5] Checking prerequisites..." -ForegroundColor Yellow
    
    # Check ZAP installation
    if (-not (Test-Path $ZapPath)) {
        Write-Host ""
        Write-Host "ZAP not found at: $ZapPath" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please install ZAP:" -ForegroundColor Yellow
        Write-Host "  1. Download from: https://www.zaproxy.org/download/" -ForegroundColor White
        Write-Host "  2. Install to default location" -ForegroundColor White
        Write-Host "  3. Or specify path: .\run-zap-tests.ps1 -Native -ZapPath 'C:\path\to\zap.bat'" -ForegroundColor White
        exit 1
    }
    Write-Host "  ✓ ZAP found: $ZapPath" -ForegroundColor Green

    # Check IdP
    try {
        Invoke-RestMethod -Uri "https://localhost:$IdpPort/health" -SkipCertificateCheck -TimeoutSec 3 | Out-Null
        Write-Host "  ✓ IdP server running" -ForegroundColor Green
    } catch {
        Write-Error "IdP server not running. Start with: dotnet run --launch-profile https"
    }

    # Start ZAP daemon
    Write-Host "[2/5] Starting ZAP daemon..." -ForegroundColor Yellow
    $zapProcess = Start-Process -FilePath $ZapPath -ArgumentList "-daemon", "-host", "127.0.0.1", "-port", $ZapPort, "-config", "api.disablekey=true" -PassThru -WindowStyle Hidden
    Write-Host "  ✓ ZAP process started (PID: $($zapProcess.Id))" -ForegroundColor Green

    # Wait for ZAP
    Write-Host "[3/5] Waiting for ZAP to initialize..." -ForegroundColor Yellow
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        try {
            $result = Invoke-RestMethod -Uri "http://localhost:$ZapPort/JSON/core/view/version/" -TimeoutSec 2
            $ready = $true
            Write-Host "  ✓ ZAP $($result.version) is ready" -ForegroundColor Green
            break
        } catch {
            Write-Host "`r  Waiting... ($i/60)" -NoNewline
        }
    }
    
    if (-not $ready) {
        Write-Error "ZAP failed to start"
    }

    # Run tests
    Write-Host ""
    Write-Host "[4/5] Running security tests..." -ForegroundColor Yellow
    $env:ZAP_URL = "http://localhost:$ZapPort"
    dotnet test Tests.SystemTests --filter "FullyQualifiedName~ZapSecurityTests" --logger "console;verbosity=normal"
    $testExitCode = $LASTEXITCODE

    # Cleanup
    if (-not $SkipCleanup) {
        Write-Host ""
        Write-Host "[5/5] Stopping ZAP..." -ForegroundColor Yellow
        Stop-Process -Id $zapProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ ZAP stopped" -ForegroundColor Green
    }
}

# =====================
# DOCKER/WSL MODE  
# =====================
if ($Docker) {
    Write-Host "[1/5] Checking WSL..." -ForegroundColor Yellow
    $wslDistros = wsl --list --quiet 2>$null
    if (-not $wslDistros.Contains($WslDistro)) {
        Write-Error "WSL distro '$WslDistro' not found"
    }
    Write-Host "  ✓ WSL: $WslDistro" -ForegroundColor Green

    # Start ZAP in Docker
    Write-Host "[2/5] Starting ZAP in Docker..." -ForegroundColor Yellow
    wsl -d $WslDistro -- bash -c "docker stop zap-test 2>/dev/null; docker rm zap-test 2>/dev/null" | Out-Null
    wsl -d $WslDistro -- bash -c "docker run -d -p ${ZapPort}:8080 --name zap-test zaproxy/zap-stable zap.sh -daemon -host 0.0.0.0 -port 8080 -config api.disablekey=true"
    
    # Wait
    Write-Host "[3/5] Waiting for ZAP..." -ForegroundColor Yellow
    Start-Sleep -Seconds 20
    $result = wsl -d $WslDistro -- bash -c "curl -s http://localhost:$ZapPort/JSON/core/view/version/"
    Write-Host "  ✓ ZAP ready: $result" -ForegroundColor Green

    # Run tests from WSL
    Write-Host "[4/5] Running tests from WSL..." -ForegroundColor Yellow
    $repoPath = (Get-Location).Path -replace '\\', '/' -replace '^C:', '/mnt/c'
    wsl -d $WslDistro -- bash -c "cd $repoPath && ZAP_URL=http://localhost:$ZapPort dotnet test Tests.SystemTests --filter 'FullyQualifiedName~ZapSecurityTests'"
    $testExitCode = $LASTEXITCODE

    # Cleanup
    if (-not $SkipCleanup) {
        Write-Host "[5/5] Cleanup..." -ForegroundColor Yellow
        wsl -d $WslDistro -- bash -c "docker stop zap-test; docker rm zap-test" | Out-Null
    }
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($testExitCode -eq 0) {
    Write-Host " ✓ Security tests PASSED" -ForegroundColor Green
} else {
    Write-Host " ✗ Security tests FAILED (exit: $testExitCode)" -ForegroundColor Red
}
Write-Host "========================================" -ForegroundColor Cyan

exit $testExitCode

