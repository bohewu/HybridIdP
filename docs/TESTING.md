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
**Last Updated**: 2024-12-10
