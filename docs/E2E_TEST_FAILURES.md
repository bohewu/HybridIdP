# E2E Test Failures Analysis

**Last Updated:** 2025-11-27  
**Test Suite Status:** 87/89 passing (97.8% pass rate)

## Overview

This document tracks the remaining E2E test failures and provides guidance for investigation and resolution.

## Current Test Results

### ✅ Passing Tests: 87/89 (97.8%)

All core functionality tests are passing, including:
- Authentication and authorization flows
- Admin CRUD operations (Users, Roles, Scopes, API Resources, Claims, Settings)
- Security policies and session management
- Dashboard and monitoring features
- Permission-based access control
- Real-time updates and notifications

### ❌ Failing Tests: 2/89 (2.2%)

## Detailed Failure Analysis

### 1. admin-clients-crud.spec.ts

**Test:** `Admin - Clients CRUD (create, update, delete client)`  
**Status:** ❌ Failing  
**Error Location:** Line 90  
**Error Type:** `expect(received).not.toBeNull() - Received: null`

#### Failure Details

```
Error: expect(received).not.toBeNull()
Received: null

  88 |     timeout: 30000
  89 |   });
> 90 |   expect(found.locator).not.toBeNull();
     |                             ^
```

#### Root Cause Analysis

The test creates a new client successfully but fails to find it in the client list after creation. The `searchListForItemWithApi` helper returns a null locator, indicating the client is not appearing in the UI.

**Possible Causes:**
1. **Pagination Issue** - Client may be created on a page beyond the default view
2. **List Refresh Timing** - UI list may not update immediately after POST response
3. **Search Filter Problem** - Client list may have active filters preventing the new client from appearing
4. **Client Display Logic** - Client may exist in API but not meet UI display criteria

#### Investigation Steps

1. **Check Playwright Trace:**
   ```powershell
   npx playwright show-trace test-results\tests-feature-clients-admi-1c41a-reate-update-delete-client--chromium\trace.zip
   ```

2. **Verify API Response:**
   - Check if POST `/api/admin/clients` returns 200/201
   - Verify the client is actually created in the database
   - Check if GET `/api/admin/clients?search={clientId}` returns the client

3. **Inspect UI State:**
   - Check if the client list has pagination enabled
   - Verify no active search filters are interfering
   - Check browser console for JavaScript errors

4. **Potential Fixes:**
   - Add explicit page refresh: `await page.goto('https://localhost:7035/Admin/Clients')`
   - Clear search filters before searching for new client
   - Implement pagination navigation to find the client
   - Wait for specific API calls to complete before searching

#### Workaround

The test currently tries to refresh the page after modal close:
```typescript
await page.goto('https://localhost:7035/Admin/Clients');
await page.waitForLoadState('networkidle');
```

This should force a list reload, but the client is still not found. This suggests a deeper issue with the search/list logic.

---

### 2. admin-clients-regenerate-secret.spec.ts

**Test:** `Admin - Regenerate secret for confidential client`  
**Status:** ❌ Failing  
**Error Location:** Line 45  
**Error Type:** `TimeoutError: locator.waitFor: Timeout 10000ms exceeded`

#### Failure Details

```
TimeoutError: locator.waitFor: Timeout 10000ms exceeded.
Call log:
  - waiting for locator('div.fixed input[readonly]').first() to be visible

  43 |
  44 |   // Wait for any input to appear in the modal (secret might not have font-mono class yet)
> 45 |   await page.locator('div.fixed input[readonly]').first().waitFor({ state: 'visible', timeout: 10000 });
     |                                                           ^
```

#### Root Cause Analysis

The test creates a confidential client and expects a secret modal to appear, but the input element containing the secret is never found.

**Possible Causes:**
1. **Incorrect Selector** - Modal structure may differ from expected DOM structure
2. **Modal Animation Delay** - Modal may take longer than 800ms to render
3. **Conditional Rendering** - Secret modal may only appear under certain conditions
4. **Modal Component Change** - UI may have been refactored and selector needs updating

