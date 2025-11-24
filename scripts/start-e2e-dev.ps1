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
    [string]$RepoRoot = (Split-Path -Parent $MyInvocation.MyCommand.Definition)
)

function Start-WindowedProcess([string]$workingDir, [string]$command, [string]$title) {
    Write-Host "Starting: $title in $workingDir" -ForegroundColor Cyan
    $escaped = $command -replace '"','`"'
    $args = "-NoExit -Command cd `"$workingDir`"; $escaped"

    # Use pwsh if available, fallback to powershell
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        Start-Process -FilePath $pwsh.Source -ArgumentList $args -WindowStyle Normal -WorkingDirectory $workingDir -PassThru | Out-Null
    } else {
        Start-Process -FilePath powershell -ArgumentList $args -WindowStyle Normal -WorkingDirectory $workingDir -PassThru | Out-Null
    }
}

$root = Get-Item -Path $RepoRoot | Resolve-Path -Relative:$false

# IdP
$idpDir = Join-Path $root 'Web.IdP'
$idpCmd = 'dotnet run --project .\Web.IdP\Web.IdP.csproj --launch-profile https'
Start-WindowedProcess -workingDir $root -command $idpCmd -title 'HybridIdP - IdP'

# TestClient
$testClientDir = Join-Path $root ''
$testClientCmd = 'dotnet run --project .\TestClient\TestClient.csproj --launch-profile https'
Start-WindowedProcess -workingDir $root -command $testClientCmd -title 'HybridIdP - TestClient'

if ($StartVite) {
    $viteDir = Join-Path $root 'Web.IdP/ClientApp'
    $viteCmd = 'npm run dev'
    Start-WindowedProcess -workingDir $viteDir -command $viteCmd -title 'HybridIdP - ClientApp (Vite)'
}

Write-Host "Launched processes. Keep these windows open while running e2e tests." -ForegroundColor Green
