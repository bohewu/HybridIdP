# E2E Tests (Playwright) - Hybrid Testing Strategy

## Overview

This folder contains Playwright-based E2E tests for HybridIdP using a **Hybrid Testing Strategy**:
- **API** for test setup and teardown (fast, reliable)
- **UI** for critical user flows and visual validation

## Quick Start

### Prerequisites
- Node.js 18+
- Apps running locally:
  - IdP: `https://localhost:7035`
  - TestClient: `https://localhost:7001`
  - Vite dev server: `http://localhost:5173` (optional, for UI tests)

### Install & Run
```powershell
cd e2e
npm install
npm run install:browsers
npm test                # Headless
npm run test:headed     # Headed (for debugging)
```

## Hybrid Testing Pattern

### Core Principles
1. **Global Auth**: Authenticate once in `global-setup.ts`, reuse via storage state
2. **API for Setup/Teardown**: Use `api` fixture for creating/deleting test data
3. **UI for Verification**: Test critical user flows and visual elements via UI
4. **Dynamic Data**: Zero pre-existing data dependencies - create what you need

### Example Test
```typescript
import { test, expect } from './fixtures';

test('Create and verify user', async ({ page, api }) => {
  // 1. Setup (API)
  const user = await api.users.create({
    email: 'test@example.com',
    userName: 'test@example.com',
    firstName: 'Test',
    lastName: 'User',
    password: 'Test@123'
  });

  // 2. Navigate and verify (UI)
  await page.goto('https://localhost:7035/Admin/Users');
  
  // 3. Assert (data-test-id for reliability)
  await expect(page.locator(`[data-test-id="user-row-${user.id}"]`))
    .toBeVisible();

  // 4. Cleanup (API)
  await api.users.deleteUser(user.id);
});
```

## Project Structure

```
e2e/
├── tests/
│   ├── fixtures.ts              # Custom fixtures (api, page)
│   ├── global-setup.ts          # One-time admin authentication
│   ├── helpers/
│   │   ├── api-client.ts        # API client (UsersApi, RolesApi, etc.)
│   │   └── admin.ts             # Legacy UI helpers (being phased out)
│   └── feature-*/               # Test files organized by feature
├── .auth/
│   └── admin.json               # Saved admin authentication state
└── playwright.config.ts
```

## API Clients

### Available APIs
- **`api.users`**: Create, list, delete users
- **`api.roles`**: Create, list, delete roles
- **`api.clients`**: Create, delete clients

### Example Usage
```typescript
// Create user
const user = await api.users.create({
  email: 'user@example.com',
  userName: 'user@example.com',
  firstName: 'Test',
  lastName: 'User',
  password: 'Test@123'
});

// Create role with permissions
const role = await api.roles.create(
  'test-role',
  'Test Role',
  ['users.read', 'users.update']
);

// Cleanup
await api.users.deleteUser(user.id);
await api.roles.deleteRole(role.id);
```

## UI Testing Best Practices

### Use data-test-id for Selectors
```typescript
// ✅ Good - Reliable, semantic
await page.locator('[data-test-id="user-row-123"]').click();
await page.locator('[data-test-id="save-btn"]').click();

// ❌ Bad - Fragile, implementation-dependent
await page.locator('.user-table tr:nth-child(3)').click();
await page.locator('button.btn-primary').click();
```

### Test Structure (AAA Pattern)
```typescript
test('descriptive test name', async ({ page, api }) => {
  // Arrange (Setup via API)
  const testData = await api.createTestData();
  
  // Act (Interact via UI)
  await page.goto('/target-page');
  await page.click('[data-test-id="action"]');
  
  // Assert (Verify via UI or API)
  await expect(page.locator('[data-test-id="result"]'))
    .toContainText('Expected');
  
  // Cleanup (Teardown via API)
  await api.deleteTestData(testData.id);
});
```

## Running Tests

### Run All Tests
```powershell
npx playwright test
```

### Run Specific Feature
```powershell
npx playwright test tests/feature-users/
npx playwright test tests/feature-roles/
```

### Run Single Test
```powershell
npx playwright test tests/feature-users/admin-users-crud.spec.ts
```

### Run with UI (Headed Mode)
```powershell
npx playwright test --headed
npx playwright test tests/feature-users/ --headed
```

### Debug Mode
```powershell
npx playwright test --debug
npx playwright test tests/feature-users/admin-users-crud.spec.ts --debug
```

