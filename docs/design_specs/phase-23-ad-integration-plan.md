# Phase 23: Active Directory Integration & Policy Enforcement

## Goal
Integrate Active Directory (AD) as a primary authentication source using direct LDAP calls. Implement "Policy Overlay" to enforce password expiration and history on the IdP side, compensating for lack of AD-side enforcement.

## Architecture

We will implement a "Shadow Account" model where AD is the source of truth for credentials, but the IdP maintains a local `ApplicationUser` (and `Person`) record for authorization, profile management, and policy tracking.

### Components
1.  **`IAdAuthenticationService`**: Interface for AD operations.
2.  **`AdAuthenticationService`**: Implementation using `System.DirectoryServices.Protocols` (Windows) or `Novell.Directory.Ldap.NET.Standard` (Cross-platform). *Decision: Use `Novell.Directory.Ldap.NET.Standard` for broader compatibility context, unless Windows-specific features are strictly required.*
3.  **`LoginService` Update**: Modify authentication flow: Local -> **AD** -> Legacy.
4.  **`JitProvisioningService`**: Reuse existing logic to create/update users from AD attributes.
5.  **Policy Enforcement**:
    *   **Expiration**: Check `pwdLastSet` during login.
    *   **History**: Store password history locally upon change.

---

## Proposed Changes

### 1. Project Dependencies (Web.IdP)
- [ ] Add NuGet package: `Novell.Directory.Ldap.NET.Standard` (Version 3.x)

### 2. Configuration (`appsettings.json`)
- [ ] Add `AdOptions` section:
    ```json
    "Ad": {
      "Enabled": true,
      "Server": "ad.example.com",
      "Port": 389, // or 636 for LDAPS
      "UseSsl": false,
      "SearchBase": "DC=example,DC=com",
      "ServiceAccountDn": "CN=IdPBindUser,CN=Users,DC=example,DC=com",
      "ServiceAccountPassword": "..."
    }
    ```

### 3. Core Interface (`Core.Application`)
- [ ] Create `Interfaces/IAdAuthenticationService.cs`:
    - `Task<AdAuthResult> ValidateAsync(string username, string password);`
    - `Task ChangePasswordAsync(string username, string oldPassword, string newPassword);`

### 4. Service Implementation (`Infrastructure`)
- [ ] Create `Services/AdAuthenticationService.cs`:
    - Implement `ValidateAsync`:
        - Connect to LDAP.
        - Bind with user credentials (verify password).
        - Search user to retrieve attributes (`givenName`, `sn`, `mail`, `pwdLastSet`, `objectGuid`).
        - Calculate `passwordAge` from `pwdLastSet`.
        - Return result (Success, InvalidCredentials, PasswordExpired, LockedOut).
    - Implement `ChangePasswordAsync`:
        - Bind with user credentials.
        - Execute password modification operation.
        - **Note**: Requires LDAPS (Port 636) usually.

### 5. Login Flow Modification (`Infrastructure/Services/LoginService.cs`)
- [ ] Inject `IAdAuthenticationService`.
- [ ] Update `AuthenticateAsync`:
    - Current: Check Local -> Check Legacy.
    - New: Check Local -> **Check AD** -> Check Legacy.
- [ ] Handle AD Success:
    - Call `JitProvisioningService.ProvisionExternalUserAsync` (Map AD attributes to `ExternalAuthResult`).
    - **Policy Check**: If `AdAuthResult.PasswordExpired`, abort login and return specific error instructing user to change password.

### 6. Password Management (`Web.IdP`)
- [ ] Update Password Change UI/API to handle AD users.
    - If user is AD-sourced, call `AdAuthenticationService.ChangePasswordAsync`.
    - **History Enforcement**:
        - Before calling AD change, hash new password.
        - Check against local `PasswordHistory`.
        - If valid, call AD change.
        - If AD success, save hash to `PasswordHistory`.

### 7. Security Policy Updates (`SecurityPolicy` Entity)
- [ ] Add property `bool ForcePasswordChangeOnFirstLogin { get; set; }` to `SecurityPolicy`.
- [ ] Update `ApplicationUser`:
    - Add `bool MustChangePassword { get; set; }`.
- [ ] Logic Update:
    - **Provisioning**: When a new user (Local or AD-Shadow) is created, set `MustChangePassword = SecurityPolicy.ForcePasswordChangeOnFirstLogin`.
    - **Login**: In `LoginService`, check `user.MustChangePassword`. If true, block login and return `LoginResult.PasswordChangeRequired()`.
    - **Password Change**: After successful password change, set `user.MustChangePassword = false`.

## Verification Plan
1.  **Mock AD**: potentially tricky to mock. Will rely on mocked `IAdAuthenticationService` for unit tests.
2.  **Manual Test**: Connect to a real (test) AD environment if available, or simulate with a local LDAP server (e.g., OpenLDAP or AD LDS) if possible.
3.  **Policy Test**:
    - Set `pwdLastSet` to old date in AD (simulation) -> Verify Login rejected.
    - Change password -> Verify History check.

