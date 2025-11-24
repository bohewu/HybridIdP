<# Simple helper to verify presence and parse validity of the e2e helper scripts. #>
$files = @(
  '.\e2e\wait-for-idp-ready.ps1',
  '.\scripts\start-e2e-dev.ps1',
  '.\scripts\run-e2e.ps1'
)

foreach ($f in $files) {
  Write-Host '---'; Write-Host "Checking: $f"
  if (-not (Test-Path $f)) { Write-Host "Missing: $f" -ForegroundColor Red; exit 1 }
  Write-Host "Exists: $f" -ForegroundColor Green

  Write-Host "Readable (first lines):" -ForegroundColor Cyan
  Get-Content $f -TotalCount 8 | ForEach-Object { Write-Host "  $_" }
}

Write-Host '---'; Write-Host 'Now executing a short (2s) runtime check for wait-for-idp-ready.ps1 (expected to fail if services are not running).'
& pwsh -NoProfile -ExecutionPolicy Bypass -File .\e2e\wait-for-idp-ready.ps1 -IdpUrl 'https://localhost:7035' -TestClientUrl 'https://localhost:7001' -TimeoutSeconds 2 -PollIntervalSeconds 1
exit $LASTEXITCODE
