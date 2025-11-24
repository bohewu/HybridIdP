<##
Checks that the TestClient app (https://localhost:7001 by default) is serving its home page
and includes a Login entry to trigger OIDC <-> IdP flows.

Usage:
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-testclient-ready.ps1 -Url 'https://localhost:7001' -TimeoutSeconds 120
#>

[CmdletBinding()]
param(
    [string]$Url = 'https://localhost:7001',
    [int]$TimeoutSeconds = 120,
    [int]$PollIntervalSeconds = 2
)

function Write-Info($m){ Write-Host $m -ForegroundColor Cyan }
function Write-Ok($m){ Write-Host $m -ForegroundColor Green }
function Write-Warn($m){ Write-Host $m -ForegroundColor Yellow }
function Write-Err($m){ Write-Host $m -ForegroundColor Red }

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
Write-Info "Checking TestClient URL: $Url (timeout: $TimeoutSeconds s)" 

while ([DateTime]::UtcNow -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Get -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
        $body = $r.Content -as [string]
        if ($body -and ($body -match 'Login' -or $body -match 'signin-oidc')) {
            Write-Ok "TestClient page contains expected login / signin anchors."
            exit 0
        } else {
            Write-Warn "TestClient reachable but expected login content not found yet."
        }
    } catch {
        Write-Warn "TestClient not reachable: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $PollIntervalSeconds
}

Write-Err "Timed out waiting for TestClient to become ready and show login content: $Url"
exit 3
