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

Run a single test file (useful for debugging):

```powershell
# from the e2e directory
npx playwright test tests/login.spec.ts
npx playwright test tests/testclient-login-consent.spec.ts
npx playwright test tests/logout.spec.ts
npx playwright test tests/admin-clients-crud.spec.ts
```

Notes:

- Self-signed HTTPS is accepted via `ignoreHTTPSErrors` in config.
- Tests assume IdP and TestClient are already started.


Developer helpers (PowerShell) — quick start ⚡

This repo includes small PowerShell helper scripts to make it easier to run the IdP, TestClient, and Playwright E2E tests in separate terminals.

- `scripts/start-e2e-dev.ps1` — opens separate pwsh windows for:
  - `dotnet run --project .\Web.IdP\Web.IdP.csproj --launch-profile https` (IdP)
  - `dotnet run --project .\TestClient\TestClient.csproj --launch-profile https` (TestClient)
  - optionally starts the Vite dev server when run with `-StartVite`

- `e2e/wait-for-idp-ready.ps1` — waits for IdP/TestClient to be reachable, attempts an admin sign-in, and calls the protected admin health endpoint (`/api/admin/health`) to verify seeding/initialization finished.

- `scripts/run-e2e.ps1` — convenience wrapper that:
  1. installs e2e npm deps and Playwright browsers
  2. (optionally) launches services via `scripts/start-e2e-dev.ps1` when run with `-StartServices`
  3. invokes `e2e/wait-for-idp-ready.ps1` to wait for readiness
  4. runs Playwright tests (headless by default; use `-Headed` to run headed)

Example flows

1) Start services manually in separate terminals and run tests

```powershell
# In terminal 1 (IdP)
cd Web.IdP
dotnet run --launch-profile https

# In terminal 2 (TestClient)
cd TestClient
dotnet run --launch-profile https

# In a third terminal, wait for readiness and run tests
cd .\e2e
..\scripts\wait-for-idp-ready.ps1 -IdpUrl 'https://localhost:7035' -TestClientUrl 'https://localhost:7001' -TimeoutSeconds 180
npm test
```

2) Single-command developer flow (opens helper windows + runs tests)

```powershell
# From repo root: starts services, waits for readiness, runs tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e.ps1 -StartServices -TimeoutSeconds 180

# Run headed tests instead
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e.ps1 -StartServices -Headed
```

If the helpers finish successfully the admin account and admin API will be reachable and you can run `npm test` in `./e2e` to run Playwright E2E.

Test user credentials:

- 基本測試用戶：`admin@hybridauth.local` / `Admin@123` (Admin 角色)

TestClient (OIDC) details and required scopes/permissions
-----------------------------------------------------

The Playwright e2e tests rely on a local TestClient web app that performs OIDC flows against the local IdP. To make the tests reliable the TestClient must be registered in the IdP with the following minimal configuration:

- ClientId: `testclient-public`
- ApplicationType: `web` (public client — no secret)
- Redirect URIs: `https://localhost:7001/signin-oidc`
- PostLogoutRedirectUris: `https://localhost:7001/signout-callback-oidc`
- Grant types / Permissions (OpenIddict permission strings):
  - ept:authorization, ept:token, ept:logout
  - gt:authorization_code, gt:refresh_token
  - scp:openid, scp:profile, scp:email, scp:roles
  - scp:api:company:read, scp:api:inventory:read

Notes:
- The tests expect the above API scopes (api:company:read, api:inventory:read) to exist in OpenIddict and be associated with API resources. The readiness script `e2e/wait-for-idp-ready.ps1` will attempt to verify and create the `testclient-public` client and the two API scopes automatically when it runs (if not present).
- If you run the IdP/TestClient manually, ensure `testclient-public` is seeded or created via the Admin UI before running the Playwright suite to avoid `invalid_scope` or `invalid_client` errors during authorize.
- The TestClient's options in `TestClient/Program.cs` show the requested scopes and must match allowed scopes in the IdP for the OIDC flow to succeed.
