<#
Opens separate PowerShell sessions to start Web.IdP and TestClient and optionally Vite.

Usage examples:
  # Start IdP and TestClient in two new terminals
  ./scripts/start-e2e-dev.ps1

  # Start IdP, TestClient and the Vite client dev server
  ./scripts/start-e2e-dev.ps1 -StartVite

Note: This script uses Start-Process to open new pwsh/windows and leaves them running (-NoExit).
#>

[CmdletBinding()]
param(
    [switch]$StartVite,
    [switch]$InheritEnv,
    [switch]$WebOnly,
    [string]$RepoRoot = $null
)

function Start-WindowedProcess([string]$workingDir, [string]$command, [string]$title, [string]$envPrep = '') {
    Write-Host "Starting: $title in $workingDir" -ForegroundColor Cyan
    $escaped = $command -replace '"','`"'
    # Build pwsh argument string and include any environment assignments (envPrep) before changing directory
    $pwshArgs = "-NoExit -Command $envPrep cd `"$workingDir`"; $escaped"

    # Use pwsh if available, fallback to powershell
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        Start-Process -FilePath $pwsh.Source -ArgumentList $pwshArgs -WindowStyle Normal -WorkingDirectory $workingDir -PassThru | Out-Null
    } else {
        Start-Process -FilePath powershell -ArgumentList $pwshArgs -WindowStyle Normal -WorkingDirectory $workingDir -PassThru | Out-Null
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $RepoRoot) {
    # default to the repository root (parent of the scripts directory)
    $RepoRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path
}
$root = Get-Item -Path $RepoRoot | Resolve-Path -Relative:$false

# Build environment assignments string if requested. This ensures new windows will be pre-populated with
# DATABASE_PROVIDER and any ConnectionStrings__* variables the parent has set.
$inheritPrep = ''
if ($InheritEnv) {
    $assignments = @()
    if ($env:DATABASE_PROVIDER) {
        $val = $env:DATABASE_PROVIDER -replace "'", "''"
        $assignments += "`$env:DATABASE_PROVIDER = '$val';"
    }
    # include connection strings (e.g. ConnectionStrings__PostgreSqlConnection)
    Get-ChildItem Env: | Where-Object { $_.Name -like 'ConnectionStrings__*' } | ForEach-Object {
        $n = $_.Name
        $v = $_.Value -replace "'", "''"
        $assignments += "`$env:$n = '$v';"
    }
    if ($assignments.Count -gt 0) { $inheritPrep = ($assignments -join ' ') }
}

# IdP
$idpDir = Join-Path $root 'Web.IdP'
$idpCmd = 'dotnet run --launch-profile https'
Start-WindowedProcess -workingDir $idpDir -command $idpCmd -title 'HybridIdP - IdP' -envPrep $inheritPrep

# TestClient (only start when not running in WebOnly mode)
if (-not $WebOnly) {
    $testClientDir = Join-Path $root 'TestClient'
    $testClientCmd = 'dotnet run --launch-profile https'
    Start-WindowedProcess -workingDir $testClientDir -command $testClientCmd -title 'HybridIdP - TestClient' -envPrep $inheritPrep
} else {
    Write-Host 'WebOnly requested – skipping TestClient startup.' -ForegroundColor Yellow
}

if ($StartVite) {
    $viteDir = Join-Path $root 'Web.IdP/ClientApp'
    $viteCmd = 'npm run dev'
    Start-WindowedProcess -workingDir $viteDir -command $viteCmd -title 'HybridIdP - ClientApp (Vite)' -envPrep $inheritPrep
}

Write-Host "Launched processes. Keep these windows open while running e2e tests." -ForegroundColor Green
