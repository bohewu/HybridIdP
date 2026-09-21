# Testing Guide

This document provides a comprehensive guide to setting up the environment, running automated tests, and performing manual verification for the HybridIdP project.

## 1. Prerequisites

Before running tests or the application, ensure you have the following installed:

- **.NET 10.0 SDK**
- **SQL Server Express** (or LocalDB) or **PostgreSQL**
- **PowerShell 7+** (Recommended)

### Database Setup
The application uses Entity Framework Core Migrations. Ensure your connection string in `appsettings.json` (or `appsettings.Development.json`) is configured correctly.

#### SQL Server (Default)
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=HybridIdP;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

#### PostgreSQL (Optional)
If using PostgreSQL, ensure the `DatabaseProvider` is set to `Postgres` in `appsettings.json`.

---

## 2. Data Seeding

The application includes a robust data seeding mechanism (`DataSeeder`) that runs automatically on startup if the database is empty or if specific test data is missing.

### Standard Seeded Data (All Environments)
- **Roles**: Admin, User, ApplicationManager.
- **Scopes**: openid, email, profile, roles, phone.
- **Claims**: Standard OIDC claims mapped to scopes.

### Privileged Test Administrator (Explicit Opt-In)
The fixed privileged test administrator is disabled by default: an absent or `false` `SeedData:PrivilegedTestAdminBootstrap:Enabled` setting does not create or mutate it in any environment. To enable it, set `SeedData:PrivilegedTestAdminBootstrap:Enabled=true` (or `SeedData__PrivilegedTestAdminBootstrap__Enabled=true`) and run in exactly `Development` or `Test`.

The opt-in has no effect in Production, Staging, an empty/default or unknown environment, or any other environment. Ordinary seed data remains independent of this privileged-test-admin bootstrap.

