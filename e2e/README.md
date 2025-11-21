# E2E Tests (Playwright)

This folder contains Playwright tests for the HybridIdP solution.

Prerequisites:

- Node.js 18+
- Browsers: `npm run install:browsers`
- The apps are running locally:
  - IdP: <https://localhost:7035>
  - TestClient: <https://localhost:7001>

Check if apps are already running:

PowerShell (recommended for this repo):

```powershell
# quick TCP check (works in PowerShell Core as well)
Test-NetConnection -ComputerName 'localhost' -Port 7035
Test-NetConnection -ComputerName 'localhost' -Port 7001

# quick HTTP check (uses Invoke-WebRequest; will error on self-signed cert)
try {
  Invoke-WebRequest -Uri 'https://localhost:7035' -UseBasicParsing -ErrorAction Stop | Out-Null
  Write-Host "IdP appears to be running: https://localhost:7035"
} catch {
  Write-Host "IdP not reachable - start it using the command below in a separate terminal"
}
try {
  Invoke-WebRequest -Uri 'https://localhost:7001' -UseBasicParsing -ErrorAction Stop | Out-Null
  Write-Host "TestClient appears to be running: https://localhost:7001"
} catch {
  Write-Host "TestClient not reachable - start it using the command below in a separate terminal"
}
```

If either app is not running, open two separate shells (PowerShell) and run the following commands (in their respective folders):

PowerShell terminal 1 (IdP):

```powershell
cd Web.IdP
dotnet run --launch-profile https
```

PowerShell terminal 2 (TestClient):

```powershell
cd TestClient
dotnet run --launch-profile https
```

If you also use the client-side dev server for Playwright UI tests, start Vite in a third terminal:

```powershell
cd Web.IdP/ClientApp
npm run dev
```

Install dependencies:

```powershell
cd .\e2e
npm install
npm run install:browsers
```

Run tests (headless):

```powershell
npm test
```

Run tests (headed):

```powershell
npm run test:headed
```

Notes:

- Self-signed HTTPS is accepted via `ignoreHTTPSErrors` in config.
- Tests assume IdP and TestClient are already started.

Test user credentials:

- 基本測試用戶：`admin@hybridauth.local` / `Admin@123` (Admin 角色)
