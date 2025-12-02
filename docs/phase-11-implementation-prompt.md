# Phase 11 Implementation Prompt for AI Agent

**Date Created:** 2025-12-02  
**Target:** AI agent in next development session  
**Context:** Phase 10.1-10.5 Complete, E2E tests passing (28/28), database clean

---

## 🎯 Mission Statement

Implement **Phase 11: Account & Role Switching - Self-Service Identity Management** to enable authenticated users to manage their identity context by switching between assigned roles and linked accounts without admin intervention.

---

## 📋 Prerequisites Verified (Phase 10 Complete)

✅ **Person Entity:** `Core.Domain.Entities.Person` with full profile fields  
✅ **Multi-Account Support:** `ApplicationUser.PersonId` FK enabling 1 Person → N Accounts  
✅ **Person Services:** `IPersonService` with CRUD + account linking APIs  
✅ **Admin UI:** `/Admin/People` with account management interface  
✅ **E2E Tests:** 28 tests passing (CRUD: 9/9, Account Linking: 7/7, Identity Verification: 12/12)  
✅ **Audit Trail:** `IAuditService` logging Person operations to `AuditEvents` table  

---

## 🏗️ Architecture Overview

### Role Switching Architecture
- **Active Role Tracking:** Add `ActiveRoleId` (nullable Guid) to `UserSession` entity
- **Permission Filtering:** When `ActiveRoleId` is set, filter user permissions to only that role's permissions
- **Security:** Switching TO Admin role requires password re-authentication; de-escalation is immediate
- **Default Behavior:** When no active role selected, use current aggregated permissions (backward compatible)

### Account Switching Architecture
- **Person-Based Validation:** User can only switch to accounts linked to their Person entity
- **Session Revocation:** Switching accounts revokes current session and all refresh tokens
- **Re-Authentication:** System signs out and signs in with target account
- **Audit Events:** Both role and account switches logged with full details

---

## 📦 Implementation Plan (5 Sub-Phases)

### Phase 11.1 - Database Schema & Session Service
**Estimated Time:** 1-2 hours  
**Files to Create:**
- `Infrastructure.Migrations.SqlServer/Migrations/20251202xxxxxx_Phase11_1_AddActiveRoleToSession.cs`
- `Infrastructure.Migrations.Postgres/Migrations/20251202xxxxxx_Phase11_1_AddActiveRoleToSession.cs`

**Files to Modify:**
- `Core.Domain/Entities/UserSession.cs` - Add:
  ```csharp
  public Guid? ActiveRoleId { get; set; }
  public DateTime? LastRoleSwitchUtc { get; set; }
  public ApplicationRole? ActiveRole { get; set; } // Navigation property
  ```
- `Infrastructure/ApplicationDbContext.cs` - Configure FK relationship:
  ```csharp
  builder.HasOne(s => s.ActiveRole)
         .WithMany()
         .HasForeignKey(s => s.ActiveRoleId)
         .OnDelete(DeleteBehavior.SetNull);
  ```
- `Infrastructure/Services/SessionService.cs` - Add `.Include(s => s.ActiveRole)` to queries

**Tasks:**
1. Add `ActiveRoleId` (nullable Guid) and `LastRoleSwitchUtc` (DateTime?) to `UserSession`
2. Generate EF migrations for SQL Server and PostgreSQL
3. Apply migrations to dev database (SQL Server in Docker)
4. Add 5 unit tests in `Tests.Infrastructure.UnitTests/UserSessionTests.cs`:
   - Create session with active role
   - Create session without active role (NULL)
   - Update active role
   - Clear active role
   - Query includes ActiveRole navigation

**Verification:**
```powershell
cd Infrastructure.Migrations.SqlServer
dotnet ef migrations add Phase11_1_AddActiveRoleToSession --startup-project ../Web.IdP
cd ../Infrastructure.Migrations.Postgres
dotnet ef migrations add Phase11_1_AddActiveRoleToSession --startup-project ../Web.IdP
docker exec -it hybrididp-mssql-service-1 /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourPassword" -Q "USE hybridauth_idp; SELECT TOP 5 * FROM UserSessions"
```

---

