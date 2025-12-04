# E2E Test Data Cleanup Guide

## Overview
E2E tests create test data (users, persons, clients, scopes, claims) that may accumulate over time. Before running tests, it's recommended to clean up old test data to ensure a clean state.

## Cleanup Scripts

### 1. Clean Users and Persons (MSSQL)
```powershell
cd C:\repos\HybridIdP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-users-mssql.ps1
```

**What it does:**
- Removes all non-admin users from `AspNetUsers` table
- Removes all non-admin persons from `Persons` table
- Preserves the admin user (`admin@hybridauth.local`)
- Shows count of remaining users and persons

**When to use:**
- Before running People CRUD tests
- Before running User management tests
- Before running Identity Verification tests
- When you see "Person not found" or "User already exists" errors

### 2. Clean Claims (MSSQL)
```powershell
cd C:\repos\HybridIdP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-claims-mssql.ps1
```

**What it does:**
- Removes all test claims with `e2e_` prefix from `UserClaims` table
- Cleans up related `ScopeClaims` references first
- Shows count of claims before and after cleanup

**When to use:**
- Before running Claims CRUD tests
- When you see "Claim already exists" errors
- To remove accumulated test claim data

### 3. Clean Roles (MSSQL)
```powershell
cd C:\repos\HybridIdP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-roles-mssql.ps1
```

**What it does:**
- Removes all test roles with `e2e_` prefix from `AspNetRoles` table
- Cleans up related `AspNetRoleClaims` and `AspNetUserRoles` first
- Shows count of roles before and after cleanup

**When to use:**
- Before running Role management tests
- When you see "Role already exists" errors
- To remove accumulated test role data

### 4. Clean Users and Persons (PostgreSQL)
```powershell
cd C:\repos\HybridIdP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-users-postgres.ps1
```

### 3. Clean Claims (Manual - No script yet)
Currently there's no dedicated cleanup script for Claims. To clean test claims:

**Option A: Via SQL (MSSQL)**
```sql
-- Connect to hybridauth_idp database
DELETE FROM Claims WHERE Name LIKE 'e2e_%';
```

**Option B: Via Admin UI**
1. Navigate to https://localhost:7035/Admin/Claims
2. Search for "e2e_" prefix
3. Manually delete test claims

**TODO: Create cleanup-claims.ps1 script**

### 4. Clean Clients (Partial - via test helpers)
Test helpers in `e2e/tests/helpers/admin.ts` provide cleanup functions:
- `deleteClientViaApiFallback()` - Delete individual client
- Most client tests include cleanup in `afterEach` hooks

**Manual cleanup if needed:**
```sql
-- Connect to hybridauth_idp database
DELETE FROM Clients WHERE ClientId LIKE 'e2e-%';
DELETE FROM ClientScopes WHERE ClientId IN (SELECT Id FROM Clients WHERE ClientId LIKE 'e2e-%');
```

### 5. Clean Scopes (via test data accumulation)
Scopes created by tests (e.g., `e2e-csm-*`) accumulate but generally don't cause issues.

**Manual cleanup if needed:**
```sql
-- Connect to hybridauth_idp database
DELETE FROM Scopes WHERE Name LIKE 'e2e-%';
DELETE FROM ClientScopes WHERE ScopeId IN (SELECT Id FROM Scopes WHERE Name LIKE 'e2e-%');
```

## Recommended Cleanup Workflow

### Before Running Full Test Suite
```powershell
# 1. Clean users and persons
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-users-mssql.ps1

# 2. Optional: Clean claims manually (see above)

# 3. Run tests
cd e2e
npx playwright test --workers=1
```

### Before Running Specific Test Categories

**People/Identity Tests:**
```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\cleanup-users-mssql.ps1
cd e2e
npx playwright test tests/feature-people/ --workers=1
```

**Claims Tests:**
```powershell
# Clean claims manually (SQL or Admin UI)
cd e2e
npx playwright test tests/feature-claims/ --workers=1
```

**Clients Tests:**
```powershell
# Usually self-cleaning via afterEach hooks
cd e2e
npx playwright test tests/feature-clients/ --workers=1
```

## Database Connection Strings

The cleanup scripts use connection strings from environment or default to:

**MSSQL:**
- Server: `mssql-service` or `localhost`
- Database: `hybridauth_idp`
- Integrated Security: Yes

**PostgreSQL:**
- Server: `localhost`
- Database: `hybridauth_idp`
- Username: `postgres`
- Password: (from environment or default)

## Future Improvements

### TODO: Create cleanup-claims.ps1
Should clean:
- Claims table entries with `Name LIKE 'e2e_%'`
- Related ClaimValues if applicable

### TODO: Create cleanup-all.ps1
A master script that calls all cleanup scripts in order:
1. cleanup-users-mssql.ps1 (or postgres)
2. cleanup-claims.ps1 (when created)
3. Optional: cleanup old clients/scopes

### TODO: Integrate cleanup into test workflow
Consider adding cleanup to:
- `scripts/run-e2e.ps1` with a `-CleanData` flag
- GitHub Actions workflow before test execution
- Pre-test hooks in Playwright config

## Troubleshooting

### "Person not found after creation"
- Run `cleanup-users-mssql.ps1` to clear stale data
- Check if search/pagination was reset after creation

### "User already exists"
- Run `cleanup-users-mssql.ps1` to remove test users
- Ensure test usernames have unique timestamps

### "Scope not found"
- Scopes may be created dynamically by tests
- Check `e2e/tests/helpers/admin.ts` scope creation logic
- Verify scope seeding in database

### Database connection issues
- Verify SQL Server is running
- Check connection string in cleanup scripts
- Ensure user has DELETE permissions on tables
