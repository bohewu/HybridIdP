# Phase 11: Account & Role Switching - Self-Service Identity Management

Status: 📋 Planned (Phase 11.1-11.5)

## Goal
Enable authenticated users to manage their identity context by switching between assigned roles (permission filtering) and linked accounts (re-authentication under same Person), providing a self-service "My Account" UI for viewing linked accounts and managing active roles without admin intervention.

## Why
- **Role Switching:** Users with multiple roles (e.g., Developer + Admin) need to operate with least-privilege by default and temporarily elevate when needed, improving security and reducing accidental privileged operations.
- **Account Switching:** A person with multiple accounts (e.g., contract account + permanent employee account) needs seamless switching between identities without logout/login cycles.
- **Self-Service UI:** Reduce admin workload by allowing users to view their own linked accounts and manage role context independently.
- **Audit Trail:** All role/account switches must be logged for compliance and security monitoring.

## Design Summary

### Role Switching Architecture
- **Active Role Tracking:** Add `ActiveRoleId` (nullable Guid) to `UserSession` entity to store currently selected role
- **Permission Filtering:** When `ActiveRoleId` is set, filter user permissions to only include that role's permissions (instead of aggregating all assigned roles)
- **Security:** Switching TO Admin role requires password re-authentication; de-escalation (Admin → User) is immediate
- **Default Behavior:** When no active role selected, use current aggregated permissions (backward compatible)

### Account Switching Architecture
- **Person-Based Validation:** User can only switch to accounts linked to their Person entity (verified via `ApplicationUser.PersonId`)
- **Session Revocation:** Switching accounts revokes current session and all refresh tokens for old account
- **Re-Authentication:** System signs out and signs in with target account using standard authentication flow
- **Audit Events:** Both role and account switches logged with `RoleSwitched` and `AccountSwitched` event types

### My Account UI Structure
```
/MyAccount (authenticated users only)
├── Linked Accounts Section (read-only)
│   └── Table: Username, Email, Roles, "Current Account" badge
├── Active Role Selector
│   ├── Dropdown with available roles
│   └── Apply button (with password modal for Admin elevation)
└── Quick Actions
    └── Switch Account button → AccountSwitcher modal
```

## Incremental Implementation Plan

### Phase 11.1 - Database Schema & Session Service ✅ (Planned)
- Add `ActiveRoleId` nullable Guid FK to `UserSession` entity
- Add `LastRoleSwitchUtc` DateTime for audit tracking
- Generate EF migrations for SQL Server and PostgreSQL (`Phase11_1_AddActiveRoleToSession`)
- Update `SessionService` to include `ActiveRole` navigation property with `.Include()` support
- Add unit tests for UserSession with ActiveRoleId (5 tests)

**Files to Create:**
- `Infrastructure.Migrations.SqlServer/Migrations/20251129xxxxxx_Phase11_1_AddActiveRoleToSession.cs`
- `Infrastructure.Migrations.Postgres/Migrations/20251129xxxxxx_Phase11_1_AddActiveRoleToSession.cs`

**Files to Modify:**
- `Core.Domain/Entities/UserSession.cs` - Add ActiveRoleId, LastRoleSwitchUtc, ActiveRole navigation
- `Infrastructure/ApplicationDbContext.cs` - Configure FK relationship
- `Infrastructure/Services/SessionService.cs` - Add `.Include(s => s.ActiveRole)` to queries

### Phase 11.2 - AccountManagementService & Business Logic ✅ (Planned)
- Create `IAccountManagementService` interface with 4 methods:
  - `Task<IEnumerable<LinkedAccountDto>> GetMyLinkedAccountsAsync(Guid userId)`
  - `Task<IEnumerable<AvailableRoleDto>> GetMyAvailableRolesAsync(Guid userId)`
  - `Task SwitchRoleAsync(Guid userId, Guid roleId, string? password = null)`
  - `Task SwitchToAccountAsync(Guid currentUserId, Guid targetAccountId, string reason)`