### Phase 11.2 - AccountManagementService & Business Logic
**Estimated Time:** 3-4 hours  
**Files to Create:**
- `Core.Application/IAccountManagementService.cs` - Interface with 4 methods:
  ```csharp
  Task<IEnumerable<LinkedAccountDto>> GetMyLinkedAccountsAsync(Guid userId);
  Task<IEnumerable<AvailableRoleDto>> GetMyAvailableRolesAsync(Guid userId);
  Task SwitchRoleAsync(Guid userId, Guid roleId, string? password = null);
  Task SwitchToAccountAsync(Guid currentUserId, Guid targetAccountId, string reason);
  ```
- `Core.Application/DTOs/AccountManagementDto.cs` - 2 DTOs:
  ```csharp
  public class LinkedAccountDto {
      public Guid UserId { get; set; }
      public string Username { get; set; }
      public string Email { get; set; }
      public List<string> Roles { get; set; }
      public bool IsCurrentAccount { get; set; }
  }
  
  public class AvailableRoleDto {
      public Guid RoleId { get; set; }
      public string RoleName { get; set; }
      public string? Description { get; set; }
      public bool IsActive { get; set; }
      public bool RequiresPasswordConfirmation { get; set; }
  }
  ```
- `Infrastructure/Services/AccountManagementService.cs` - Implementation (~250 lines)
- `Tests.Infrastructure.UnitTests/AccountManagementServiceTests.cs` - 12 tests

**Files to Modify:**
- `Web.IdP/Program.cs` - Add DI registration:
  ```csharp
  builder.Services.AddScoped<IAccountManagementService, AccountManagementService>();
  ```

**Business Logic Requirements:**
1. **GetMyLinkedAccounts:** Query `ApplicationUser` where `PersonId == currentUser.PersonId`
2. **GetMyAvailableRoles:** Query `UserManager.GetRolesAsync(currentUser)` + check active role from session
3. **SwitchRole:**
   - Validate user has target role assigned
   - If target role is "Admin", require password: `await _signInManager.CheckPasswordSignInAsync(user, password, false)`
   - Update `UserSession.ActiveRoleId` and `LastRoleSwitchUtc`
   - Log `RoleSwitched` audit event
4. **SwitchToAccount:**
   - Validate target account has same `PersonId`
   - Revoke all sessions for current user: `await _sessionService.RevokeAllUserSessionsAsync(currentUserId)`
   - Sign out current user: `await _signInManager.SignOutAsync()`
   - Sign in target user: `await _signInManager.SignInAsync(targetUser, isPersistent: true)`
   - Log `AccountSwitched` audit event

**Verification:**
```powershell
dotnet test Tests.Infrastructure.UnitTests/AccountManagementServiceTests.cs
# Expected: 12 passed
```

---

### Phase 11.3 - My Account API Endpoints
**Estimated Time:** 2-3 hours  
**Files to Create:**
- `Web.IdP/Controllers/Api/MyAccountController.cs` - User-facing API (~150 lines)

**API Endpoints:**

```csharp
[ApiController]
[Route("api/my")]
[Authorize] // Authenticated users only
public class MyAccountController : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<ActionResult<IEnumerable<LinkedAccountDto>>> GetMyLinkedAccounts()
    
    [HttpGet("roles")]
    public async Task<ActionResult<IEnumerable<AvailableRoleDto>>> GetMyAvailableRoles()
    
    [HttpPost("switch-role")]
    public async Task<IActionResult> SwitchRole([FromBody] SwitchRoleRequest request)
    // Request: { "roleId": "guid", "password": "string?" }
    
    [HttpPost("switch-account")]
    public async Task<IActionResult> SwitchAccount([FromBody] SwitchAccountRequest request)
    // Request: { "targetAccountId": "guid", "reason": "string" }
}
```

**Response Examples:**

**GET /api/my/accounts**
```json
{
  "accounts": [
    {
      "userId": "guid",
      "username": "john.doe",
      "email": "john@example.com",
      "roles": ["Developer", "Manager"],
      "isCurrentAccount": true
    }
  ]
}
```

**GET /api/my/roles**
```json
{
  "roles": [
    {
      "roleId": "guid",
      "roleName": "Developer",
      "description": "Software developer role",
      "isActive": true,
      "requiresPasswordConfirmation": false
    },
    {
      "roleId": "guid",
      "roleName": "Admin",
      "description": "System administrator",
      "isActive": false,
      "requiresPasswordConfirmation": true
    }
  ]
}
```

