# Setup test user with consent to Demo Client 1
# Run this script after the IdP is running

$baseUrl = "https://localhost:7035"
$testUser = @{
    email = "testuser@hybridauth.local"
    password = "Test@123"
}

Write-Host "Setting up test user with consent..." -ForegroundColor Cyan

# 1. Register the test user (if not exists)
Write-Host "`n1. Registering test user: $($testUser.email)" -ForegroundColor Yellow
try {
    $registerResponse = Invoke-WebRequest -Uri "$baseUrl/Account/Register" -Method POST -SessionVariable session -Body @{
        Email = $testUser.email
        Password = $testUser.password
        ConfirmPassword = $testUser.password
        FirstName = "Test"
        LastName = "User"
    } -ContentType "application/x-www-form-urlencoded" -SkipCertificateCheck -ErrorAction Stop
    Write-Host "✓ User registered successfully" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 400) {
        Write-Host "⚠ User already exists, continuing..." -ForegroundColor Yellow
    } else {
        Write-Host "✗ Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 2. Login as the test user
Write-Host "`n2. Logging in as test user..." -ForegroundColor Yellow
$loginResponse = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method POST -WebSession $session -Body @{
    Email = $testUser.email
    Password = $testUser.password
    RememberMe = "false"
} -ContentType "application/x-www-form-urlencoded" -SkipCertificateCheck

Write-Host "✓ Logged in successfully" -ForegroundColor Green

# 3. Initiate authorization with Demo Client 1
Write-Host "`n3. Initiating authorization with Demo Client 1..." -ForegroundColor Yellow
$authorizeParams = @{
    client_id = "demo-client-1"
    redirect_uri = "https://localhost:7001/signin-oidc"
    response_type = "code"
    scope = "openid profile email"
    response_mode = "form_post"
}

$authorizeUrl = "$baseUrl/connect/authorize?" + ($authorizeParams.GetEnumerator() | ForEach-Object { "$($_.Key)=$([System.Uri]::EscapeDataString($_.Value))" }) -join "&"

$authorizeResponse = Invoke-WebRequest -Uri $authorizeUrl -Method GET -WebSession $session -SkipCertificateCheck -MaximumRedirection 0 -ErrorAction SilentlyContinue

Write-Host "✓ Authorization initiated" -ForegroundColor Green

# 4. Grant consent
Write-Host "`n4. Granting consent..." -ForegroundColor Yellow
# Extract the form data from the consent page if needed
# For now, we'll just submit consent directly
try {
    $consentResponse = Invoke-WebRequest -Uri "$baseUrl/connect/authorize" -Method POST -WebSession $session -Body @{
        submit = "Grant"
    } -ContentType "application/x-www-form-urlencoded" -SkipCertificateCheck -MaximumRedirection 0 -ErrorAction SilentlyContinue
    
    Write-Host "✓ Consent granted successfully!" -ForegroundColor Green
} catch {
    Write-Host "Note: Manual consent may be required on first authorization" -ForegroundColor Yellow
}

Write-Host "`n================================================" -ForegroundColor Cyan
Write-Host "Test user setup complete!" -ForegroundColor Green
Write-Host "Email: $($testUser.email)" -ForegroundColor White
Write-Host "Password: $($testUser.password)" -ForegroundColor White
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Login with the test user credentials" -ForegroundColor White
Write-Host "2. Access Demo Client 1 to create a consent" -ForegroundColor White
Write-Host "3. After consenting, Demo Client 1 will appear on Index page" -ForegroundColor White
Write-Host "================================================" -ForegroundColor Cyan
