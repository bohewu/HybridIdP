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
  - When launching new windows the script can inherit the current environment variables so they start with DATABASE_PROVIDER/connection strings
    already set. Use `-InheritEnv` to enable this behavior (useful when starting from a session that sets `DATABASE_PROVIDER` for Postgres runs).

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

Running tests using PostgreSQL (helper)
------------------------------------

This repository includes a helper `scripts/run-e2e-postgres.ps1` which automates the common Postgres-backed e2e workflow: bring up docker-compose, ensure the Postgres DB exists and `pgcrypto` is enabled, apply Postgres EF migrations, start `Web.IdP` and `TestClient` in separate windows, wait for readiness, and run Playwright tests.

Basic example (from repo root):

```powershell
# start docker-compose, create DB + pgcrypto, run migrations, start services, and run tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -TimeoutSeconds 300
```

Only start Web.IdP window (skip TestClient) when running the Postgres helper:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -WebOnly -TimeoutSeconds 300
```

Seed API resources automatically
--------------------------------

If you want the run-e2e-postgres helper to seed API Resources (the `ApiResources` and `ApiResourceScopes` table data) before running tests, pass `-SeedApiResources`.

```powershell
# bring up containers, apply migrations, create API resources, start services and run tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -SeedApiResources -TimeoutSeconds 300
```

Normalize test client permissions (recommended)
--------------------------------------------

To avoid OpenIddict parsing errors caused by malformed/duplicated permissions in the `OpenIddictApplications.Permissions` column, the runner can normalize the TestClient permissions for both Postgres and MSSQL prior to seeding or running tests.

```powershell
# Normalize across both DBs, then seed and run tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -NormalizePermissions -SeedApiResources -TimeoutSeconds 300
```

TestClient UI readiness check
---------------------------

The Postgres helper can also validate that the TestClient home page is returning and contains a `Login` link or `signin-oidc` anchor before tests run. This check helps catch cases where TestClient is reachable at the TCP/HTTP level but still returning e.g. a static error page. The helper `scripts/check-testclient-ready.ps1` is automatically called by `run-e2e-postgres.ps1` after starting services.

pgcrypto validation
-------------------

The `run-e2e-postgres.ps1` helper now validates that the `pgcrypto` extension exists in the target database and will attempt to create it automatically. If the script cannot create the extension (for example when the DB user lacks CREATE EXTENSION privileges), it will print actionable commands you can run inside the Postgres container (as the `postgres` superuser) and then exit with a clear error. This helps avoid subtle `gen_random_uuid()` seed failures during migrations.

Only starting Web.IdP against Postgres (no tests)
-----------------------------------------------

If you want to run only `Web.IdP` pointing at Postgres (for manual dev or debugging), you can prepare Postgres and migrations then start the IdP process directly in your shell. Example sequence:

```powershell
# 1) Ensure postgres container is running (docker-compose up -d)
# 2) Create DB and enable pgcrypto inside the container (adjust container name / user):
docker exec -it <postgres_container> psql -U user -d postgres -c "CREATE DATABASE hybridauth_idp;"
docker exec -it <postgres_container> psql -U user -d hybridauth_idp -c "CREATE EXTENSION IF NOT EXISTS pgcrypto;"

# 3) Set env vars in the same shell (so dotnet run inherits them):
$env:DATABASE_PROVIDER = 'PostgreSQL'
$env:ConnectionStrings__PostgreSqlConnection = 'Host=localhost;Port=5432;Database=hybridauth_idp;Username=user;Password=password'

# 4) Apply Postgres migrations (from repo root):
cd Infrastructure.Migrations.Postgres
dotnet ef database update --startup-project ..\Web.IdP

# 5) Run Web.IdP (this will use the Postgres provider):
cd ..\Web.IdP
dotnet run --launch-profile https
```

Notes:
- `scripts/run-e2e-postgres.ps1` is intended as a convenience wrapper for local development and should be run from the repository root so it can find `docker-compose.yml` and relative projects.
- The script uses the first container matching `db-service` or the `postgres` image when locating the DB container. Use `docker ps` to confirm container names if needed.
