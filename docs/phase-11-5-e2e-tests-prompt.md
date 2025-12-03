# Phase 11.5: My Account E2E Tests Implementation Prompt

## Context
You are implementing end-to-end (E2E) tests for the My Account feature (Phase 11.3 & 11.4) using Playwright. The tests should verify role switching, account switching, and password confirmation functionality.

## Project Structure Reference

### Test Location and Structure
```
e2e/
├── tests/
│   ├── feature-auth/          # Authentication tests (login, logout, consent)
│   ├── feature-clients/       # OIDC clients CRUD tests
│   ├── feature-users/         # User management tests
│   ├── feature-roles/         # Role management tests
│   ├── feature-people/        # Person & account linking tests
│   ├── feature-sessions/      # Session management tests
│   ├── feature-my-account/    # ← CREATE THIS for My Account tests
│   ├── page-objects/          # Page object classes
│   └── helpers/               # Test helper utilities
├── playwright.config.ts       # Playwright configuration
├── package.json
└── README.md                  # E2E setup and running instructions
```

### Existing Test Patterns to Follow
**CRITICAL**: Review these files for project conventions:
1. `e2e/tests/feature-auth/login.spec.ts` - Simple authentication test pattern
2. `e2e/tests/feature-users/admin-users-crud.spec.ts` - CRUD operations pattern
3. `e2e/tests/feature-users/admin-users-role-assignment.spec.ts` - Role management pattern
4. `e2e/tests/feature-people/admin-people-account-linking.spec.ts` - Account linking pattern
5. `e2e/tests/feature-sessions/user-sessions-management.spec.ts` - Session operations pattern
6. `e2e/tests/page-objects/PeoplePage.ts` - Page object pattern example
7. `e2e/playwright.config.ts` - Playwright configuration
8. `e2e/README.md` - E2E test setup and conventions

## Implementation Requirements

### 1. Page Object Model (Optional)
Create `e2e/tests/page-objects/MyAccountPage.ts` if complex interactions are needed:

**Note**: Based on existing tests like `login.spec.ts`, many tests interact directly with page selectors without page objects. Use page objects only if the My Account page has complex reusable interactions.

**If creating page object, include these methods:**
```typescript
export class MyAccountPage {
  constructor(private page: Page) {}
  
  // Navigation
  async navigate(): Promise<void> {
    await this.page.goto('https://localhost:7035/Account/MyAccount');
    await this.page.waitForSelector('.page-title');
  }
  
  // Role list selectors
  getRoleCards() {
    return this.page.locator('.role-card');
  }
  
  getRoleByName(roleName: string) {
    return this.page.locator('.role-card').filter({ hasText: roleName });
  }
  
  // Account list selectors  
  getAccountCards() {
    return this.page.locator('.account-card');
  }
  
  // Actions
  async switchRole(roleName: string, password?: string): Promise<void> {
    const roleCard = this.getRoleByName(roleName);
    await roleCard.locator('.btn-primary').click();
    
    if (password) {
      await this.page.waitForSelector('.modal-overlay');
      await this.page.fill('#password', password);
      await this.page.click('.modal-footer .btn-primary');
    }
  }
}
```

### 2. Test Data Setup
Based on `e2e/README.md`, use existing test user:
- Admin user: `admin@hybridauth.local` / `Admin@123`

For multi-role testing, you may need to:
- Create additional test users via Admin UI or SQL scripts
- Use existing PowerShell scripts in repo root for user creation
- Reference `cleanup-e2e-test-data.ps1` and `setup-test-user.ps1`

### 3. Test Scenarios

#### Test File Structure: `e2e/tests/feature-my-account/`
Create folder and test files following existing pattern:
- `my-account-role-switching.spec.ts` - Role switching tests
- `my-account-account-switching.spec.ts` - Account switching tests  
- `my-account-navigation.spec.ts` - Navigation and UI tests

**Example Test Structure** (following `login.spec.ts` pattern):

