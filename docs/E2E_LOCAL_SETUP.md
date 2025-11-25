# E2E local run — clone & verify (SQL Server and PostgreSQL)

This document explains step-by-step how to get a fresh clone of the repository running end-to-end (E2E) tests locally and reliably reproduce the green test pass seen in CI. It covers both PostgreSQL and SQL Server flows and lists common troubleshooting tips for local environments.

Notes: the repository includes helper scripts that automate much of the setup. This guide assumes Windows (PowerShell pwsh.exe) but commands should be easily portable to macOS/Linux with small edits.

---

## Prerequisites (essential)

- Docker Desktop (Docker Engine + Compose v2)
- .NET SDK 10 (repo docs require .NET 10)
- Node.js 18+
- PowerShell Core (pwsh) on Windows — run scripts with `pwsh -NoProfile -ExecutionPolicy Bypass -File <script>`
- At least ~8–16 GB RAM and multi-core CPU (E2E runs use browsers and multiple services)

Optional (helpful): Git, VS Code, Playwright CLI (installed via npm below)

---

## Quick clone & run — recommended: Postgres automated flow

This is the easiest way to get a fresh environment prepared, migrated and seeded, then run the Playwright tests end-to-end. The helper script will:
- Bring up containers (Postgres, MSSQL, Redis, idp services)
- Create DBs and apply any migrations for the Postgres provider
- Ensure pgcrypto extension is present (Postgres)
- Run `normalize-testclient-permissions` and `setup-test-api-resources` as needed
- Start local web apps and run Playwright tests

1) Clone the repo and change directory

```powershell
git clone <repo-url>
cd HybridIdP
```

2) Run the Postgres helper (copy/paste; please allow up to 10 minutes on a slow machine)

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e-postgres.ps1 -UpCompose -StartServices -NormalizePermissions -SeedApiResources -TimeoutSeconds 600
```

Notes:
- The script assumes default Postgres credentials inside docker compose: user=`user`, password=`password`, db=`hybridauth_idp` (these match docker-compose.yml).
- If Postgres lacks `pgcrypto`, the helper prints the exact container command to create it as the `postgres` superuser.

This helper should leave your dev environment ready and will execute the Playwright test suite for you.

---

## SQL Server (MSSQL) flow — a little more manual, but supported

By default docker-compose in this repo uses SQL Server in development. The MSSQL E2E flow requires migrations/seeding for the SQL Server database.

1) Start MSSQL + Redis containers

```powershell
# From repo root
pwsh -NoProfile -ExecutionPolicy Bypass -Command "docker compose up -d mssql-service redis-service"
```

2) Apply EF Core migrations for SQL Server (if needed)

```powershell
cd Infrastructure.Migrations.SqlServer
# Use Service/StartupProject parameter if needed for your environment
dotnet ef database update --startup-project ..\Web.IdP
cd ..\..
```

3) Ensure test client permissions are normalized (script exists and is robust)

```powershell
.\scripts\normalize-testclient-permissions.ps1 -Provider SqlServer -MssqlContainer hybrididp-mssql-service-1 -SqlSaPassword "YourStrong!Passw0rd"
```

4) Start IdP and TestClient and run tests (wrapper)

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-e2e.ps1 -StartServices -TimeoutSeconds 600
```

Notes:
- The SQL Server container uses `YourStrong!Passw0rd` as SA password in docker-compose.yml by default — change consistently if you override it locally.
- `normalize-testclient-permissions.ps1` now handles sqlcmd cert trust (uses -C) and sets `QUOTED_IDENTIFIER` so SQL updates succeed.

---

## Playwright / E2E tests — local commands

Install e2e dependencies and browsers (once):

```powershell
cd e2e
npm ci
npx playwright install --with-deps
```

Run tests (full suite):

```powershell
npx playwright test --workers=2 --retries=0
```

Run a single test file:

```powershell
npx playwright test tests/feature-roles/roles-crud.spec.ts --project=chromium --trace on --workers=1 --retries=0
```

Run a single test by title (regex):

```powershell
npx playwright test -g "Admin - Roles CRUD" --project=chromium --trace on
```

---

## Typical flakiness / troubleshooting checklist

If tests fail on a fresh clone, check these first:

- Playwright browsers not installed → run `npx playwright install --with-deps` in the `e2e/` directory.
- Admin account missing / different credentials: helper uses `admin@hybridauth.local` / `Admin@123`. If you changed them, update `scripts/wait-for-idp-ready.ps1` and `setup-test-api-resources.ps1` flags accordingly.
- Port conflicts (default ports): IdP = 7035, TestClient = 7001. Make sure these are free (or update compose & script env vars).
- Postgres extension pgcrypto missing → `scripts/run-e2e-postgres.ps1` tries to create it, but if you are using a managed DB or non-superuser you may need to create it manually.
- SQL Server connection cert errors → `normalize-testclient-permissions.ps1` uses sqlcmd `-C` to trust the server cert; if you run sqlcmd manually you may need to add `-C` or adjust encryption settings.
- Slow environment timeouts → increase timeout on runners: `-TimeoutSeconds 600` or bigger.

How to collect logs and artefacts for debugging:

- Playwright traces are generated when a test fails (see output). Use `npx playwright show-trace <trace.zip>`.
- Inspect container logs:
  - `docker logs --tail 200 hybrididp-idp-service-1`
  - `docker logs --tail 200 hybrididp-postgres-service-1`
  - `docker logs --tail 200 hybrididp-mssql-service-1`
- DB queries (verify testclient permissions & admin user):
  - Postgres: `docker exec -i postgres_container psql -U user -d hybridauth_idp -c "SELECT \"Permissions\" FROM \"OpenIddictApplications\" WHERE \"ClientId\" = 'testclient-public';"`
  - MSSQL: `docker exec -i mssql_container /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "YourStrong!Passw0rd" -d hybridauth_idp -C -Q "SELECT Permissions FROM OpenIddictApplications WHERE ClientId = 'testclient-public';"`

---

## Expected results on a fresh clone

If your environment matches the prerequisites and you follow the steps above, you should be able to:

- Run the Postgres helper: service up → migrations → normalization → seeding → start services → Playwright run.
- Run the MSSQL flow: compose up MSSQL+Redis → apply migrations → normalize permissions → start apps → Playwright run.

With a stable environment and no resource constraints, tests should pass consistently. Intermittent failures are typically timing / resource issues and generally solved by increasing timeouts or running fewer workers.

---

## Appendices

1) Admin account used by helpers: `admin@hybridauth.local` / `Admin@123`.
2) Default container service names used by scripts: `postgres-service`, `mssql-service`, `idp-service`, `redis-service`.
3) If you want me to add CI smoke checks, create a simplified e2e job, or push this doc + a small checklist to a branch and open a PR, tell me and I'll do it.

Good luck — if you want I can now push these doc changes and open a PR for you.
