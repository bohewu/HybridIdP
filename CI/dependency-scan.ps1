<#
.SYNOPSIS
    .NET Dependency Vulnerability Scanner for HybridIdP
    
.DESCRIPTION
    Scans all .NET projects for known vulnerable NuGet packages.
    Uses built-in 'dotnet list package --vulnerable' command.
    
.PARAMETER OutputDir
    Directory for scan reports. Default: ./security-reports
    
.PARAMETER IncludeTransitive
    Include transitive (indirect) dependencies in scan
    
.PARAMETER Format
    Output format: "console" (default), "json", or "both"
    
.EXAMPLE
    # Quick scan with console output
    .\dependency-scan.ps1
    
.EXAMPLE
    # Full scan including transitive deps, with JSON report
    .\dependency-scan.ps1 -IncludeTransitive -Format both
    
.NOTES
    Prerequisites:
    - .NET SDK 8.0 or higher
    - NuGet vulnerability database (automatically downloaded)
#>

[CmdletBinding()]
param(
    [string]$OutputDir = "./security-reports",
    
    [switch]$IncludeTransitive,
    
    [ValidateSet("console", "json", "both")]
    [string]$Format = "console",
    
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
Write-Host "  .NET Dependency Vulnerability Scan" -ForegroundColor Magenta
Write-Host "  HybridIdP SSDLC Pipeline" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Step 1: Check .NET SDK
Write-Info "Checking .NET SDK..."
try {
    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK found: $dotnetVersion"
} catch {
    Write-Error ".NET SDK is not installed. Please install .NET SDK 8.0 or higher."
    exit 1
}

# Step 2: Find solution file
$solutionFile = Get-ChildItem -Path "." -Filter "*.sln" | Select-Object -First 1
if (-not $solutionFile) {
    Write-Error "No .sln file found in current directory"
    exit 1
}
Write-Info "Solution: $($solutionFile.Name)"

# Step 3: Create output directory
if ($Format -ne "console") {
    $OutputPath = New-Item -ItemType Directory -Path $OutputDir -Force
    Write-Info "Reports will be saved to: $($OutputPath.FullName)"
}

# Step 4: Build arguments
$dotnetArgs = @("list", $solutionFile.Name, "package", "--vulnerable")

if ($IncludeTransitive) {
    $dotnetArgs += "--include-transitive"
    Write-Info "Including transitive dependencies"
}

# Step 5: Run vulnerability scan
Write-Host ""
Write-Host "Scanning for vulnerable packages..." -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host ""

$scanStartTime = Get-Date

if ($Format -eq "console") {
    # Console only
    dotnet @dotnetArgs
    $exitCode = $LASTEXITCODE
} elseif ($Format -eq "json") {
    # JSON only
    $jsonArgs = $dotnetArgs + @("--format", "json")
    $jsonOutput = dotnet @jsonArgs
    $exitCode = $LASTEXITCODE
    
    $jsonFile = Join-Path $OutputDir "dependency-scan-$Timestamp.json"
    $jsonOutput | Out-File -FilePath $jsonFile -Encoding UTF8
    Write-Info "JSON report saved: $jsonFile"
} else {
    # Both
    dotnet @dotnetArgs
    $exitCode = $LASTEXITCODE
    
    $jsonArgs = $dotnetArgs + @("--format", "json")
    $jsonOutput = dotnet @jsonArgs
    
    $jsonFile = Join-Path $OutputDir "dependency-scan-$Timestamp.json"
    $jsonOutput | Out-File -FilePath $jsonFile -Encoding UTF8
    Write-Info "JSON report saved: $jsonFile"
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
Write-Host "  Solution:    $($solutionFile.Name)"
Write-Host "  Duration:    $($scanDuration.TotalSeconds.ToString('F1')) seconds"
Write-Host "  Transitive:  $(if ($IncludeTransitive) { 'Yes' } else { 'No' })"

if ($exitCode -eq 0) {
    Write-Host ""
    Write-Success "NO VULNERABLE PACKAGES FOUND!"
} else {
    Write-Host ""
    Write-Warning "Vulnerable packages detected - review the output above"
}

Write-Host ""
exit $exitCode
