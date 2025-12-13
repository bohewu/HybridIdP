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

### Phase 19.3: People & Identity Verification ✅
**Goal**: Handle complex data relationships (Person <-> User <-> Identity).
-   [x] **API Helpers**: `PeopleApi` in `api-client.ts` for Person CRUD and account linking.
-   [x] **Refactor Tests**: Rewrote `feature-people` specs to hybrid pattern.
    -   *Old*: 4 files, ~48KB, complex page.evaluate patterns.
    -   *New*: 4 files, ~7KB, clean API-first approach.

### Phase 19.4: Clients, Resources & Configuration ✅
**Goal**: Refactor configuration-heavy tests.
-   [x] **API Helpers**: Added `ClientsApi` to `api-client.ts`.
-   [x] **Refactor Tests**: Simplified `feature-clients` and `feature-resources` to API pattern.
    -   *Result*: Basic tests refactored, comprehensive client-scope-manager tests preserved.
    -   *Note*: `ResourcesApi` not yet implemented - tests are placeholders.

### Phase 19.5: Cleanup & CI Optimization
**Goal**: Final polish and performance tuning.
-   [ ] **Delete Legacy Scripts**: Remove all `.ps1`, `.txt` outputs, and the `scripts/` folder.
    -   *Reason*: Data seeding is handled by `DataSeeder` and dynamic API helpers.
    -   *Targets*: `e2e/*.ps1`, `e2e/*.txt`, `scripts/*`.
-   [ ] **Remove Legacy Helpers**: Deprecate and remove `e2e/tests/helpers/admin.ts` (UI helpers).
-   [ ] **CI Integration**: Ensure new suite runs in GitHub Actions with proper reporting.
-   [ ] **Documentation**: Update `e2e/README.md` with new patterns.

### Phase 19.6: Advanced UI Tests & Remaining Fixes
**Goal**: Complete comprehensive UI tests and fix complex flows.
-   [ ] **client-scope-manager**: Complex UI tests (257 lines) - already well-written, verify and maintain.
-   [ ] **feature-auth OIDC Flows**: Fix failing consent/scope tests.
    -   *Issue*: 1 test failing blocks 83 tests from running.
    -   *Files*: `consent-required-scopes.spec.ts`, `scope-authorization-flow.spec.ts`, etc.
-   [ ] **Sessions API**: Implement full session management API in `api-client.ts`.
    -   *Methods*: `listSessions()`, `revokeSession()`, `revokeAllSessions()`.
-   [ ] **Comprehensive UI Tests**: Add real UI form tests for:
    -   User creation via UI form (not just API)
    -   Role assignment via UI modal
    -   Permission selection in forms
-   [ ] **API Coverage**: Implement missing API clients:
    -   `ResourcesApi` for API Resources CRUD
    -   `ScopesApi` for Scope management
    -   `ClaimsApi` for Claims management

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
