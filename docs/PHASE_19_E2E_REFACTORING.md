# Phase 19: E2E Test Refactoring Strategy (Hybrid Testing)

## Problem Statement
Current E2E tests are slow, flaky, and prone to data conflicts. We need to shift to a **Hybrid Testing Strategy** (UI for flows, API for setup) and ensure **Idempotency**.

## Core Principles
1.  **Hybrid Testing**: Use API for "Arrange" and "Teardown", UI only for "Act" and "Assert" of visual flows.
2.  **Global Auth**: Authenticate once via `global-setup`, reuse state.
3.  **Dynamic Data**: Zero dependency on pre-existing data. Create what you need, delete when done.
4.  **Parallelism**: All tests must be able to run in parallel (`fullyParallel: true`).

## Execution Plan (Phased)

### Phase 19.1: Framework Foundation & Auth ✅
**Goal**: Establish the infrastructure for hybrid testing.
-   [x] **Config**: Update `playwright.config.ts` to support global setup and storage state.
-   [x] **Global Setup**: Implement `global-setup.ts` to save Admin auth state to `.auth/admin.json`.
-   [x] **API Client**: Create `e2e/tests/helpers/api-client.ts` with type-safe API clients.
-   [x] **Fixtures**: Create `e2e/tests/fixtures.ts` with `api` context for hybrid tests.

### Phase 19.2: User Management (Core) ✅
**Goal**: Refactor the most data-heavy feature to prove the pattern.
-   [x] **API Helpers**: `UsersApi` in `api-client.ts` for create/delete users, assign roles.
-   [x] **Refactor Tests**: Rewrote `feature-users` specs to hybrid pattern.
    -   *Old*: 5 files, ~1000+ lines, many `page.evaluate(fetch())` calls.
    -   *New*: 5 files, ~400 lines, clean `api` fixture usage.

### Phase 19.3: People & Identity Verification
**Goal**: Handle complex data relationships (Person <-> User <-> Identity).
-   [ ] **API Helpers**: Add `PeopleApi` for Person CRUD and `IdentityApi` for verification simulation.
-   [ ] **Refactor Tests**: Rewrite `feature-people`.
    -   Pre-seed "Verified Person" via API before testing Account Linking UI.
    -   Ensure `admin-people-account-linking.spec.ts` is fully idempotent.

### Phase 19.4: Clients, Resources & Configuration
**Goal**: Refactor configuration-heavy tests.
-   [ ] **API Helpers**: Add `ClientsApi` and `ResourcesApi`.
-   [ ] **Refactor Tests**: Rewrite `feature-clients` and `feature-resources` to manage test data entirely via API.

### Phase 19.5: Cleanup & CI Optimization
**Goal**: Final polish and performance tuning.
-   [ ] **Delete Legacy Scripts**: Remove all `.ps1`, `.txt` outputs, and the `scripts/` folder.
    -   *Reason*: Data seeding is handled by `DataSeeder` and dynamic API helpers.
    -   *Targets*: `e2e/*.ps1`, `e2e/*.txt`, `scripts/*`.
-   [ ] **Remove Legacy Helpers**: Deprecate and remove `e2e/tests/helpers/admin.ts` (UI helpers).
-   [ ] **CI Integration**: Ensure new suite runs in GitHub Actions with proper reporting.
-   [ ] **Documentation**: Update `e2e/README.md` with new patterns.

## Refactoring Pattern Example

**Legacy (Slow/Flaky):**
```typescript
test('Create User', async ({ page }) => {
  await login(page); // UI Login (Slow)
  await page.click('Nav -> Users');
  await page.click('Create');
  await page.fill('Form', data); // UI Input (Slow)
  await page.click('Save');
  // ... verify ...
  await page.click('Delete'); // UI Cleanup (Slow)
});
```

**New (Hybrid):**
```typescript
test.use({ storageState: 'admin.json' }); // Reuse Auth

test('Edit User', async ({ page, api }) => {
  // 1. Arrange (API)
  const user = await api.users.create({ ... }); 

  // 2. Act (UI) - Go straight to target
  await page.goto(`/Admin/Users/Edit/${user.id}`);
  await page.fill('Name', 'New Name');
  await page.click('Save');

  // 3. Assert (UI/Data)
  await expect(page.locator('.alert')).toContainText('Saved');
  
  // 4. Cleanup (API - Auto-handled by fixture or explicit)
  await api.users.delete(user.id);
});
```
