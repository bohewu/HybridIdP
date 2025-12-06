# Device Flow Verification Script
# This script simulates the backend client requesting a device code, 
# and then polls for the token, allowing the user to focus on keying in the code in the browser.

$clientId = "testclient-device"
$scope = "openid profile offline_access"
$authority = "https://localhost:7035"

# 1. Request Device Code
Write-Host "Requesting Device Code from $authority..."
$response = Invoke-RestMethod -Uri "$authority/connect/device" -Method Post -Body @{
    client_id = $clientId
    scope = $scope
}

$deviceCode = $response.device_code
$userCode = $response.user_code
$verificationUri = $response.verification_uri_complete
$expiresIn = $response.expires_in
$interval = 5

if ($response.interval) {
    $interval = $response.interval
}

Write-Host "--------------------------------------------------------"
Write-Host "Device Code Request Successful!" -ForegroundColor Green
Write-Host "User Code:        $userCode" -ForegroundColor Yellow
Write-Host "Verification URL: $verificationUri" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------"
Write-Host "Please open the URL in your browser and enter the User Code."
Write-Host "Waiting for approval..."

# 2. Poll for Token
$startTime = Get-Date
$timeout = $startTime.AddSeconds($expiresIn)

while ((Get-Date) -lt $timeout) {
    # Use curl.exe for polling to avoid PowerShell's exception handling on 400 Bad Request
    # -s: Silent
    # -d: Data (form-urlencoded)
    # -k: Insecure (allow self-signed certs)
    $output = & curl.exe -s -k -d "grant_type=urn:ietf:params:oauth:grant-type:device_code" -d "device_code=$deviceCode" -d "client_id=$clientId" "$authority/connect/token"

    try {
        $body = $output | ConvertFrom-Json
        
        if ($body.access_token) {
            Write-Host ""
            Write-Host "--------------------------------------------------------"
            Write-Host "Device Flow Completed Successfully!" -ForegroundColor Green
            Write-Host "Access Token received."
            Write-Host "Access Token: $($body.access_token.Substring(0, 20))..."
            if ($body.refresh_token) {
                Write-Host "Refresh Token: $($body.refresh_token.Substring(0, 20))..."
            }
            Write-Host "--------------------------------------------------------"
            return
        }
        elseif ($body.error -eq "authorization_pending") {
            Write-Host -NoNewline "."
            Start-Sleep -Seconds $interval
        }
        elseif ($body.error -eq "slow_down") {
            Write-Host -NoNewline "s"
            $interval += 5
            Start-Sleep -Seconds $interval
        }
        else {
            Write-Host ""
            Write-Host "Error: $($body.error) - $($body.error_description)" -ForegroundColor Red
            return
        }
    }
    catch {
        # If parsing fails or other errors
        Write-Host ""
        Write-Host "Unexpected response or parsing error." -ForegroundColor Yellow
        Write-Host "Raw Output: $output"
        return
    }
}

Write-Host ""
Write-Host "Device Flow Timeout." -ForegroundColor Red
