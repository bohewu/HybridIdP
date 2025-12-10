<#
.SYNOPSIS
    NPM Audit Scanner for HybridIdP Frontend (Read-Only)
    
.DESCRIPTION
    Scans npm dependencies for known vulnerabilities WITHOUT making any changes.
    This is a SAFE read-only operation that will not modify package.json or package-lock.json.
    
.PARAMETER ClientAppPath
    Path to the ClientApp folder. Default: ./Web.IdP/ClientApp
    
.PARAMETER OutputDir
    Directory for scan reports. Default: ./security-reports
    
.PARAMETER Format
    Output format: "console" (default), "json", or "both"
    
.PARAMETER ProductionOnly
    Only audit production dependencies (exclude devDependencies)
    
.EXAMPLE
    # Quick audit with console output
    .\npm-audit.ps1
    
.EXAMPLE
    # Full audit with JSON report
    .\npm-audit.ps1 -Format both
    
.EXAMPLE
    # Production dependencies only
    .\npm-audit.ps1 -ProductionOnly
    
.NOTES
    This script is READ-ONLY and will NEVER run 'npm audit fix'.
    All dependency updates should be done manually after careful review.
#>

[CmdletBinding()]
param(
    [string]$ClientAppPath = "./Web.IdP/ClientApp",
    
    [string]$OutputDir = "./security-reports",
    
    [ValidateSet("console", "json", "both")]
    [string]$Format = "console",
    
    [switch]$ProductionOnly,
    
    [switch]$Help
)

# Colors for output
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }

if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

$ErrorActionPreference = "Stop"
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  NPM Dependency Audit (Read-Only)" -ForegroundColor Magenta
Write-Host "  HybridIdP SSDLC Pipeline" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  ⚠️  This scan is READ-ONLY" -ForegroundColor Yellow
Write-Host "  ⚠️  No packages will be modified" -ForegroundColor Yellow
Write-Host ""

# Step 1: Check npm
Write-Info "Checking npm..."
try {
    $npmVersion = npm --version
    Write-Success "npm found: v$npmVersion"
} catch {
    Write-Error "npm is not installed. Please install Node.js."
    exit 1
}

# Step 2: Verify ClientApp path
if (-not (Test-Path (Join-Path $ClientAppPath "package.json"))) {
    Write-Error "package.json not found at: $ClientAppPath"
    exit 1
}
Write-Info "ClientApp: $ClientAppPath"

# Step 3: Create output directory
if ($Format -ne "console") {
    $OutputPath = New-Item -ItemType Directory -Path $OutputDir -Force
    Write-Info "Reports will be saved to: $($OutputPath.FullName)"
}

# Step 4: Build arguments
$npmArgs = @("audit")

if ($ProductionOnly) {
    $npmArgs += "--omit=dev"
    Write-Info "Scanning production dependencies only"
} else {
    Write-Info "Scanning all dependencies (including dev)"
}

# Step 5: Run audit
Write-Host ""
Write-Host "Scanning for vulnerable packages..." -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host ""

$scanStartTime = Get-Date

Push-Location $ClientAppPath
try {
    if ($Format -eq "console") {
        npm @npmArgs
        $exitCode = $LASTEXITCODE
    } elseif ($Format -eq "json") {
        $jsonOutput = npm @npmArgs --json 2>&1
        $exitCode = $LASTEXITCODE
        
        $jsonFile = Join-Path $OutputDir "npm-audit-$Timestamp.json"
        $jsonOutput | Out-File -FilePath (Join-Path "..\..\.." $jsonFile) -Encoding UTF8
        Write-Info "JSON report saved: $jsonFile"
    } else {
        npm @npmArgs
        $consoleExitCode = $LASTEXITCODE
        
        $jsonOutput = npm @npmArgs --json 2>&1
        $exitCode = $consoleExitCode
        
        $jsonFile = Join-Path $OutputDir "npm-audit-$Timestamp.json"
        $jsonOutput | Out-File -FilePath (Join-Path "..\..\.." $jsonFile) -Encoding UTF8
        Write-Info "JSON report saved: $jsonFile"
    }
} finally {
    Pop-Location
}

$scanEndTime = Get-Date
$scanDuration = $scanEndTime - $scanStartTime

Write-Host ""
Write-Host "============================================" -ForegroundColor Yellow

# Step 6: Display summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Scan Summary" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Path:        $ClientAppPath"
Write-Host "  Duration:    $($scanDuration.TotalSeconds.ToString('F1')) seconds"
Write-Host "  Prod Only:   $(if ($ProductionOnly) { 'Yes' } else { 'No' })"

# npm audit exit codes: 0 = no vulns, non-zero = vulns found
if ($exitCode -eq 0) {
    Write-Host ""
    Write-Success "NO VULNERABILITIES FOUND!"
} else {
    Write-Host ""
    Write-Warning "Vulnerabilities detected - review the output above"
    Write-Host ""
    Write-Host "  💡 To investigate:" -ForegroundColor Cyan
    Write-Host "     cd $ClientAppPath" -ForegroundColor White
    Write-Host "     npm audit" -ForegroundColor White
    Write-Host ""
    Write-Host "  ⚠️  DO NOT run 'npm audit fix' without careful review!" -ForegroundColor Yellow
}

Write-Host ""
exit $exitCode
