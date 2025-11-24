<#
PowerShell helper to prepare and run the e2e Playwright tests.

Features:
- Installs e2e dependencies and Playwright browsers
- Waits for IdP and TestClient readiness using e2e/wait-for-idp-ready.ps1
- Runs Playwright tests (headless by default)

Usage examples:
  ./scripts/run-e2e.ps1                      # install deps, wait, run headless tests
  ./scripts/run-e2e.ps1 -Headed              # run headed tests
  ./scripts/run-e2e.ps1 -StartServices       # optionally also launch IdP/TestClient using start-e2e-dev

#>

[CmdletBinding()]
param(
    [switch]$Headed,
    [switch]$StartServices,
    [int]$TimeoutSeconds = 180,
    [string]$IdpUrl = 'https://localhost:7035',
    [string]$TestClientUrl = 'https://localhost:7001'
)

function Write-Info($msg){ Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg){ Write-Host $msg -ForegroundColor Green }
function Write-Err($msg){ Write-Host $msg -ForegroundColor Red }

$root = (Get-Location).Path

if ($StartServices) {
    Write-Info "Starting IdP and TestClient windows (scripts/start-e2e-dev.ps1)..."
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\start-e2e-dev.ps1"
    Start-Sleep -Seconds 2
}

Write-Info "Installing e2e npm dependencies and Playwright browsers..."
npm --prefix "$root\e2e" install
if ($LASTEXITCODE -ne 0) { Write-Err "npm install failed"; exit $LASTEXITCODE }

npm --prefix "$root\e2e" run install:browsers
if ($LASTEXITCODE -ne 0) { Write-Err "playwright install failed"; exit $LASTEXITCODE }

Write-Info "Waiting for IdP and TestClient to be ready (timeout: $TimeoutSeconds s)..."
& pwsh -NoProfile -ExecutionPolicy Bypass -File "$root\e2e\wait-for-idp-ready.ps1" -IdpUrl $IdpUrl -TestClientUrl $TestClientUrl -TimeoutSeconds $TimeoutSeconds
if ($LASTEXITCODE -ne 0) { Write-Err "wait-for-idp-ready failed (exit code $LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Info "Running Playwright tests..."
if ($Headed) {
    npm --prefix "$root\e2e" run test:headed
} else {
    npm --prefix "$root\e2e" test
}

exit $LASTEXITCODE