## Troubleshooting

### Tests Failing on Login
- Check that apps are running (IdP at 7035, TestClient at 7001)
- Verify `.auth/admin.json` exists (created by global-setup)
- Ensure admin user exists: `admin@hybridauth.local` / `Admin@123`

### "Already Exists" Errors
- Tests use dynamic timestamps for unique data
- Check if previous test run left orphaned data
- Verify cleanup code is running (check test afterEach hooks)

### Slow Tests
- Use API for setup/teardown instead of UI
- Avoid unnecessary `page.waitForTimeout()` - use `waitForSelector` instead
- Run tests in parallel with `--workers=4`

### HTTPS Certificate Errors
- Config already sets `ignoreHTTPSErrors: true`
- If still failing, check that IdP/TestClient are running with HTTPS

## Test Organization

### Feature-based Folders
```
tests/
├── feature-auth/         # Auth flows, login, consent
├── feature-users/        # User management
├── feature-roles/        # Role management
├── feature-clients/      # Client management
├── feature-people/       # Person/identity management
└── feature-impersonation/# Impersonation flows
```

### Naming Conventions
- Test files: `feature-name.spec.ts` or `admin-feature-name.spec.ts`
- Test descriptions: `'Should do X when Y'` or `'Feature does X'`
- Dynamic data: Use timestamps `e2e-user-${Date.now()}@example.com`

## CI/CD Integration

### GitHub Actions Example
```yaml
- name: Run E2E Tests
  run: |
    npm ci
    npx playwright install --with-deps
    npm test
  working-directory: ./e2e
```

### Parallel Execution
```powershell
# Run with 4 workers (faster in CI)
npx playwright test --workers=4

# Or configure in playwright.config.ts
fullyParallel: true
```

## Migration from Legacy Tests

### Old Pattern (Avoid)
```typescript
// ❌ Old: UI for everything
test('old way', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);
  await page.click('.nav-users');
  await page.click('.btn-create');
  await page.fill('#email', 'test@example.com');
  await page.click('.btn-save');
  // ... many more clicks ...
  await page.click('.btn-delete');
});
```

### New Pattern (Recommended)
```typescript
// ✅ New: API for setup/teardown, UI for verification
test('new way', async ({ page, api }) => {
  const user = await api.users.create({ /* ... */ });
  
  await page.goto('https://localhost:7035/Admin/Users');
  await expect(page.locator(`[data-test-id="user-row-${user.id}"]`))
    .toBeVisible();
  
  await api.users.deleteUser(user.id);
});
```

## Common Test Scenarios

### Create and Verify User
```typescript
test('User appears in admin list', async ({ page, api }) => {
  const user = await api.users.create({
    email: `test-${Date.now()}@example.com`,
    userName: `test-${Date.now()}@example.com`,
    firstName: 'Test',
    lastName: 'User',
    password: 'Test@123'
  });

  await page.goto('https://localhost:7035/Admin/Users');
  await expect(page.locator(`[data-test-id="user-row-${user.id}"]`))
    .toBeVisible();

  await api.users.deleteUser(user.id);
});
```

### Assign Role via UI
```typescript
test('Assign role via UI', async ({ page, api }) => {
  const role = await api.roles.create('test-role', 'Test', ['users.read']);
  const user = await api.users.create({ /* ... */ });

  await page.goto('https://localhost:7035/Admin/Users');
  const userRow = page.locator(`[data-test-id="user-row-${user.id}"]`);
  await userRow.locator('[data-test-id="assign-role-btn"]').click();
  
  await page.check(`[data-test-id="role-${role.id}"]`);
  await page.click('[data-test-id="save-btn"]');

  await expect(userRow).toContainText(role.name);

  await api.users.deleteUser(user.id);
  await api.roles.deleteRole(role.id);
});
```

## Resources

- [Playwright Documentation](https://playwright.dev)
- [Phase 19 E2E Refactoring](../docs/PHASE_19_E2E_REFACTORING.md)
- `tests/fixtures.ts` - Custom fixture definitions
- `tests/helpers/api-client.ts` - API client implementation

## Test Data Credentials

- **Admin**: `admin@hybridauth.local` / `Admin@123`
- **TestClient ID**: `testclient-public`
- **Required Scopes**: `openid`, `profile`, `email`, `roles`

---

**Last Updated**: 2025-12-13 (Phase 19.5 - Hybrid Testing Strategy)
