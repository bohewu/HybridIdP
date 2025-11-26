# Recreate testclient-public via Admin API
$baseUrl = 'https://localhost:7035'
$adminEmail = 'admin@hybridauth.local'
$adminPassword = 'Admin@123'

[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

Write-Host 'Logging in as admin...' -ForegroundColor Cyan
$loginPage = Invoke-WebRequest -Uri ($baseUrl + '/Account/Login') -WebSession $session -SkipCertificateCheck -UseBasicParsing -ErrorAction Stop
$form = @{}
$inputPattern = '<input\s+[^>]*name=["''](?<name>[^"'']+)["''][^>]*value=["''](?<value>[^"'']*)["''][^>]*>'
foreach ($m in [regex]::Matches($loginPage.Content, $inputPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
    $n = $m.Groups['name'].Value; $v = $m.Groups['value'].Value
    if (-not [string]::IsNullOrEmpty($n)) { $form[$n] = $v }
}
$form['Input.Login'] = $adminEmail
$form['Input.Password'] = $adminPassword
$form['Input.RememberMe'] = 'false'
Invoke-WebRequest -Uri ($baseUrl + '/Account/Login') -WebSession $session -Method Post -Body $form -SkipCertificateCheck -UseBasicParsing -ErrorAction Stop
Write-Host 'Admin session established.' -ForegroundColor Green

Write-Host 'Searching for existing testclient-public...' -ForegroundColor Cyan
$clients = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients?search=testclient-public&take=100" -WebSession $session -SkipCertificateCheck -ErrorAction Stop
$client = $clients.items | Where-Object { $_.clientId -eq 'testclient-public' } | Select-Object -First 1
if ($client) {
    Write-Host "Found testclient-public (id=$($client.id)), backing up details and deleting..." -ForegroundColor Yellow
    $clientDetails = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients/$($client.id)" -WebSession $session -SkipCertificateCheck -ErrorAction Stop
    $backupFile = "./testclient-public-backup-$(Get-Date -Format 'yyyyMMddHHmmss').json"
    $clientDetails | ConvertTo-Json -Depth 12 | Out-File -FilePath $backupFile -Encoding utf8
    Invoke-RestMethod -Uri "$baseUrl/api/admin/clients/$($client.id)" -Method Delete -WebSession $session -SkipCertificateCheck -ErrorAction Stop
    Write-Host "Deleted existing client. Backup saved to $backupFile" -ForegroundColor Green
} else {
    Write-Host 'No existing testclient-public found, will create a new one.' -ForegroundColor Cyan
}

Write-Host 'Creating fresh testclient-public with canonical permissions...' -ForegroundColor Cyan
$createBody = @{ 
    ClientId = 'testclient-public';
    ClientSecret = $null;
    DisplayName = 'Test Client (Public)';
    ApplicationType = 'web';
    Type = 'public';
    ConsentType = 'explicit';
    RedirectUris = @('https://localhost:7001/signin-oidc');
    PostLogoutRedirectUris = @('https://localhost:7001/signout-callback-oidc');
    Permissions = @('ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','response_type:code','scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read')
} | ConvertTo-Json -Depth 12
$resp = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients" -Method Post -Body $createBody -ContentType 'application/json' -WebSession $session -SkipCertificateCheck -ErrorAction Stop
Write-Host "Created client id=$($resp.id)" -ForegroundColor Green

Write-Host 'Verifying client exists now...' -ForegroundColor Cyan
$clients = Invoke-RestMethod -Uri "$baseUrl/api/admin/clients?search=testclient-public&take=100" -WebSession $session -SkipCertificateCheck -ErrorAction Stop
$client = $clients.items | Where-Object { $_.clientId -eq 'testclient-public' } | Select-Object -First 1
if ($client) { Write-Host "Success: client id=$($client.id)" -ForegroundColor Green } else { Write-Host 'Failed to recreate client' -ForegroundColor Red }

Write-Host 'Done.' -ForegroundColor Cyan
