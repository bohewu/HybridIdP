# Phase 11.5: My Account E2E Tests Implementation Prompt

## Context
You are implementing end-to-end (E2E) tests for the My Account feature (Phase 11.3 & 11.4) using Playwright. The tests should verify role switching, account switching, and password confirmation functionality.

## Project Structure Reference

### Test Location
- E2E tests are located in: `e2e/tests/`
- Page objects are in: `e2e/page-objects/`
- Test fixtures are in: `e2e/fixtures/`

### Existing Test Patterns to Follow
**CRITICAL**: Review these files for project conventions:
1. `e2e/tests/admin-clients.spec.ts` - Admin feature testing pattern
2. `e2e/tests/admin-users.spec.ts` - User management testing pattern
3. `e2e/page-objects/AdminClientsPage.ts` - Page object pattern
4. `e2e/page-objects/LoginPage.ts` - Authentication pattern
5. `e2e/fixtures/test-helpers.ts` - Helper utilities
6. `e2e/playwright.config.ts` - Playwright configuration

## Implementation Requirements

### 1. Page Object Model
Create `e2e/page-objects/MyAccountPage.ts`:

**Required Methods:**
```typescript
class MyAccountPage {
  // Navigation
  async navigate(): Promise<void>
  
  // Role Management
  async getRoles(): Promise<Array<{name: string, isActive: boolean, requiresPassword: boolean}>>
  async switchRole(roleName: string, password?: string): Promise<void>
  async getActiveRole(): Promise<string | null>
  
  // Account Management
  async getLinkedAccounts(): Promise<Array<{email: string, isCurrent: boolean}>>
  async switchAccount(email: string): Promise<void>
  
  // Password Modal
  async isPasswordModalVisible(): Promise<boolean>
  async enterPassword(password: string): Promise<void>
  async confirmPasswordModal(): Promise<void>
  async cancelPasswordModal(): Promise<void>
  
  // Verification
  async waitForPageLoad(): Promise<void>
  async hasError(): Promise<boolean>
  async getErrorMessage(): Promise<string | null>
}
```

### 2. Test Fixtures
Update `e2e/fixtures/test-helpers.ts` to include:
- Multi-role user creation (User, Admin, Manager roles)
- Person with multiple accounts setup
- Session cleanup utilities

### 3. Test Scenarios

#### Test File: `e2e/tests/my-account.spec.ts`

**Test Suite Structure:**
```typescript
describe('My Account - Role & Account Management', () => {
  describe('Role Switching', () => {
    test('should display all user roles')
    test('should show active role with badge')
    test('should switch to non-admin role without password')
    test('should require password for Admin role')
    test('should reject invalid password for Admin role')
    test('should update role badge after switching')
    test('should persist role across page refresh')
  })
  
  describe('Account Switching', () => {
    test('should display linked accounts when multiple exist')
    test('should hide linked accounts section when only one account')
    test('should show current account badge')
    test('should switch to another linked account')
    test('should redirect to home after account switch')
  })
  
  describe('Password Modal', () => {
    test('should show modal when switching to Admin role')
    test('should focus password input on modal open')
    test('should close modal on cancel')
    test('should submit on Enter key')
    test('should disable confirm button when password empty')
  })
  
  describe('UI/UX', () => {
    test('should show loading state while fetching data')
    test('should display error message on API failure')
    test('should be responsive on mobile viewport')
  })
  
  describe('Navigation', () => {
    test('should navigate from user dropdown menu')
    test('should navigate from role badge click')
  })
})
```

### 4. Test Data Setup

**Required Test Users:**
```typescript
// User with multiple roles
const multiRoleUser = {
  username: 'multirole.user',
  email: 'multirole@example.com',
  password: 'Test123!@#',
  roles: ['User', 'Admin', 'Manager']
}

// Person with multiple accounts
const personWithMultipleAccounts = {
  personId: 'guid',
  accounts: [
    { username: 'account1', email: 'account1@example.com', roles: ['User'] },
    { username: 'account2', email: 'account2@example.com', roles: ['User', 'Admin'] }
  ]
}
```

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
- **ALWAYS use TypeScript** for E2E tests (NOT JavaScript)
- Use strict typing for all variables and functions
- Define interfaces for test data structures

### Selectors
- Prefer `data-testid` attributes (add to components if needed)
- Use semantic selectors: `page.getByRole()`, `page.getByText()`, `page.getByLabel()`
- Avoid CSS selectors unless absolutely necessary

### Async/Await
- Always use `async/await` for Playwright actions
- Use `page.waitForSelector()` or `page.waitForLoadState()` appropriately
- Don't use arbitrary `page.waitForTimeout()` unless documenting why

### Test Organization
- One test file per feature
- Group related tests in `describe` blocks
- Use descriptive test names starting with "should"
- Keep tests independent and atomic

### Assertions
- Use Playwright's `expect()` from `@playwright/test`
- Use specific matchers: `toBeVisible()`, `toHaveText()`, `toContainText()`
- Add meaningful assertion messages for failures

### Authentication
- Use existing `LoginPage` page object
- Reuse authentication state with `storageState` when possible
- Clean up sessions after tests

### Error Handling
- Test both success and failure scenarios
- Verify error messages are user-friendly
- Test network failure scenarios with `page.route()`

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

## Known Issues to Handle

1. **Role Switch 404 Error**: Admin to Admin role switch returns 404 - add test to verify this and mark as known issue
2. **Session Management**: Ensure role switches update session correctly
3. **CSRF Tokens**: Verify CSRF token handling in POST requests
4. **i18n Support**: Test with both `zh-TW` and `en-US` locales

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

## Files to Create/Modify

1. **Create**: `e2e/page-objects/MyAccountPage.ts`
2. **Create**: `e2e/tests/my-account.spec.ts`
3. **Update**: `e2e/fixtures/test-helpers.ts` (add multi-role user helpers)
4. **Update**: Vue components (add `data-testid` attributes if needed)

## Testing Checklist

Before considering implementation complete:
- [ ] All tests pass locally
- [ ] Tests are deterministic (no random failures)
- [ ] Page object follows existing patterns
- [ ] TypeScript types are properly defined
- [ ] Test data is cleaned up after runs
- [ ] Both zh-TW and en-US locales work
- [ ] Mobile responsive tests included
- [ ] Error scenarios covered
- [ ] Authentication flows work correctly
- [ ] Known 404 issue documented in test

## Additional Notes

- Run tests with: `npm run test:e2e` from `e2e/` directory
- Run in UI mode for debugging: `npm run test:e2e:ui`
- Check Playwright docs: https://playwright.dev/
- Review test reports in `e2e/playwright-report/`
- Screenshots saved to `e2e/test-results/` on failure

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