#### Investigation Steps

1. **Check Playwright Trace:**
   ```powershell
   npx playwright show-trace test-results\tests-feature-clients-admi-11046-ret-for-confidential-client-chromium\trace.zip
   ```

2. **Inspect Modal DOM Structure:**
   - Run test with `--headed` mode and pause at failure point
   - Inspect the actual DOM structure of the secret modal
   - Check if modal appears at all or if it has a different structure

3. **Check Multiple Selector Patterns:**
   The test file shows two different selectors were used:
   - First secret: `input[readonly][class*="font-mono"]`
   - Regenerated secret: `div.fixed input[readonly].font-mono`
   
   This inconsistency suggests the modal structure might vary.

4. **Potential Fixes:**
   - Use more robust selector: `[role="dialog"] input[type="text"][readonly]`
   - Check if modal backdrop is blocking interaction
   - Increase wait time beyond 800ms
   - Wait for specific modal visibility state before accessing inputs

#### Workaround

Current attempt to wait for modal:
```typescript
await page.waitForSelector('div.fixed', { timeout: 10000, state: 'visible' });
await page.waitForTimeout(800);
await page.locator('div.fixed input[readonly]').first().waitFor({ state: 'visible', timeout: 10000 });
```

Despite waiting for the modal container (`div.fixed`) to be visible, the input inside it is not found. This suggests either:
- The modal renders without the input initially
- The input has a different structure/selector
- The modal is created but not fully rendered

---

## Recommended Next Steps

### Immediate Actions

1. **Run Tests with Trace Viewer**
   ```powershell
   cd e2e
   npx playwright test tests/feature-clients/admin-clients-crud.spec.ts --trace on
   npx playwright test tests/feature-clients/admin-clients-regenerate-secret.spec.ts --trace on
   npx playwright show-trace test-results/.../trace.zip
   ```

2. **Run Tests in Headed Mode**
   ```powershell
   npx playwright test tests/feature-clients/admin-clients-crud.spec.ts --headed --debug
   npx playwright test tests/feature-clients/admin-clients-regenerate-secret.spec.ts --headed --debug
   ```

3. **Check Application Logs**
   - Review IdP server logs for any errors during client creation
   - Check for validation errors or exceptions

### Long-term Improvements

1. **Apply Timing Helpers**
   - Refactor other tests using arbitrary `waitForTimeout()` calls
   - Use timing helpers from `e2e/tests/helpers/timing.ts`
   - Target: 20+ test files identified with timing anti-patterns

2. **Improve Test Resilience**
   - Add retry logic to critical assertions
   - Use `test.describe.configure({ retries: 2 })` for flaky tests
   - Implement better wait conditions instead of fixed timeouts

3. **Enhance Helper Functions**
   - Update `searchListForItemWithApi` to handle pagination
   - Add modal detection helpers that work with various modal frameworks
   - Create selector fallback mechanisms for UI components

## Test Environment

**Prerequisites:**
- Services must be running before tests execute
- Use `e2e/wait-for-idp-ready.ps1` to ensure services are ready
- Recommended: Run tests with `scripts/run-e2e.ps1 -StartServices`

**Test Configuration:**
- Browser: Chromium
- Workers: 3 parallel workers
- Timeout: 60s per test
- Base URL: https://localhost:7035

**Authentication:**
- Test user: admin@example.com
- Test password: Admin123!
- Required role: Admin

## Success Metrics

- **Target:** 100% pass rate (89/89 tests passing)
- **Current:** 97.8% pass rate (87/89 tests passing)
- **Improvement:** +3 tests fixed (from previous unstable baseline)

## Related Documentation

- [E2E Local Setup Guide](./E2E_LOCAL_SETUP.md)
- [Project Status](./PROJECT_STATUS.md)
- [Development Guide](./DEVELOPMENT_GUIDE.md)
