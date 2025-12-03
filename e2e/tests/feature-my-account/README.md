# My Account E2E Tests

Comprehensive end-to-end tests for the My Account feature using Playwright.

## Test Files

### 1. `my-account-navigation.spec.ts` (6 tests)
Tests navigation to My Account page through various entry points:
- Role badge in header
- User dropdown menu
- Page title verification
- Navigation accessibility

### 2. `my-account-role-switching.spec.ts` (11 tests)
Tests role switching functionality:
- Single role user behavior
- Multi-role user scenarios
- Password confirmation for Admin role
- Error handling for wrong passwords
- Modal interactions (cancel, confirm)

### 3. `my-account-ui-states.spec.ts` (15 tests)
Tests visual states and responsive design:
- Material Design styling verification
- Active/inactive role states
- Button visibility states
- Responsive layouts (mobile, tablet, desktop)
- Accessibility compliance

## Test Users

### Admin User (Single Role)
- **Username**: `admin@hybridauth.local`
- **Password**: `Admin@123`
- **Role**: Admin
- **Use for**: Basic navigation and UI state tests

### Multi-Role Test User
- **Username**: `multitest@hybridauth.local`
- **Password**: `MultiTest@123`
- **Roles**: Admin, User
- **Use for**: Role switching tests
- **Setup**: Run `setup-multi-role-test-user.sql` in root directory

## Prerequisites

1. **Database Setup**:
   ```powershell
   # Run from repository root
   sqlcmd -S localhost -d HybridIdP -i setup-multi-role-test-user.sql
   ```

2. **Start Services**:
   ```powershell
   # Terminal 1: IdP
   cd Web.IdP
   dotnet run --launch-profile https
   
   # Terminal 2: TestClient  
   cd TestClient
   dotnet run --launch-profile https
   ```

## Running Tests

```powershell
# From e2e/ directory

# Run all My Account tests
npx playwright test tests/feature-my-account/

# Run specific test file
npx playwright test tests/feature-my-account/my-account-navigation.spec.ts

# Run with headed browser (visual debugging)
npx playwright test tests/feature-my-account/ --headed

# Run with UI mode (interactive)
npx playwright test tests/feature-my-account/ --ui

# Run single test by name
npx playwright test tests/feature-my-account/ -g "should navigate to My Account page"
```

## Test Features

### Same-Role Switch Prevention
Tests verify that users cannot switch to their currently active role:
- **Frontend**: Switch button is hidden (`v-if="!role.isActive"`)
- **Backend**: Additional validation returns false if attempting same-role switch
- **Tests**: UI state tests verify button is not visible for active role

### Password Confirmation
Tests verify Admin role requires password confirmation:
- Password modal appears when switching to Admin role
- Correct password allows switch
- Wrong password shows error
- Cancel button closes modal without switching

### Visual States
Tests verify Material Design styling:
- Active role: Blue background (`#e8f0fe`), blue text (`#1967d2`)
- Active badge: Green success badge
- Hover effects on inactive roles
- Proper spacing: 16px vertical, 24px horizontal padding

### Responsive Design
Tests verify layout works on multiple viewports:
- Mobile: 375x667
- Tablet: 768x1024
- Desktop: 1920x1080

## Expected Behavior

### Active Role Card
- ✅ Blue background (#e8f0fe)
- ✅ Blue text color (#1967d2) for role name
- ✅ Green "Active" badge
- ✅ NO switch button visible
- ✅ Distinct visual separation from inactive roles

### Inactive Role Card
- ✅ White/light background
- ✅ Dark text color (#202124)
- ✅ Switch button visible and clickable
- ✅ Hover effect (light gray background)

### Role Switching
1. Click switch button on inactive role
2. If Admin role: Password modal appears
3. Enter password and confirm (or cancel)
4. Role badge updates in header
5. Page refreshes showing new active role

## Common Test Patterns

### Login Pattern
```typescript
test.beforeEach(async ({ page }) => {
  await page.goto('https://localhost:7035/Account/Login');
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');
  await page.click('button.auth-btn-primary');
  await page.waitForSelector('.user-name');
});
```

### Navigation to My Account
```typescript
await page.goto('https://localhost:7035/Account/MyAccount');
await page.waitForSelector('.role-card');
```

### Finding Active Role
```typescript
const activeRoleCard = page.locator('.role-card.active');
await expect(activeRoleCard).toBeVisible();
```

### Finding Inactive Role
```typescript
const inactiveRoleCards = page.locator('.role-card:not(.active)');
```

### Password Modal Interaction
```typescript
await switchButton.click();
await page.waitForSelector('.modal-overlay', { state: 'visible' });
await page.fill('#password', 'Password123');
await page.click('.modal-footer .btn-primary');
await page.waitForSelector('.modal-overlay', { state: 'hidden' });
```

## Troubleshooting

### Test Fails: "Element not found"
- Ensure IdP and TestClient are running
- Check if multi-role test user exists in database
- Verify role names match (Admin, User)

### Test Fails: "Wrong password"
- Password hash in SQL script may need regeneration
- Use actual password: `MultiTest@123`
- Check user exists: `SELECT * FROM AspNetUsers WHERE UserName = 'multitest@hybridauth.local'`

### Test Skipped
- Some tests skip if user has only one role
- Multi-role tests require `multitest@hybridauth.local` user
- Run `setup-multi-role-test-user.sql` to create user

### Timeout Errors
- Increase timeout in test: `{ timeout: 10000 }`
- Check if services are responding: `https://localhost:7035`
- Verify database connectivity

## Test Reports

After running tests:
- **HTML Report**: `../playwright-report/index.html`
- **Screenshots**: `../test-results/` (on failure)
- **Console**: Real-time pass/fail status

Open HTML report:
```powershell
npx playwright show-report
```

## Contributing

When adding new tests:
1. Follow existing test patterns
2. Use TypeScript with proper types
3. Use descriptive test names
4. Clean up test data after test
5. Make tests independent and atomic
6. Use `test.beforeEach()` for common setup
7. Add comments for complex test logic

## Related Documentation

- **Main E2E README**: `../README.md`
- **Phase 11.5 Prompt**: `../../docs/phase-11-5-e2e-tests-prompt.md`
- **Implementation Summary**: `../../docs/phase-11-5-implementation-summary.md`
- **Backend Implementation**: Phase 11.3 documentation
- **Frontend Implementation**: Phase 11.4 documentation
