# E2E Test Failures Analysis
**Date**: 2025-12-04  
**Current Status**: 163/186 passed (87.6%)  
**Target**: >95% (177/186 passing)  
**Gap**: Need to fix 14 more tests

---

## ✅ Completed Fixes (164 → 163 passing, -7 obsolete tests)

### 1. **CSP OAuth Blocking** ✅
- **Fix**: Modified `Web.IdP/Middleware/SecurityHeadersMiddleware.cs` to skip CSP headers for `/connect/*` endpoints
- **Impact**: Fixed OAuth consent form submission blocking
- **Tests Fixed**: 2 (role-permissions-claims tests)
- **Commit**: `fix: Exclude OAuth endpoints from CSP and remove obsolete navigation tests`

### 2. **Obsolete Navigation Tests** ✅
- **Removed**: `my-account-navigation.spec.ts` (6 tests) - role badge feature was removed
- **Removed**: Homepage card navigation test from `linked-accounts.spec.ts` (1 test)
- **Impact**: Cleaned up 7 obsolete tests
- **New Total**: 186 tests (down from 193)

### 3. **Admin Login Helper Fix** ✅
- **Fix**: Modified `e2e/tests/helpers/admin.ts` login() function to handle non-admin users
- **Change**: Wait for either `.user-name` OR URL navigation (AccessDenied)
- **Impact**: Fixed 1 permission test (admin-clients-permissions "when role assigned by id")
- **Commit**: `fix: Make admin login helper handle non-admin users gracefully`

---

## ❌ Remaining Failures (23 tests)

### Category 1: **Frontend Permission Bugs** (9 tests) 🐛
**Root Cause**: Vue admin app not hiding Create/Edit/Delete buttons for read-only users

#### Tests:
1. `admin-clients-permissions.spec.ts:4` - Clients permission denied (create/update/delete)
   - User with `clients.read` can see "Create New Client" button (should be hidden)
   
2. `admin-clients-crud.spec.ts:5` - Admin - Clients CRUD (create, update, delete client)
   - Element not found after creation
   
3. `admin-clients-regenerate-secret.spec.ts:4` - Regenerate secret for confidential client
   - Secret input modal not appearing (timeout 10s)
   
4. `admin-users-crud.spec.ts:133` - Users permission denied (create/update/delete)
   - Similar permission UI issue
   
5. `admin-users-error.spec.ts:69` - Non-admin user cannot update another user (permission denied)
   - Similar permission UI issue
   
6-9. **Session Management** (4 tests)
   - `user-sessions-management.spec.ts:48` - List sessions and revoke single session
   - `user-sessions-management.spec.ts:92` - Revoke all sessions removes all authorizations
   - `user-sessions-management.spec.ts:129` - Non-admin user cannot revoke sessions (permission denied)
     - **Issue**: User with `users.read` gets 200 instead of 401/403 when calling revoke API
   - `user-sessions-management.spec.ts:155` - User cannot revoke another user's session

**Fix Required**: Update Vue components to check user permissions and conditionally render action buttons.

---

### Category 2: **People CRUD - UI Data Loading** (7 tests) 🐛
**Root Cause**: Created people not appearing in table after page reload

#### Tests:
1. `admin-people-crud.spec.ts:28` - Create, update, and delete person with identity document
   - Created person not visible in table after reload
   
2. `admin-people-crud.spec.ts:76` - Create person without identity document
   - Same issue
   
3-7. `admin-people-identity-verification.spec.ts` (5 tests)
   - Line 154: Verify person identity with National ID successfully
   - Line 298: Verify identity button is visible for unverified person with document
   - Line 324: Verify identity button is hidden for already verified person
   - Line 354: Person without identity document does not show verify button
   - Line 376: Multiple document types: National ID checksum validation variants

**Possible Causes**:
- Vue app pagination issue (new items not on first page)
- Data not refreshing after API call
- Search/filter state not cleared

**Fix Required**: 
- Check People list component data fetching logic
- Ensure newly created items appear in the list
- May need to add search/filter reset or navigate to correct page

---

### Category 3: **Localization Issues** (2 tests) 🌐
**Root Cause**: Tests expect Chinese text but app returns English

#### Tests:
1. `authorizations.spec.ts:23` - should display Authorizations page with correct title
   - Expected: "授權管理"
   - Received: "Authorized Applications"
   
2. `linked-accounts.spec.ts:24` - should display LinkedAccounts page with correct title
   - Expected: "帳號鏈結"
   - Received: "Linked Accounts"

**Additional**:
3. `authorizations.spec.ts:33` - should display authorized applications list
   - UI elements not matching expected structure
   