- Implement `AccountManagementService` in Infrastructure with:
  - Person-based authorization (verify same PersonId for account switching)
  - Password verification for Admin role elevation using `SignInManager.CheckPasswordSignInAsync`
  - Session revocation and re-authentication logic
  - Audit logging for all switches
- Add 12 unit tests covering validation, authorization, audit logging

**Files to Create:**
- `Core.Application/IAccountManagementService.cs` - Service interface
- `Infrastructure/Services/AccountManagementService.cs` - Implementation (~200 lines)
- `Core.Application/DTOs/AccountManagementDto.cs` - 2 DTOs (LinkedAccountDto, AvailableRoleDto)
- `Tests.Infrastructure.UnitTests/AccountManagementServiceTests.cs` - 12 comprehensive tests

**Files to Modify:**
- `Web.IdP/Program.cs` - Register `IAccountManagementService → AccountManagementService` in DI

**Business Rules:**
1. Role switching requires user has the target role assigned
2. Admin role elevation requires password confirmation
3. Account switching requires accounts share same PersonId
4. Account switching revokes all sessions for current account
5. All switches logged via IAuditService with timestamp and reason

### Phase 11.3 - My Account API Endpoints ✅ (Planned)
- Create `MyAccountController` in `Web.IdP/Controllers/Api` with 4 RESTful endpoints:
  - `GET /api/my/accounts` - List linked accounts for current user
  - `GET /api/my/roles` - List available roles with active role indicator
  - `POST /api/my/switch-role` - Switch active role (body: `{ "roleId": "guid", "password": "string?" }`)
  - `POST /api/my/switch-account` - Switch to different account (body: `{ "targetAccountId": "guid", "reason": "string" }`)
- Use `[Authorize]` attribute (authenticated users only, not admin-restricted)
- Return appropriate HTTP status codes: 200 OK, 400 Bad Request (validation), 403 Forbidden (authorization), 401 Unauthorized
- Add API integration tests (optional, can use E2E tests for coverage)

**Files to Create:**
- `Web.IdP/Controllers/Api/MyAccountController.cs` - User-facing API (~150 lines)

**API Specifications:**

**GET /api/my/accounts**
```json
Response 200:
{
  "accounts": [
    {
      "userId": "guid",
      "username": "john.doe",
      "email": "john@example.com",
      "roles": ["Developer", "Manager"],
      "isCurrentAccount": true
    },
    {
      "userId": "guid",
      "username": "john.contractor",
      "email": "john.contractor@example.com",
      "roles": ["Contractor"],
      "isCurrentAccount": false
    }
  ]
}
```

**GET /api/my/roles**
```json
Response 200:
{
  "roles": [
    {
      "roleId": "guid",
      "roleName": "Developer",
      "description": "Software developer role",
      "isActive": true
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

**POST /api/my/switch-role**
```json
Request:
{
  "roleId": "guid",
  "password": "Admin@123"  // Required for Admin role only
}

Response 200: { "message": "Role switched successfully", "activeRole": "Admin" }
Response 400: { "error": "Password required for Admin role elevation" }
Response 403: { "error": "Role not assigned to current user" }
```

**POST /api/my/switch-account**
```json
Request:
{
  "targetAccountId": "guid",
  "reason": "Switching to permanent employee account"
}

