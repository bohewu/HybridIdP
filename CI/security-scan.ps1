<#
.SYNOPSIS
    OWASP ZAP Security Scanner for HybridIdP
    
.DESCRIPTION
    Runs OWASP ZAP vulnerability scan against a target URL using Docker.
    Supports baseline (passive) and full (active) scan modes.
    
.PARAMETER ScanType
    Type of scan to perform: "baseline" (fast, passive) or "full" (thorough, active)
    Default: baseline
    
.PARAMETER ReportFormat
    Output report format: "html", "json", or "both"
    Default: html
    
.PARAMETER TargetUrl
    The URL to scan. Default: http://host.docker.internal:8080
    
.PARAMETER OutputDir
    Directory for scan reports. Default: ./security-reports
    
.PARAMETER KeepContainer
    If set, don't remove the ZAP container after scan (useful for debugging)
    
.EXAMPLE
    # Quick baseline scan with HTML report
    .\security-scan.ps1
    
.EXAMPLE
    # Full scan with both HTML and JSON reports
    .\security-scan.ps1 -ScanType full -ReportFormat both
    
.EXAMPLE
    # Scan a specific target
    .\security-scan.ps1 -TargetUrl http://host.docker.internal:5000 -ScanType baseline
    
.NOTES
    Prerequisites:
    - Docker must be installed and running
    - Target application must be running (e.g., via docker-compose)
    
    For docker-compose environment:
    1. Start services: docker compose -f docker-compose.dev.yml up -d
    2. Run scan: .\security-scan.ps1
#>

[CmdletBinding()]
param(
    [ValidateSet("baseline", "full")]
    [string]$ScanType = "baseline",
    
    [ValidateSet("html", "json", "both")]
    [string]$ReportFormat = "html",
    
    [string]$TargetUrl = "http://host.docker.internal:8080",
    
    [string]$OutputDir = "./security-reports",
    
    [switch]$KeepContainer,
    
    [switch]$Help
)

# Colors for output
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }

# Show help
if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

$ErrorActionPreference = "Stop"

# Configuration
$ZapImage = "zaproxy/zap-stable"
$ContainerName = "zap-security-scan-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  OWASP ZAP Security Scanner" -ForegroundColor Magenta
Write-Host "  HybridIdP SSDLC Pipeline" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Step 1: Check Docker
Write-Info "Checking Docker availability..."
try {
    $dockerVersion = docker --version
    Write-Success "Docker found: $dockerVersion"
} catch {
    Write-Error "Docker is not installed or not running. Please install Docker Desktop."
    exit 1
}

# Step 2: Create output directory
Write-Info "Creating output directory: $OutputDir"
$OutputPath = New-Item -ItemType Directory -Path $OutputDir -Force
$AbsoluteOutputPath = $OutputPath.FullName
Write-Success "Reports will be saved to: $AbsoluteOutputPath"

# Step 3: Pull ZAP image
Write-Info "Pulling ZAP Docker image: $ZapImage"
docker pull $ZapImage
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to pull ZAP image"
    exit 1
}
Write-Success "ZAP image ready"

# Step 4: Determine scan script and build command
$ReportFiles = @()
$ZapArgs = @()

switch ($ScanType) {
    "baseline" {
        $ScanScript = "zap-baseline.py"
        Write-Info "Scan Type: BASELINE (passive scan, ~1-2 minutes)"
    }
    "full" {
        $ScanScript = "zap-full-scan.py"
        Write-Info "Scan Type: FULL (active scan, ~10-30 minutes)"
        Write-Warning "Full scan performs active attacks - only use on systems you own!"
    }
}

# Build report arguments
switch ($ReportFormat) {
    "html" {
        $ReportFiles += "zap-report-$Timestamp.html"
        $ZapArgs += "-r", "zap-report-$Timestamp.html"
    }
    "json" {
        $ReportFiles += "zap-report-$Timestamp.json"
        $ZapArgs += "-J", "zap-report-$Timestamp.json"
    }
    "both" {
        $ReportFiles += "zap-report-$Timestamp.html"
        $ReportFiles += "zap-report-$Timestamp.json"
        $ZapArgs += "-r", "zap-report-$Timestamp.html"
        $ZapArgs += "-J", "zap-report-$Timestamp.json"
    }
}

Write-Info "Target URL: $TargetUrl"
Write-Info "Report Format: $ReportFormat"

# Step 5: Run ZAP scan
Write-Host ""
Write-Host "Starting security scan..." -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow

$dockerRunArgs = @(
    "run"
    "--name", $ContainerName
    "--rm"
    "-v", "${AbsoluteOutputPath}:/zap/wrk:rw"
    "-t", $ZapImage
    $ScanScript
    "-t", $TargetUrl
    "-I"  # Don't fail on warnings
) + $ZapArgs

Write-Info "Docker command: docker $($dockerRunArgs -join ' ')"
Write-Host ""

$scanStartTime = Get-Date
docker @dockerRunArgs
$ScanExitCode = $LASTEXITCODE
$scanEndTime = Get-Date
$scanDuration = $scanEndTime - $scanStartTime

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow

# Step 6: Display results
Write-Host ""
Write-Host "Scan completed in $($scanDuration.TotalMinutes.ToString('F1')) minutes" -ForegroundColor Cyan
Write-Host ""

# ZAP exit codes: 0 = pass, 1 = warnings, 2 = fail
switch ($ScanExitCode) {
    0 {
        Write-Success "SCAN PASSED - No vulnerabilities found!"
    }
    1 {
        Write-Warning "SCAN COMPLETED WITH WARNINGS - Review the report for details"
    }
    2 {
        Write-Error "SCAN FAILED - Critical vulnerabilities detected!"
    }
    default {
        Write-Error "SCAN ERROR - Exit code: $ScanExitCode"
    }
}

# List generated reports
Write-Host ""
Write-Info "Generated Reports:"
foreach ($report in $ReportFiles) {
    $reportPath = Join-Path $AbsoluteOutputPath $report
    if (Test-Path $reportPath) {
        $fileSize = (Get-Item $reportPath).Length / 1KB
        Write-Host "  📄 $reportPath ($($fileSize.ToString('F1')) KB)" -ForegroundColor White
    }
}

# Step 7: Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Scan Summary" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Target:      $TargetUrl"
Write-Host "  Scan Type:   $ScanType"
Write-Host "  Duration:    $($scanDuration.TotalMinutes.ToString('F1')) minutes"
Write-Host "  Reports:     $AbsoluteOutputPath"
Write-Host "  Exit Code:   $ScanExitCode"
Write-Host ""

# Return exit code for CI integration
exit $ScanExitCode
