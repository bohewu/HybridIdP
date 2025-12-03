# Quick Start: My Account E2E Tests

## 1. Setup Multi-Role Test User (One-Time)

```powershell
# Run this SQL script to create test user with Admin + User roles
sqlcmd -S localhost -d HybridIdP -i setup-multi-role-test-user.sql

# Or execute in your SQL client
```

**Test User Credentials**:
- Username: `multitest@hybridauth.local`
- Password: `MultiTest@123`
- Roles: Admin, User

## 2. Start Services

```powershell
# Terminal 1: IdP
cd Web.IdP
dotnet run --launch-profile https

# Terminal 2: TestClient
cd TestClient
dotnet run --launch-profile https
```

## 3. Run Tests

```powershell
cd e2e

# Install dependencies (first time only)
npm install

# Run all My Account tests
npx playwright test tests/feature-my-account/

# Run with visual browser
npx playwright test tests/feature-my-account/ --headed

# Run specific test file
npx playwright test tests/feature-my-account/my-account-navigation.spec.ts
```

## What's Tested?

### ✅ Navigation (6 tests)
- Role badge click → My Account page
- User dropdown → My Account page
- Page title display

### ✅ Role Switching (11 tests)
- Display all roles
- Active role identification
- Switch between roles
- Password confirmation for Admin
- Error handling

### ✅ UI States (15 tests)
- Material Design styling
- Active/inactive role colors
- Button visibility (hidden for current role)
- Responsive layouts

**Total: 32 E2E Tests**

## Key Files

- **Tests**: `e2e/tests/feature-my-account/*.spec.ts`
- **Setup**: `setup-multi-role-test-user.sql`
- **Docs**: `docs/phase-11-5-implementation-summary.md`

## Need Help?

- **Test README**: `e2e/tests/feature-my-account/README.md`
- **E2E Setup**: `e2e/README.md`
- **Full Docs**: `docs/phase-11-5-e2e-tests-prompt.md`