Response 200: { "message": "Account switched successfully", "newSessionId": "guid" }
Response 403: { "error": "Target account does not belong to your Person" }
```

### Phase 11.4 - My Account Vue.js UI ✅ (Planned)
- Create Razor page `/MyAccount.cshtml` + `.cs` with `[Authorize]` attribute
- Create Vue.js app `ClientApp/src/user/account/AccountManagementApp.vue` with:
  - Linked Accounts table (read-only, shows username, email, roles, current badge)
  - Active Role Selector (dropdown with available roles + Apply button)
  - Quick Actions section with "Switch Account" button
- Create 3 Vue components:
  - `RoleSwitcher.vue` - Modal with role selection and password input (shown for Admin elevation)
  - `AccountSwitcher.vue` - Modal with account list and confirmation
  - `AccountInfo.vue` - Card component showing current account details
- Add ~40 i18n keys in `en-US.json` and `zh-TW.json`:
  - "MyAccount", "LinkedAccounts", "ActiveRole", "SwitchRole", "SwitchAccount"
  - "CurrentAccount", "AvailableRoles", "ConfirmPassword", "AdminElevationRequired"
  - "AccountSwitchedSuccessfully", "RoleSwitchedSuccessfully", "PasswordIncorrect"
- Add Vite entry point in `vite.config.js` for `myaccount` app
- Add navigation link in `_Layout.cshtml` user dropdown (between Admin Console and Logout)

**Files to Create:**
- `Web.IdP/Pages/MyAccount.cshtml` + `.cs` - Razor page
- `Web.IdP/ClientApp/src/user/account/AccountManagementApp.vue` - Main app component (~200 lines)
- `Web.IdP/ClientApp/src/user/account/components/RoleSwitcher.vue` - Role modal (~120 lines)
- `Web.IdP/ClientApp/src/user/account/components/AccountSwitcher.vue` - Account modal (~100 lines)
- `Web.IdP/ClientApp/src/user/account/components/AccountInfo.vue` - Info card (~80 lines)
- `Web.IdP/ClientApp/src/user/account/main.js` - Vite entry point

**Files to Modify:**
- `Web.IdP/ClientApp/vite.config.js` - Add `myaccount` entry point
- `Web.IdP/ClientApp/src/i18n/locales/en-US.json` - Add ~40 i18n keys
- `Web.IdP/ClientApp/src/i18n/locales/zh-TW.json` - Add Chinese translations
- `Web.IdP/Pages/Shared/_Layout.cshtml` - Add "My Account" menu item
- `Web.IdP/Resources/SharedResource.en-US.resx` - Add "MyAccount"
- `Web.IdP/Resources/SharedResource.zh-TW.resx` - Add "我的帳號"

**UI Features:**
- Table-based linked accounts view with sortable columns
- Real-time role switching with loading states
- Password confirmation modal for Admin elevation
- Account switcher with confirmation dialog
- Success/error toast notifications
- Form validation and error handling
- Consistent styling with Admin UI (Bootstrap + Tailwind)

### Phase 11.5 - E2E Tests & Documentation ✅ (Planned)
- Create 6 comprehensive E2E tests in `e2e/tests/feature-account/my-account-management.spec.ts`:
  1. **View Linked Accounts** - Navigate to My Account, verify accounts table shows all linked accounts with correct badges
  2. **Switch Role (Non-Admin)** - Select Developer role, click Apply, verify role switched without password prompt
  3. **Switch Role to Admin** - Select Admin role, verify password modal shown, enter password, verify role switched
  4. **Switch Role Invalid Password** - Attempt Admin elevation with wrong password, verify error message
  5. **Switch Account** - Click Switch Account, select different account, confirm, verify session updated and page shows new account
  6. **Unauthorized Account Switch** - Create test user without linked accounts, attempt API call to switch to unrelated account, verify 403 response
- Create test helpers in `e2e/tests/helpers/account.ts`:
  - `switchRoleInUI(page, roleName, password?)` - Helper to switch role via UI
  - `switchAccountInUI(page, targetUsername)` - Helper to switch account
  - `createLinkedAccounts(page, personId, count)` - Helper to setup test data
- Update `e2e/global-setup.ts` to create test person with 2 linked accounts for testing
- Document Phase 11 in `docs/PROJECT_PROGRESS.md` and update `docs/phase-11-account-role-management.md` with implementation results

**Files to Create:**
- `e2e/tests/feature-account/my-account-management.spec.ts` - 6 E2E tests
- `e2e/tests/helpers/account.ts` - Test helper functions

**Files to Modify:**
- `e2e/global-setup.ts` - Add test person with linked accounts setup
- `docs/PROJECT_PROGRESS.md` - Add Phase 11 entry
- `docs/phase-11-account-role-management.md` - Update with completion status

**Test Coverage:**
- ✅ View linked accounts
- ✅ Switch between non-admin roles
- ✅ Admin role elevation with password
- ✅ Invalid password handling
- ✅ Account switching flow
- ✅ Authorization enforcement (403 tests)
- ✅ Session persistence after role switch
- ✅ Audit log verification

## Security & Audit Considerations

### Authentication & Authorization
1. **Role Switching Security**
   - Users can only switch to roles they are explicitly assigned (verified via `UserManager.GetRolesAsync`)
   - Admin role elevation requires password re-authentication using `SignInManager.CheckPasswordSignInAsync`
   - De-escalation (Admin → Developer) allowed without password for usability
   - Active role stored in `UserSession`, not in authentication cookie (prevents tampering)

2. **Account Switching Security**
   - Target account must belong to same Person (verified via `PersonId` FK)
   - Current session and all refresh tokens revoked before switch
   - New session created with full re-authentication (not token refresh)
   - IP address and User-Agent logged for suspicious activity detection

3. **Permission Filtering**
   - When `ActiveRoleId` is set, permission handler filters to only that role's permissions
   - Update `PermissionAuthorizationHandler` to check `UserSession.ActiveRoleId` from database
   - If no active role, aggregate all assigned roles (backward compatible behavior)
   - Permission claims updated on role switch (requires new sign-in)

### Audit Trail Requirements
All switches must log via `IAuditService` with the following event types:

**RoleSwitched Event:**
```csharp
new AuditEvent {
    EventType = "RoleSwitched",
    UserId = currentUserId,
    Timestamp = DateTime.UtcNow,
    Details = JsonSerializer.Serialize(new {
        FromRole = previousRole?.Name,
        ToRole = newRole.Name,
        RequiredPassword = isAdminElevation,
        SessionId = sessionId,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = httpContext.Request.Headers["User-Agent"].ToString()
    })
}
```

**AccountSwitched Event:**
```csharp
new AuditEvent {
    EventType = "AccountSwitched",
    UserId = currentUserId,
    Timestamp = DateTime.UtcNow,
    Details = JsonSerializer.Serialize(new {
        FromAccount = currentUser.UserName,
        ToAccount = targetUser.UserName,
        PersonId = person.Id,
        Reason = switchReason,
        SessionsRevoked = revokedSessionCount,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = httpContext.Request.Headers["User-Agent"].ToString()
    })
}
```

### Session Management
- **Session Continuity:** Role switching updates session metadata without creating new session
- **Session Revocation:** Account switching revokes all sessions for old account and creates new session for new account
- **Token Invalidation:** Use OpenIddict's `IOpenIddictAuthorizationManager.RevokeAsync()` to invalidate refresh tokens on account switch
- **Session Hijacking Prevention:** Log IP address and User-Agent changes; alert on suspicious switches

### Rate Limiting Recommendations
To prevent abuse, implement rate limiting on switch endpoints:
- Role switching: Max 10 switches per hour per user
- Account switching: Max 5 switches per hour per person
- Failed Admin elevation attempts: Max 3 per 15 minutes (lockout on exceed)

Implementation can use `AspNetCoreRateLimit` NuGet package or custom middleware with Redis caching.

## Testing and Validation

### Unit Tests (Phase 11.2)
**AccountManagementServiceTests.cs** (12 tests):
1. `GetMyLinkedAccounts_ReturnsAllAccountsForPerson`
2. `GetMyLinkedAccounts_MarksCurrentAccount`
3. `GetMyAvailableRoles_ReturnsAssignedRoles`
4. `GetMyAvailableRoles_MarksActiveRole`
5. `SwitchRole_ToNonAdminRole_UpdatesSessionWithoutPassword`
6. `SwitchRole_ToAdminRole_RequiresPassword`
7. `SwitchRole_ToAdminRole_WithCorrectPassword_Succeeds`
8. `SwitchRole_ToAdminRole_WithIncorrectPassword_ThrowsUnauthorized`
9. `SwitchRole_ToUnassignedRole_ThrowsForbidden`
10. `SwitchToAccount_WithSamePerson_SucceedsAndRevokesSession`
11. `SwitchToAccount_WithDifferentPerson_ThrowsForbidden`
12. `SwitchActions_LogAuditEvents`

### E2E Tests (Phase 11.5)
**my-account-management.spec.ts** (6 tests):
1. View linked accounts table with correct data and badges
2. Switch role to non-admin (Developer) without password
3. Switch role to Admin with password confirmation modal
4. Attempt Admin elevation with incorrect password (error handling)
5. Switch to different linked account and verify session change
6. Attempt to switch to unrelated account (403 authorization test)

### Integration Test Scenarios
- Create person with 3 linked accounts → switch between all 3 → verify sessions
- User with Admin + Developer roles → switch to Developer → verify Admin permissions removed
- Switch to Admin role → logout → login again → verify role not persisted across sessions (unless saved in DB preference - optional Phase 11.6 enhancement)

## Acceptance Criteria

### Phase 11 Complete When:
1. ✅ `UserSession` entity has `ActiveRoleId` and `LastRoleSwitchUtc` columns with migrations applied
2. ✅ `AccountManagementService` implements all 4 methods with Person-based authorization
3. ✅ My Account API has 4 working endpoints returning correct DTOs
4. ✅ My Account UI shows linked accounts (read-only) and active role selector
5. ✅ Role switching works: non-admin roles switch immediately, Admin requires password
6. ✅ Account switching revokes old session and creates new session
7. ✅ All switches logged to `AuditEvents` table with full details
8. ✅ 12 unit tests passing for `AccountManagementService`
9. ✅ 6 E2E tests passing for My Account UI workflows
10. ✅ Documentation updated in `PROJECT_PROGRESS.md` and this phase doc

### Security Checklist:
- [ ] Admin role elevation requires password verification
- [ ] Account switching validates same PersonId
- [ ] Session revocation on account switch implemented
- [ ] Audit events logged for all switches
- [ ] Permission filtering based on ActiveRoleId implemented
- [ ] Rate limiting recommendation documented (implementation optional)

### UX Checklist:
- [ ] Role switcher shows current active role
- [ ] Account table shows "Current Account" badge
- [ ] Password modal only shown for Admin elevation
- [ ] Success/error toasts for all operations
- [ ] Loading states during API calls
- [ ] i18n support for all UI text (en-US + zh-TW)

## Rollout & Deployment Strategy

### Phase Approach:
1. **Phase 11.1-11.2:** Backend implementation (migrations, service, tests) - Can deploy without frontend
2. **Phase 11.3:** API endpoints - Enables testing with API clients before UI ready
3. **Phase 11.4:** Frontend UI - User-facing feature, requires all backend complete
4. **Phase 11.5:** E2E tests and documentation - Validation and quality assurance

### Database Migration:
- Add `ActiveRoleId` and `LastRoleSwitchUtc` columns to `UserSessions` table (nullable, no default)
- Existing sessions will have `ActiveRoleId = NULL` (meaning use all assigned roles - backward compatible)
- No data migration needed for existing sessions

### Backward Compatibility:
- When `UserSession.ActiveRoleId` is NULL, permission handler aggregates all assigned roles (current behavior)
- Existing authentication flows unaffected until user explicitly switches role
- My Account page is new addition, does not modify existing pages

### Performance Considerations:
- Add index on `UserSession.ActiveRoleId` for faster permission lookups
- Add composite index on `(UserId, ActiveRoleId)` for session queries
- Cache active role in memory during request lifetime (avoid repeated DB queries)
- Consider adding `ActiveRole` navigation property to reduce N+1 queries

## Future Enhancements (Phase 11.6+)

### Optional Features Not in Initial Scope:
1. **Persistent Role Preference**
   - Save user's preferred default role in `ApplicationUser` or `Person` table
   - Auto-select preferred role on login instead of aggregating all roles
   - UI: "Set as Default" checkbox in role switcher

2. **External Login Management** (Deferred from Phase 11)
   - Link/unlink Google/Facebook accounts via OAuth callbacks
   - View linked external providers in My Account UI
   - Prevent unlinking last authentication method

3. **Account Switching Without Re-Auth** (Advanced)
   - Issue impersonation token for target account without full re-authentication
   - Use OpenIddict's token exchange or custom JWT with `switch_account` claim
   - Security risk: requires careful design to prevent privilege escalation

4. **Multi-Factor Authentication for Switches**
   - Require MFA code for Admin role elevation (in addition to password)
   - Require MFA for account switching if enabled on either account
   - Integration with existing MFA implementation (if Phase 5 MFA exists)

5. **Switch History UI**
   - Show timeline of role/account switches in My Account page
   - Filter audit log to show only `RoleSwitched` and `AccountSwitched` events for current user
   - Export switch history to CSV/Excel

6. **Role-Based UI Customization**
   - Show/hide menu items based on active role
   - Change dashboard layout based on role (Admin sees monitoring, Developer sees projects)
   - Requires frontend permission checks using `hasPermission()` helper

## Developer Notes

### Key Files Reference:
- **Entities:** `Core.Domain/Entities/UserSession.cs`, `ApplicationUser.cs`, `Person.cs`
- **Services:** `Infrastructure/Services/AccountManagementService.cs`, `SessionService.cs`
- **API:** `Web.IdP/Controllers/Api/MyAccountController.cs`
- **UI:** `Web.IdP/ClientApp/src/user/account/AccountManagementApp.vue`
- **Tests:** `Tests.Infrastructure.UnitTests/AccountManagementServiceTests.cs`, `e2e/tests/feature-account/my-account-management.spec.ts`

### Related Documentation:
- **Phase 10 (Person & Identity):** `docs/phase-10-person-identity.md` - Foundation for multi-account support
- **Phase 7 (Audit & Monitoring):** `docs/phase-7-audit-monitoring.md` - Audit event patterns
- **Architecture:** `docs/ARCHITECTURE.md` - Permission system and authentication flow

### Testing Strategy:
1. Run unit tests after Phase 11.2: `dotnet test Tests.Infrastructure.UnitTests/AccountManagementServiceTests.cs`
2. Test API endpoints manually: Use Postman/curl with admin token to test switch endpoints
3. Run E2E tests after Phase 11.5: `npm test e2e/tests/feature-account/my-account-management.spec.ts`
4. Validate audit logs: Check `AuditEvents` table for `RoleSwitched` and `AccountSwitched` entries

### Common Pitfalls:
- **Forgetting to revoke sessions on account switch** - Always call `SessionService.RevokeSessionAsync` before switching
- **Not validating PersonId on account switch** - Critical security check to prevent unauthorized access
- **Caching old permissions after role switch** - Must re-sign-in to update claims principal
- **Missing password validation for Admin elevation** - Always check `SignInManager.CheckPasswordSignInAsync` result

---

**Created:** 2025-11-29  
**Author:** HybridIdP Team  
**Status:** Phase 11.1-11.5 Planned, Phase 11.6+ Future Enhancements  
**Dependencies:** Phase 10.1-10.3 Complete (Person entity and services)

**Next Steps:** Implement Phase 10.4 (Person-centric profile migration) before starting Phase 11.1
