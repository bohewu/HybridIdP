# Testing Guide

This document provides a comprehensive guide to setting up the environment, running automated tests, and performing manual verification for the HybridIdP project.

## 1. Prerequisites

Before running tests or the application, ensure you have the following installed:

- **.NET 10.0 SDK** (Preview)
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
- **Admin User**: `admin@hybridauth.local` / `Admin@123` (Linked to a Person entity).
- **Scopes**: openid, email, profile, roles, phone.
- **Claims**: Standard OIDC claims mapped to scopes.

### Test Seeded Data (Development/Test Only)
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

### Unit Tests
Run unit tests to verify individual components.
```powershell
dotnet test Core.Domain.UnitTests
dotnet test Core.Application.UnitTests
dotnet test Infrastructure.UnitTests
dotnet test Web.IdP.UnitTests
```

### System Tests
System tests verify the full integration using `TestServer`.
```powershell
dotnet test Web.IdP.SystemTests
```
*Note: System tests use an in-memory database and seeded M2M clients.*

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
1. Use the [Device Flow Console Client](file:///c:/repos/HybridIdP/HybridIdP.TestClient.Device/Program.cs).
2. Run the client:
   ```powershell
   dotnet run --project HybridIdP.TestClient.Device
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