File: `e2e/tests/feature-my-account/my-account-role-switching.spec.ts`
```typescript
import { test, expect } from '@playwright/test';

test.describe('My Account - Role Switching', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should navigate to My Account page', async ({ page }) => {
    // Click user dropdown
    await page.click('#userDropdown');
    
    // Click My Account link
    await page.click('a[href="/Account/MyAccount"]');
    
    // Verify we're on My Account page
    await expect(page).toHaveURL('https://localhost:7035/Account/MyAccount');
    await expect(page.locator('.page-title')).toContainText('我的帳戶');
  });

  test('should display all user roles', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Verify role cards are displayed
    const roleCards = page.locator('.role-card');
    await expect(roleCards).toHaveCount(1); // Admin user has Admin role
  });

  // Add more tests following this pattern...
});
```

File: `e2e/tests/feature-my-account/my-account-navigation.spec.ts`
```typescript
import { test, expect } from '@playwright/test';

test.describe('My Account - Navigation', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should show role badge in navigation', async ({ page }) => {
    // Verify role badge is visible
    await expect(page.locator('.role-badge')).toBeVisible();
    await expect(page.locator('.role-badge')).toContainText('Admin');
  });

  test('should navigate via role badge click', async ({ page }) => {
    // Click on role badge
    await page.click('.role-badge');
    
    // Should navigate to My Account
    await expect(page).toHaveURL('https://localhost:7035/Account/MyAccount');
  });
});
```

### 4. Test Data Setup

**Default Test User (from e2e/README.md):**
- Username: `admin@hybridauth.local`
- Password: `Admin@123`
- Role: `Admin`

**Multi-Role Test User (for role switching tests):**
- Username: `multitest@hybridauth.local`
- Password: `MultiTest@123`
- Roles: `Admin`, `User`
- Setup: Run `setup-multi-role-test-user.sql` script in the root directory

**For Multi-Role Testing:**
The `setup-multi-role-test-user.sql` script creates a test user with both Admin and User roles, allowing comprehensive testing of:
- Role switching between different roles
- Password confirmation for Admin role
- Direct switching for non-Admin roles
- UI states for active/inactive roles

**For Multi-Account Testing:**
Reference `e2e/tests/feature-people/admin-people-account-linking.spec.ts` for account linking patterns.

## Backend API Endpoints Reference
Review these implementations for test expectations:
- `Web.IdP/Controllers/MyAccountController.cs` - API endpoints
- Phase 11.3 documentation: `docs/phase-11-3-backend-implementation.md`

**Key Endpoints:**
- `GET /api/my/roles` - Get user roles
- `POST /api/my/switch-role` - Switch active role
- `GET /api/my/accounts` - Get linked accounts
- `POST /api/my/switch-account` - Switch to another account

## Frontend Components Reference
Review these for selector patterns:
- `Web.IdP/ClientApp/src/AccountManagementApp.vue` - Main component
- `Web.IdP/ClientApp/src/components/account/RoleList.vue` - Role list
- `Web.IdP/ClientApp/src/components/account/AccountList.vue` - Account list
- `Web.IdP/ClientApp/src/components/account/PasswordModal.vue` - Password modal
- `Web.IdP/ClientApp/src/components/navigation/RoleBadge.vue` - Role badge

## Project Conventions (CRITICAL - MUST FOLLOW)

### TypeScript
- **ALWAYS use TypeScript** for E2E tests (`.spec.ts` files)
- Use strict typing for all variables and functions
- Import from `@playwright/test`: `import { test, expect } from '@playwright/test';`

### Test File Naming
- Place tests in appropriate `feature-*` folders: `e2e/tests/feature-my-account/`
- Name files descriptively: `my-account-role-switching.spec.ts`
- Use `.spec.ts` extension (NOT `.test.ts`)

### Selectors (Following Existing Patterns)
Based on `login.spec.ts` and other existing tests:
- Use CSS class selectors: `.user-name`, `.role-badge`, `.page-title`
- Use ID selectors for form inputs: `#Input_Login`, `#Input_Password`
- Use element + class: `button.auth-btn-primary`
- Use attribute selectors: `a[href="/Account/MyAccount"]`
- Add `data-testid` attributes to Vue components if needed for complex scenarios

### Async/Await
- Always use `async/await` for Playwright actions
- Use `page.waitForSelector()` to wait for elements: `await page.waitForSelector('.user-name')`
- Use `page.waitForLoadState()` when needed
- Avoid `page.waitForTimeout()` unless absolutely necessary