The operational first-administrator bootstrap is a separate, disabled-by-default capability for a genuinely fresh deployment. It does not create, repair, reset, or promote test accounts, and it must not replace or modify this fixed Development/Test fixture. See [Deployment Guide: One-Time Operational First Administrator](DEPLOYMENT_GUIDE.md#one-time-operational-first-administrator).

### Test Seeded Data (Development Only)
When running in `Development` environment, the following additional data is seeded for testing:

- **Public Test Client**: 
  - ClientId: `testclient-public`
  - Redirect URI: `https://localhost:7001/signin-oidc`
  - Flow: Authorization Code + PKCE
  - Consent: Explicit

- **Demo Client**:
  - ClientId: `demo-client-1`
  - Redirect URI: `https://localhost:7001/signin-oidc`
  - Flow: Authorization Code + PKCE
  - Consent: Explicit

- **M2M Test Client**:
  - ClientId: `testclient-m2m`
  - Secret: `m2m-test-secret-2024`
  - Flow: Client Credentials

- **Device Test Client**:
  - ClientId: `testclient-device`
  - Flow: Device Flow

- **Standard Test User**:
  - Email: `testuser@hybridauth.local`
  - Password: `Test@123`

- **API Resources**:
  - `company_api` (Scopes: `api:company:read`, `api:company:write`)
  - `inventory_api` (Scopes: `api:inventory:read`)

---

## 3. Automated Tests

### Credential Migration Validation

Run the bounded Stage 1 and Stage 2 credential-migration checks from the repository root:

```powershell
python .\tools\run_credential_migration_validation.py
```

The default run executes only the existing `Stage1BindingMigrationTests`,
`Stage2CredentialMigrationTests`, `Stage2DirectoryCredentialTests`,
`CredentialMigrationCeremonyTests`, and `MigrationIssuanceGuardTests`. It forces
credential migration and directory-integration switches off in each child test
process, emits phase progress and sanitized test counts, and never performs AD
actions, password resets, deployment, commit, or push operations.

The recovery-proof coverage added to Stage 2 is exercised by focused local
tests rather than by a connected recovery environment:

```powershell
dotnet test Tests.Application.UnitTests/Tests.Application.UnitTests.csproj --filter "FullyQualifiedName~RecoveryProofContractTests|FullyQualifiedName~Stage2CredentialMigrationTests|FullyQualifiedName~UsersControllerCredentialRecoveryTests"
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --filter "FullyQualifiedName~RecoveryProofFoundationTests|FullyQualifiedName~HttpContextRecoveryProofAuthorizerTests"
dotnet test Tests.Infrastructure.IntegrationTests/Tests.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~RecoveryProofPersistenceTests"
dotnet test Tests.Web.IdP.UnitTests/Tests.Web.IdP.UnitTests.csproj --filter "FullyQualifiedName~RecoveryEmailControllerTests|FullyQualifiedName~CredentialMigrationCeremonyTests"
dotnet test Tests.SystemTests/Tests.SystemTests.csproj --filter "FullyQualifiedName~CredentialRecoveryApiContractTests"
```

These tests cover recovery-address independence and authorization, destination
verification, OTP attempts/expiry/replay and atomic consumption, administrator
resend/replacement/approval constraints, sanitized failures and audit contracts,
default-off configuration, no permanent email-MFA side effect, and preserved
post-proof reset/bind/finalization ordering. The persistence tests verify the EF
model through local test infrastructure; they are not deployment-database
execution. The controller and system tests use local/mocked boundaries and do
not constitute AD, provider, SMTP, browser, or production end-to-end evidence.

Frontend verification for this feature belongs in `Web.IdP/ClientApp` and must
cover the migration proof controls, Account/MFA recovery-email settings,
administrator Users assistance, and sanitized audit presentation in both
`en-US` and `zh-TW`:

```powershell
npm test -- --run
npm run build
```

Browser evidence, when collected, must use non-sensitive fixtures and cover
desktop and mobile states without claiming connected directory behavior. Read
the final UI task result before recording exact screenshots, viewport results,
or pass counts.

#### HIDP-16 configurable recovery guidance (2026-09-16)

Native recovery guidance is optional and OSS-neutral. All seven settings under
`ForgotPasswordRecovery` default to an empty string; leaving a setting empty or
whitespace-only, or clearing it later and restarting the application, hides that
slot without rendering an empty guidance container.

```json
{
  "ForgotPasswordRecovery": {
    "TopNotice": "@Recovery.Guidance.Top",
    "VerificationTip": "Check your junk mail folder if the code has not arrived.",
    "ResetTip": "@Recovery.Guidance.Reset",
    "SuccessReminder": "Use your new password the next time you sign in.",
    "SupportText": "@Recovery.Guidance.Support",
    "SupportLabel": "Recovery help",
    "SupportUrl": "https://help.example.org/account-recovery"
  }
}
```

`TopNotice`, `VerificationTip`, `ResetTip`, `SuccessReminder`, `SupportText`,
and `SupportLabel` accept either literal plain text or `@ResourceKey`. A literal
is rendered unchanged as encoded text. For the resource form, whitespace around
the key after `@` is trimmed and resolution is: enabled exact culture, then
enabled `en-US`, then hidden. A disabled exact-culture row is skipped and may
therefore fall back to enabled `en-US`; a missing exact-culture row behaves the
same way. A blank setting, blank key, missing or disabled fallback, or resolved
whitespace value hides the slot. If an enabled exact-culture row exists but its
value is whitespace-only, that resolved slot is hidden rather than falling
through to `en-US`.

Resource rows are resolved on every request, so an enabled Resource value change
is visible on the next request. The seven configuration values use the existing
startup-bound `IOptions<ForgotPasswordRecoveryOptions>` lifecycle: restart after
configuration changes, and do not rely on hot reload.

`SupportUrl` is independent and is never treated as a Resource key. A support
link renders only when the URL is absolute `http` or `https`, contains no
UserInfo, and `SupportLabel` resolves to nonblank text. `SupportText` is
independent and can render without an eligible link.

Placement is stable across the start, awaiting-code, awaiting-password, and
success phases. Core validation, sent, password-prompt, error, and success
content remains first and visually authoritative. `TopNotice` follows applicable
core status; `VerificationTip`, `ResetTip`, and `SuccessReminder` then appear
only in their matching code, password, and confirmed-success phases, before the
principal interaction. Support content appears near the bottom after the phase
form or action. These slots use neutral secondary styling, preserve existing
focus and ARIA behavior, and are Razor-encoded plain text; there is no
`Html.Raw` path.

Guidance does not vary by account existence, eligibility, configured recovery
email, or source-cohort classification. This delivery did not change recovery
routing or availability, OTP behavior, directory/AD writes, sessions, tokens,
or `PasswordHash` rules.

#### HIDP-17 Legacy Password Sync focused verification (2026-09-18)

The isolated candidate was verified for exactly two configured password
destinations: Directory/AD and one default-off Legacy Password Sync API. The
12/12 destination matrix proves Directory/Legacy off/on = 0/1 calls, on/off =
1/0, on/on = 1/1 with Directory before Legacy, and off/off = 0/0. Legacy-only
dispatch remains bound to an authorized required-change attempt, consumed
native proof challenge, or consumed migration continuation.

The terminal non-connected checks were:

```powershell
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DirectoryRequiredCredentialChangeServiceTests.ChangeAsync_DestinationMatrix|FullyQualifiedName~Stage2CredentialMigrationServiceTests.CommitAsync_DestinationMatrix|FullyQualifiedName~NativePasswordRecoveryResetServiceTests.ResetAsync_DestinationMatrix"
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter "FullyQualifiedName~DirectoryRequiredCredentialChangeServiceTests|FullyQualifiedName~Stage2CredentialMigrationServiceTests|FullyQualifiedName~NativePasswordRecoveryResetServiceTests|FullyQualifiedName~LegacyPasswordSyncCoordinatorTests|FullyQualifiedName~LegacyPasswordSyncAttemptStoreTests|FullyQualifiedName~RecoveryProofFoundationTests|FullyQualifiedName~CredentialMigrationOptionsTests"
dotnet test Tests.Application.UnitTests/Tests.Application.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Stage2CredentialMigrationTests"
dotnet test Tests.Infrastructure.UnitTests/Tests.Infrastructure.UnitTests.csproj --no-restore --filter "FullyQualifiedName~LegacyPasswordSyncContractTests"
dotnet test Tests.Application.UnitTests/Tests.Application.UnitTests.csproj --no-restore --filter "FullyQualifiedName~CredentialMigrationContractTests"
dotnet build HybridAuthIdP.sln --no-restore -nodeReuse:false --verbosity:minimal
```

Post-review results were 12/12 for the three destination matrices, 89/89 for
the affected HybridIdP caller/coordinator/durable-source/options slice, 23/23
for the Stage 2 application slice, 38/38 for Legacy Password Sync
contract/options/transport, and 3/3 for credential-migration contracts. The
HybridIdP solution build passed with zero warnings and zero errors.

The private endpoint separately rejected `Guid.Empty` before its coordinator
or writers in the exact focused test (1/1). Its complete focused PasswordSync
surface passed 57/57, and its solution build passed with zero warnings and zero
errors. Its routing topology remains outside this public contract.

The HIDP-16 Guidance TestServer limiter seam was corrected without changing
product defaults. Its affected exact method passed 4/4, the other four exact
methods passed 12/12, 1/1, 1/1 and 3/3, and the exact-five aggregate passed
21/21 with terminal lifecycle and cleanup evidence. Discovery, startup output,
timeout or process termination were not counted as PASS.

These results are offline implementation evidence only. Deployment, production
migration, external interoperability, connected database/provider checks, real
Legacy Password Sync requests, live AD/directory writes, credential use, push,
release and publication were **NOT RUN** and remain separate follow-up gates.


### Database Migration Lifecycle Checks

Run these bounded, non-connected checks from the repository root before an
operator-controlled migration. They do not use a deployment database, start a
normal IdP host, or invoke AD, provider, or credential workflows:

```bash
bash deployment/migrate-db.sh --help
bash deployment/tests/deployment-hardening-tests.sh
dotnet test Tests.Web.IdP.UnitTests/Tests.Web.IdP.UnitTests.csproj --filter "FullyQualifiedName~DatabaseMigrationLifecycleTests"
python tools/run_credential_migration_validation.py --dry-run
```

The deployment hardening script uses local command stubs to verify the
schema-only migration command contract for both SQL Server and PostgreSQL. The
credential-migration runner's `--dry-run` mode only prints its selected test
commands; its normal focused run keeps every Stage 1 and Stage 2 feature switch
off and does not perform connected directory actions.

Recovery proof adds the provider-specific migrations
`20260905060135_AddCredentialRecoveryProofFoundation` for SQL Server and
`20260905060159_AddCredentialRecoveryProofFoundation` for PostgreSQL. Apply the
appropriate migration only through the existing operator-controlled migration
procedure. No connected deployment database was migrated or verified as part
of the credential-recovery delivery round documented here.

### Unit Tests
Run unit tests to verify individual components.
```powershell
dotnet test Core.Domain.UnitTests
dotnet test Core.Application.UnitTests
dotnet test Infrastructure.UnitTests
dotnet test Web.IdP.UnitTests
```

### System Tests
System tests start the real `Web.IdP` child process and use the configured local SQL Server by default.
```powershell
dotnet test Tests.SystemTests/Tests.SystemTests.csproj
```
`WebIdPServerFixture` reads the selected connection string from
`Web.IdP/appsettings.Development.json` and injects it into the child process so that user secrets
or unrelated host settings cannot silently change the test database or TLS behavior. Override it
only for the current test process when needed:

```powershell
$env:TEST_DATABASE_PROVIDER = "SqlServer"
$env:TEST_SQLSERVER_CONNECTION_STRING = "<test-only SQL Server connection string>"

# Or:
$env:TEST_DATABASE_PROVIDER = "PostgreSQL"
$env:TEST_POSTGRESQL_CONNECTION_STRING = "<test-only PostgreSQL connection string>"
```

Do not point these variables at a deployed or production database. Tests that restart the child
host run in a non-parallel collection so they cannot invalidate active requests from other system
tests. The child host also uses process-local OpenIddict signing and encryption keys, avoiding the
Windows development-certificate store; that option is rejected outside Development and Test. The
fixture explicitly opts into the privileged test administrator bootstrap only for its allowed
Development test host.

---

## 4. Manual Verification

### Verifying Public Client Flow
1. Start the application:
   ```powershell
   dotnet run --project Web.IdP
   ```
2. Navigate to the test URL (simulating a client request):
   ```
   https://localhost:5001/connect/authorize?client_id=testclient-public&response_type=code&scope=openid%20email%20profile%20roles&redirect_uri=https://localhost:7001/signin-oidc&code_challenge=...&code_challenge_method=S256
   ```
   *(Note: You'll need a valid PKCE challenge generator for the URL above)*

3. Log in with `testuser@hybridauth.local` / `Test@123`.
4. Verify the Consent screen appears (since consent is Explicit).
5. Approve the request.
6. Verify redirection to `https://localhost:7001/signin-oidc` with an authorization code.

### Verifying Device Flow
1. Use the [Device Flow Console Client](../samples/TestClient.Device/Program.cs).
2. Run the client:
   ```powershell
   dotnet run --project samples/TestClient.Device/TestClient.Device.csproj
   ```
3. Follow the on-screen instructions to visit the verification URL and enter the user code.
4. Verify token retrieval.

### Verifying API Resources
Check the database tables `ApiResources` and `ApiResourceScopes` to confirm `company_api` and `inventory_api` are populated.

---
---

## 5. MFA Testing Guide (多重因素驗證測試)

本專案支援多種 MFA 方式，包含 TOTP、Email OTP 以及 Passkey。

### TOTP (驗證碼產生器)
- **手動測試**: 進入「個人設定」 -> 「MFA 設定」 -> 「Setup Authenticator」，使用手機 App (Microsoft/Google Authenticator) 掃描。
- **技巧**: 複製密鑰並使用網頁版工具 (如 [totp.app](https://totp.app/))，無需手機。

### Email OTP (信箱驗證碼)
- **手動測試**: 在設定頁點擊「發送驗證碼」。
- **技巧**: 使用 [Mailpit](http://localhost:8025) 攔截本地郵件，無需真實收信。
- **安全行為**: 每組待驗證碼最多嘗試 5 次；第 5 次錯誤後，即使輸入原本正確的碼也必須失敗。重新發送的新碼會取得新的 5 次嘗試額度。

### Passkey (WebAuthn)
- **手動測試**: 使用電腦生物辨識或 Yubikey。
- **技巧**: 使用 Chrome DevTools -> WebAuthn 面板建立虛擬金鑰測試。

---

## 6. Manual Testing: Device Authorization Flow

### Step 1: Initiate Request
```powershell
curl --location 'https://localhost:7035/connect/device' `
--header 'Content-Type: application/x-www-form-urlencoded' `
--data-urlencode 'client_id=testclient-device' `
--data-urlencode 'scope=openid profile offline_access'
```

### Step 2: Approve
1. 瀏覽器開啟 `https://localhost:7035/connect/verify`。
2. 輸入 `user_code` 並登入。

### Step 3: Get Token
```powershell
curl --location 'https://localhost:7035/connect/token' `
--header 'Content-Type: application/x-www-form-urlencoded' `
--data-urlencode 'grant_type=urn:ietf:params:oauth:grant-type:device_code' `
--data-urlencode 'client_id=testclient-device' `
--data-urlencode 'device_code=[DEVICE_CODE]'
```

---

## 7. E2E Test Client Credentials

### Public Client (`testclient-public`)
- **Client Type**: Public (SPA/Mobile)
- **Grant Types**: Auth code + PKCE, Refresh token
- **Redirect URI**: `https://localhost:7001/signin-oidc`
- **Secret**: None (Requires PKCE)

### M2M Client (`testclient-m2m`)
- **Client Type**: Confidential
- **Secret**: `m2m-test-secret-2024`

---
**Last Updated**: 2025-12-19
