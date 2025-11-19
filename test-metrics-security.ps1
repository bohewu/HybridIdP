# Test script to verify /metrics endpoint IP whitelist protection
# Run this from: .\test-metrics-security.ps1

Write-Host "=== Testing Prometheus /metrics endpoint security ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Access from localhost (should succeed)
Write-Host "Test 1: Accessing from localhost (::1 / 127.0.0.1)" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri https://localhost:7035/metrics -SkipCertificateCheck -ErrorAction Stop
    Write-Host "✅ PASS - Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "   Localhost is allowed (as expected)" -ForegroundColor Gray
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 403) {
        Write-Host "❌ FAIL - Status: 403 Forbidden" -ForegroundColor Red
        Write-Host "   Localhost should be allowed but was blocked!" -ForegroundColor Red
    } else {
        Write-Host "❌ FAIL - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""

# Test 2: Explanation about external IP blocking
Write-Host "Test 2: External IP blocking verification" -ForegroundColor Yellow
Write-Host "   ℹ  To test external IP blocking, you would need to:" -ForegroundColor Gray
Write-Host "      1. Deploy to a server" -ForegroundColor Gray
Write-Host "      2. Try accessing from a non-whitelisted IP" -ForegroundColor Gray
Write-Host "      3. Expect 403 Forbidden response" -ForegroundColor Gray
Write-Host ""
Write-Host "   Current whitelist configuration:" -ForegroundColor Gray
Write-Host "   - Development: 127.0.0.1, ::1, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16" -ForegroundColor Gray
Write-Host "   - Production: 127.0.0.1, ::1" -ForegroundColor Gray

Write-Host ""
Write-Host "=== Security test completed ===" -ForegroundColor Cyan