**Verification:**
```powershell
# Start backend
cd Web.IdP; dotnet run

# Test with curl (replace TOKEN with valid JWT)
curl -H "Authorization: Bearer TOKEN" https://localhost:7035/api/my/accounts
curl -H "Authorization: Bearer TOKEN" https://localhost:7035/api/my/roles
curl -X POST -H "Authorization: Bearer TOKEN" -H "Content-Type: application/json" \
  -d '{"roleId":"guid","password":"Admin@123"}' \
  https://localhost:7035/api/my/switch-role
```

---

### Phase 11.4 - My Account Vue.js UI
**Estimated Time:** 4-5 hours  
**Files to Create:**
- `Web.IdP/Pages/MyAccount.cshtml` + `.cs` - Razor page with `[Authorize]` attribute
- `Web.IdP/ClientApp/src/user/account/AccountManagementApp.vue` - Main app (~200 lines)
- `Web.IdP/ClientApp/src/user/account/components/RoleSwitcher.vue` - Role modal (~120 lines)
- `Web.IdP/ClientApp/src/user/account/components/AccountSwitcher.vue` - Account modal (~100 lines)
- `Web.IdP/ClientApp/src/user/account/components/AccountInfo.vue` - Info card (~80 lines)
- `Web.IdP/ClientApp/src/user/account/main.js` - Vite entry point

**Files to Modify:**
- `Web.IdP/ClientApp/vite.config.js` - Add entry point:
  ```javascript
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        admin: resolve(__dirname, 'admin.html'),
        myaccount: resolve(__dirname, 'myaccount.html') // NEW
      }
    }
  }
  ```
- `Web.IdP/ClientApp/src/i18n/locales/en-US.json` - Add ~40 i18n keys
- `Web.IdP/ClientApp/src/i18n/locales/zh-TW.json` - Add Chinese translations
- `Web.IdP/Pages/Shared/_Layout.cshtml` - Add menu item:
  ```html
  <a class="dropdown-item" href="/MyAccount">
      <i class="bi bi-person-circle me-2"></i>@Localizer["MyAccount"]
  </a>
  ```

**UI Components:**

**AccountManagementApp.vue:**
```vue
<template>
  <div class="container py-4">
    <h1>{{ t('myAccount.title') }}</h1>
    
    <!-- Linked Accounts Section -->
    <div class="card mb-4">
      <div class="card-header">{{ t('myAccount.linkedAccounts') }}</div>
      <div class="card-body">
        <table class="table">
          <thead>
            <tr>
              <th>{{ t('myAccount.username') }}</th>
              <th>{{ t('myAccount.email') }}</th>
              <th>{{ t('myAccount.roles') }}</th>
              <th>{{ t('myAccount.status') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="account in accounts" :key="account.userId">
              <td>{{ account.username }}</td>
              <td>{{ account.email }}</td>
              <td>{{ account.roles.join(', ') }}</td>
              <td>
                <span v-if="account.isCurrentAccount" class="badge bg-success">
                  {{ t('myAccount.currentAccount') }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
    
    <!-- Active Role Selector -->
    <div class="card mb-4">
      <div class="card-header">{{ t('myAccount.activeRole') }}</div>
      <div class="card-body">
        <div class="row align-items-center">
          <div class="col-md-8">
            <select v-model="selectedRoleId" class="form-select">
              <option v-for="role in roles" :key="role.roleId" :value="role.roleId">
                {{ role.roleName }}
                <span v-if="role.isActive">({{ t('myAccount.active') }})</span>
              </option>
            </select>
          </div>
          <div class="col-md-4">
            <button @click="handleSwitchRole" class="btn btn-primary">
              {{ t('myAccount.switchRole') }}
            </button>
          </div>
        </div>
      </div>
    </div>
    
    <!-- Quick Actions -->
    <div class="card">
      <div class="card-header">{{ t('myAccount.quickActions') }}</div>
      <div class="card-body">
        <button @click="showAccountSwitcher = true" class="btn btn-outline-primary">
          <i class="bi bi-arrow-left-right me-2"></i>{{ t('myAccount.switchAccount') }}
        </button>
      </div>
    </div>
    
    <!-- Modals -->
    <RoleSwitcher 
      v-if="showRoleSwitcher" 
      :role="selectedRole" 
      @confirm="confirmRoleSwitch" 
      @cancel="showRoleSwitcher = false" 
    />
    <AccountSwitcher 
      v-if="showAccountSwitcher" 
      :accounts="accounts" 
      @confirm="confirmAccountSwitch" 
      @cancel="showAccountSwitcher = false" 
    />
  </div>
</template>
```