### Test Organization
- Use `test.describe()` for grouping related tests
- Use `test.beforeEach()` for common setup (like login)
- Use `test.afterEach()` for cleanup if needed
- Keep tests independent and atomic
- Use descriptive test names: `test('should display all user roles', ...)`

### Assertions
- Use Playwright's `expect()` from `@playwright/test`
- Common matchers:
  - `await expect(page).toHaveURL('...')`
  - `await expect(locator).toBeVisible()`
  - `await expect(locator).toContainText('...')`
  - `await expect(locator).toHaveCount(n)`
- Add timeout if needed: `{ timeout: 20000 }`

### Authentication
- Login in `beforeEach` for authenticated tests
- Standard login flow:
  ```typescript
  await page.goto('https://localhost:7035/Account/Login');
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');
  await page.click('button.auth-btn-primary');
  await page.waitForSelector('.user-name');
  ```

### URLs
- IdP: `https://localhost:7035`
- TestClient: `https://localhost:7001`
- Use full URLs in tests: `await page.goto('https://localhost:7035/Account/MyAccount')`

## Existing Project Patterns

### Page Object Pattern
```typescript
export class MyPage {
  constructor(private page: Page) {}
  
  // Locators as getters
  get submitButton() {
    return this.page.getByRole('button', { name: 'Submit' });
  }
  
  // Actions as methods
  async clickSubmit(): Promise<void> {
    await this.submitButton.click();
  }
}
```

### Test Structure
```typescript
test.describe('Feature Name', () => {
  test.beforeEach(async ({ page }) => {
    // Setup
  });
  
  test('should do something', async ({ page }) => {
    // Arrange
    // Act
    // Assert
  });
  
  test.afterEach(async ({ page }) => {
    // Cleanup
  });
});
```

## Database & Test Data

### Database Access
- Use SQL scripts in root directory for manual setup if needed
- Tests should be isolated and not depend on specific DB state
- Clean up test data after test runs

### Test User Creation
Reference existing patterns in:
- `cleanup-e2e-test-data.ps1` - Cleanup script
- `setup-test-user.ps1` - User creation script

## Requirements and Business Rules

### Same-Role Switch Prevention
**Requirement**: Users cannot switch to their currently active role.

**Implementation**:
1. **Frontend (UI)**: Switch button is hidden for the current active role using `v-if="!role.isActive"` in `RoleList.vue`
   - Active role shows blue background (`#e8f0fe`) and "Active" badge
   - No switch button is rendered for the active role
   
2. **Backend (API)**: Additional validation in `AccountManagementService.SwitchRoleAsync()`
   - Checks if `oldRoleId == roleId` before processing
   - Returns `false` and logs warning if attempting same-role switch
   - Prevents unnecessary session updates and audit logs

**Test Coverage**:
- UI state tests verify switch button is not visible for active role
- Backend validation prevents API abuse even if UI is bypassed

## Known Issues to Handle

1. **Session Management**: Ensure role switches update session correctly
2. **CSRF Tokens**: Verify CSRF token handling in POST requests
3. **i18n Support**: Test with both `zh-TW` and `en-US` locales

## Success Criteria

Your E2E tests should:
- ✅ Cover all happy path scenarios
- ✅ Test error handling and edge cases
- ✅ Be reliable and not flaky
- ✅ Run in CI/CD pipeline
- ✅ Follow existing project patterns exactly
- ✅ Use TypeScript with proper typing
- ✅ Include meaningful test names and assertions
- ✅ Clean up test data properly

## Files Created/Modified

### ✅ Completed

1. **Created**: `e2e/tests/feature-my-account/` (new folder)
2. **Created**: `e2e/tests/feature-my-account/my-account-navigation.spec.ts` - Navigation tests (role badge, user dropdown)
3. **Created**: `e2e/tests/feature-my-account/my-account-role-switching.spec.ts` - Role switching tests with password confirmation
4. **Created**: `e2e/tests/feature-my-account/my-account-ui-states.spec.ts` - UI states and styling tests
5. **Created**: `setup-multi-role-test-user.sql` - SQL script to create test user with multiple roles
6. **Updated**: `Infrastructure/Services/AccountManagementService.cs` - Added same-role switch prevention
7. **Updated**: `docs/phase-11-5-e2e-tests-prompt.md` - Documented requirements and implementation