4. `linked-accounts.spec.ts:257` - should load linked-accounts.js external script
   - Script count = 0 (expected > 0)

**Fix Required**: 
- Set correct locale in test setup (zh-TW)
- Check if locale is being persisted in user session
- Verify i18n configuration in Vue app

---

### Category 4: **OAuth/Consent Flow** (2 tests) 🔐
**Root Cause**: OAuth redirect timing out at TestClient Profile page

#### Tests:
1. `user-sessions-management.spec.ts:48` - List sessions and revoke single session
   - Timeout at `loginViaTestClient()` waiting for `**/Account/Profile`
   - No authorization/session created after timeout
   
2. `user-sessions-management.spec.ts:92` - Revoke all sessions removes all authorizations
   - Same issue with `createMultipleSessions()`

**Note**: Role-permissions-claims tests with identical flow are PASSING ✅

**Possible Causes**:
- Intermittent TestClient issue
- Race condition in session creation
- Different test user permissions affecting redirect

**Workaround Attempted**: Modified `loginViaTestClient()` to wait for any TestClient URL, but caused sessions not to be created.

---

### Category 5: **Scope Authorization Flow** (1 test) ⚙️
#### Test:
1. `scope-authorization-flow.spec.ts:11` - Admin marks scope as required → consent shows scope as disabled and checked
   - Error: `page.evaluate: Target page, context or browser has been closed`

**Fix Required**: Investigate test timing/cleanup issue

---

### Category 6: **Claims CRUD** (1 test) 🔧
#### Test:
1. `claims-crud.spec.ts:5` - Admin - Claims CRUD (create, update, delete custom claim)
   - Error: `page.evaluate: Target page, context or browser has been closed`

**Fix Required**: Similar to scope flow - timing issue in finally block

---

## 📊 Summary by Fix Difficulty

### 🟢 Easy (Can be fixed in tests):
- ✅ **DONE**: CSP blocking (2 tests)
- ✅ **DONE**: Obsolete tests removal (7 tests)
- ✅ **DONE**: Login helper (1 test)

### 🟡 Medium (Requires frontend changes):
- **Localization**: Set locale in test setup (2-4 tests)
- **People UI**: Fix data loading/pagination (7 tests)

### 🔴 Hard (Requires backend/architecture changes):
- **Permission UI**: Vue components need permission checks (9 tests)
- **OAuth timing**: Session creation investigation (2 tests)
- **Page lifecycle**: Browser context cleanup (2 tests)

---

## 🎯 Recommended Next Steps

### Option 1: Accept Current State (87.6%)
- Backend tests: **100%** ✅
- E2E tests: **87.6%** (close to 90%)
- 23 failures are mostly **real application bugs**, not test issues
- Document known issues for future sprints

### Option 2: Quick Wins to Reach 90%+
1. **Fix localization** (2 tests): Set `locale: 'zh-TW'` in test beforeEach
2. **Investigate People CRUD** (7 tests): Add wait for table refresh or check pagination
3. **Total**: Could reach ~172/186 = **92.5%**

### Option 3: Full Fix to 95%+
1. All of Option 2
2. Fix Vue permission UI (add v-if checks based on user permissions)
3. Debug OAuth timing issues
4. **Total**: Could reach ~177+/186 = **95%+**

---

## 🔍 Test Command Reference

```powershell
# Run all E2E tests
npx playwright test

# Run specific category
npx playwright test tests/feature-people/
npx playwright test tests/feature-sessions/

# Run single test with UI
npx playwright test tests/feature-people/admin-people-crud.spec.ts --headed

# Debug with trace
npx playwright test tests/feature-people/admin-people-crud.spec.ts --trace on
npx playwright show-trace trace.zip
```

---

## 📝 Files Modified

1. `Web.IdP/Middleware/SecurityHeadersMiddleware.cs` - Skip CSP for OAuth endpoints
2. `e2e/tests/helpers/admin.ts` - Handle non-admin login gracefully  
3. `e2e/tests/feature-my-account/my-account-navigation.spec.ts` - **DELETED**
4. `e2e/tests/feature-my-account/linked-accounts.spec.ts` - Removed homepage navigation test

---

## 📦 Git Commits

```
7894c6a - fix: Make admin login helper handle non-admin users gracefully
2a714bf - fix: Exclude OAuth endpoints from CSP and remove obsolete navigation tests
[previous] - fix: Complete SessionRefreshLifecycle implementation for 100% backend test coverage
```

---

**End of Analysis** | Generated: 2025-12-04