**i18n Keys (en-US.json):**
```json
{
  "myAccount": {
    "title": "My Account",
    "linkedAccounts": "Linked Accounts",
    "activeRole": "Active Role",
    "switchRole": "Switch Role",
    "switchAccount": "Switch Account",
    "currentAccount": "Current Account",
    "username": "Username",
    "email": "Email",
    "roles": "Roles",
    "status": "Status",
    "active": "Active",
    "quickActions": "Quick Actions",
    "passwordRequired": "Password Required",
    "enterPassword": "Enter your password to elevate to Admin role",
    "passwordPlaceholder": "Password",
    "confirm": "Confirm",
    "cancel": "Cancel",
    "switchSuccess": "Switched successfully",
    "switchError": "Failed to switch",
    "passwordIncorrect": "Incorrect password",
    "unauthorized": "You don't have permission to switch to this role/account"
  }
}
```

**Verification:**
```powershell
cd Web.IdP/ClientApp; npm run dev
# Open https://localhost:7035/MyAccount
# Expected: See linked accounts table, role selector, switch account button
```

---

### Phase 11.5 - E2E Tests & Documentation
**Estimated Time:** 2-3 hours  
**Files to Create:**
- `e2e/tests/feature-account/my-account-management.spec.ts` - 6 E2E tests

**Files to Modify:**
- `e2e/global-setup.ts` - Add test person with 2 linked accounts
- `docs/PROJECT_PROGRESS.md` - Add Phase 11 entry
- `docs/phase-11-account-role-management.md` - Update with completion status

**E2E Test Scenarios:**

```typescript
import { test, expect } from '@playwright/test'
import { loginAsUser, createLinkedAccounts } from '../helpers/account'

test.describe('My Account Management', () => {
  test('View linked accounts', async ({ page }) => {
    // Login as user with 2 linked accounts
    // Navigate to /MyAccount
    // Verify accounts table shows both accounts
    // Verify current account has badge
  })

  test('Switch role to non-admin without password', async ({ page }) => {
    // Login as user with Developer and Manager roles
    // Navigate to /MyAccount
    // Select "Manager" role
    // Click "Switch Role"
    // Verify no password modal shown
    // Verify success message
  })

  test('Switch role to Admin with password', async ({ page }) => {
    // Login as user with Developer and Admin roles
    // Navigate to /MyAccount
    // Select "Admin" role
    // Click "Switch Role"
    // Verify password modal shown
    // Enter correct password
    // Verify success message
  })

  test('Switch role to Admin with incorrect password fails', async ({ page }) => {
    // Same as above but enter wrong password
    // Verify error message
  })

  test('Switch account successfully', async ({ page }) => {
    // Login as first account
    // Navigate to /MyAccount
    // Click "Switch Account"
    // Select second account
    // Confirm switch
    // Verify redirected and session updated
  })

  test('Cannot switch to unrelated account', async ({ page }) => {
    // Login as user
    // Attempt API call to switch to account with different PersonId
    // Verify 403 Forbidden response
  })
})
```

**Verification:**
```powershell
cd e2e
npx playwright test tests/feature-account/my-account-management.spec.ts --workers=1
# Expected: 6 passed
```

---

## 🔒 Security Checklist

Before marking Phase 11 complete, verify:

- [ ] Admin role elevation requires password verification
- [ ] Account switching validates same PersonId
- [ ] Session revocation on account switch implemented
- [ ] Audit events logged for all switches (check `AuditEvents` table)
- [ ] Permission filtering based on ActiveRoleId implemented (update `PermissionAuthorizationHandler`)
- [ ] API endpoints require authentication (`[Authorize]` attribute)
- [ ] Password validation uses `SignInManager.CheckPasswordSignInAsync` (not plain text comparison)
- [ ] SQL injection prevention: Use parameterized queries via EF Core