### Optional (Not Required)
- `e2e/tests/page-objects/MyAccountPage.ts` - Not needed, direct selector approach works well
- `e2e/tests/feature-my-account/my-account-account-switching.spec.ts` - Future enhancement for multi-account feature

## Testing Checklist

Before considering implementation complete:
- [x] Tests are in correct folder structure: `e2e/tests/feature-my-account/`
- [x] Test files use `.spec.ts` extension
- [x] TypeScript types are properly defined
- [x] Follow existing selector patterns from other tests
- [x] Error scenarios covered
- [x] Authentication flows work correctly
- [x] Same-role switch prevention implemented and tested (backend + frontend)
- [x] Multi-role test user setup script created (`setup-multi-role-test-user.sql`)
- [x] UI state tests for active/inactive roles
- [x] Password confirmation flow tested for Admin role
- [x] Navigation tests via role badge and user dropdown
- [ ] All tests pass locally with `npm test` (requires running IdP and TestClient)
- [ ] Tests are deterministic (no random failures)
- [ ] Both zh-TW and en-US locales work (test text content accordingly)

## Running Tests

From `e2e/` directory:

```powershell
# Ensure IdP and TestClient are running first
# Terminal 1: cd Web.IdP; dotnet run --launch-profile https
# Terminal 2: cd TestClient; dotnet run --launch-profile https

# Install dependencies
npm install

# Run all tests (headless)
npm test

# Run specific feature tests
npx playwright test tests/feature-my-account/

# Run single test file
npx playwright test tests/feature-my-account/my-account-role-switching.spec.ts

# Run with headed browser (for debugging)
npm run test:headed

# Or specific test headed
npx playwright test tests/feature-my-account/my-account-navigation.spec.ts --headed

# Run with UI mode
npx playwright test --ui
```

## Additional Notes

- Review `e2e/README.md` for complete setup instructions
- Check Playwright docs: https://playwright.dev/
- Test reports in `e2e/playwright-report/`
- Screenshots on failure in `e2e/test-results/`
- Use `scripts/start-e2e-dev.ps1` helper to start services automatically

## Important References

**Backend Implementation:**
- Phase 11.3 docs: `docs/phase-11-3-backend-implementation.md`
- Controller: `Web.IdP/Controllers/MyAccountController.cs`
- Services: `Core.Application/IAccountManagementService.cs`
- DTOs: `Core.Application/DTOs/RoleDto.cs`, `LinkedAccountDto.cs`

**Frontend Implementation:**
- Phase 11.4 docs: `docs/phase-11-4-ui-implementation-prompt.md`
- Main app: `Web.IdP/ClientApp/src/AccountManagementApp.vue`
- Components: `Web.IdP/ClientApp/src/components/account/*.vue`
- API service: `Web.IdP/ClientApp/src/services/accountApi.js`

**Existing E2E Examples:**
- Admin tests: `e2e/tests/admin-*.spec.ts`
- Page objects: `e2e/page-objects/*.ts`
- Config: `e2e/playwright.config.ts`

## Questions to Ask Before Implementation

1. What test users and roles already exist in the test database?
2. Are there existing helpers for multi-role user creation?
3. What is the current authentication flow in E2E tests?
4. Should tests use API mocking or real backend calls?
5. What is the CI/CD pipeline configuration for E2E tests?

## Common Pitfalls to Avoid

❌ **Don't**:
- Use JavaScript instead of TypeScript
- Write flaky tests with arbitrary waits
- Hard-code test data without cleanup
- Skip error scenario testing
- Ignore existing page object patterns
- Use fragile CSS selectors
- Create test dependencies (tests affecting each other)

✅ **Do**:
- Follow existing TypeScript patterns
- Use Playwright's built-in waiting mechanisms
- Create isolated, independent tests
- Test both success and failure paths
- Reuse existing page objects and helpers
- Use semantic selectors
- Clean up test data properly

---

## Summary

This prompt should guide you to implement E2E tests that:
1. Follow the exact same patterns as existing tests
2. Use TypeScript properly
3. Test all critical My Account functionality
4. Handle errors gracefully
5. Are maintainable and reliable

**Start by reviewing the existing test files mentioned above before writing any code.**