---

## 📊 Success Metrics

**Phase 11 Complete When:**
1. ✅ 5 unit tests passing for `UserSession` with ActiveRoleId
2. ✅ 12 unit tests passing for `AccountManagementService`
3. ✅ 4 API endpoints returning correct responses (test with Postman/curl)
4. ✅ My Account UI loads and shows linked accounts table
5. ✅ Role switching works (non-admin immediate, Admin requires password)
6. ✅ Account switching revokes old session and creates new session
7. ✅ 6 E2E tests passing for My Account workflows
8. ✅ Audit events logged to database (verify with SQL query)
9. ✅ Documentation updated in `PROJECT_PROGRESS.md`

---

## 🛠️ Development Environment Setup

**Prerequisites:**
- .NET 8 SDK
- Node.js 18+
- SQL Server in Docker (already running: `hybrididp-mssql-service-1`)
- Backend running on https://localhost:7035
- Frontend dev server on http://localhost:5173

**Start Development:**
```powershell
# Terminal 1: Backend
cd Web.IdP
dotnet run

# Terminal 2: Frontend
cd Web.IdP/ClientApp
npm run dev

# Terminal 3: E2E Tests
cd e2e
npx playwright test --ui
```

---

## 📚 Reference Documentation

**Must Read Before Starting:**
1. `docs/phase-10-person-identity.md` - Person entity and multi-account architecture
2. `docs/phase-11-account-role-management.md` - Full Phase 11 specification (this is summary)
3. `docs/ARCHITECTURE.md` - Permission system and authentication flow
4. `Core.Domain/Entities/UserSession.cs` - Existing session entity structure

**Code Patterns to Follow:**
1. **Service Pattern:** See `Infrastructure/Services/PersonService.cs` for reference
2. **API Controller:** See `Web.IdP/Controllers/Api/AdminPeopleController.cs` for patterns
3. **Vue Component:** See `Web.IdP/ClientApp/src/admin/persons/PersonForm.vue` for styling
4. **E2E Test:** See `e2e/tests/feature-people/admin-people-crud.spec.ts` for helper usage

---

## 🚨 Common Pitfalls to Avoid

1. **Don't forget to revoke sessions on account switch** - Always call `SessionService.RevokeAllUserSessionsAsync`
2. **Don't skip PersonId validation** - Critical security check for account switching
3. **Don't cache permissions after role switch** - Must update claims principal
4. **Don't use plain text password comparison** - Always use `SignInManager.CheckPasswordSignInAsync`
5. **Don't forget to add `.Include(s => s.ActiveRole)` in SessionService queries** - Avoid N+1 problem
6. **Don't hardcode "Admin" string** - Use `RoleNames.Admin` constant from `Core.Domain/Constants/RoleNames.cs`

---

## 💬 Questions to Ask Before Starting

If anything is unclear, ask the user:
1. Should we implement rate limiting for switch operations? (Recommended: 10 role switches/hour, 5 account switches/hour)
2. Should role preference persist across sessions? (Optional Phase 11.6 feature)
3. Should we add MFA requirement for Admin elevation? (If MFA exists in codebase)
4. Should external login management be part of Phase 11.4 UI? (Currently deferred to Phase 11.6)

---

## 🎯 Final Deliverables

When Phase 11 is complete, provide:
1. **Code Summary:** List of files created/modified with line counts
2. **Test Results:** Screenshot or console output showing all tests passing
3. **Database Verification:** SQL query result showing `UserSessions` table with `ActiveRoleId` column
4. **API Testing:** Postman collection or curl commands for all 4 endpoints
5. **UI Demo:** Screenshot of My Account page with linked accounts and role switcher
6. **Audit Log Sample:** SQL query showing `RoleSwitched` and `AccountSwitched` events

---

**Good luck!** 🚀

Remember: **Small, incremental steps with tests at each phase.** Don't try to implement everything at once. Follow the 5 sub-phases in order.

If you encounter issues:
1. Check existing Person-related code for patterns
2. Review E2E test helpers in `e2e/tests/helpers/admin.ts`
3. Verify database migrations applied: `docker exec -it hybrididp-mssql-service-1 /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourPassword" -Q "USE hybridauth_idp; SELECT * FROM __EFMigrationsHistory"`

**End of Prompt**
