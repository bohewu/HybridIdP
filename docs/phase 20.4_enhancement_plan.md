# Phase 20.4 Enhancement: Configurable Strong Security Model for Passkeys

**Status**: ✅ COMPLETED (2025-12-18)
**Goal**: Implement a configurable security model where Passkeys can optionally be enforced as dependent on an existing MFA method (TOTP/Email), preventing MFA bypass and ensuring highest security standards.

---

## Overview

This enhancement introduces a "Strong Security" mode for Passkeys. When enabled, this mode treats Passkeys strictly as an **enhancement** to a foundational MFA method (TOTP or Email), rather than an independent login method.

### Key Features
1. **Configurable Policy**: Admins can toggle `RequireMfaForPasskey` in Security Settings.
2. **Registration Guard**: Users cannot register a Passkey unless they have enabled TOTP or Email MFA.
3. **Cascading Revocation**: If a user disables their last MFA method, their Passkeys are automatically revoked to prevent security degradation.
4. **Legacy Support**: Existing users who violate the policy (have Passkey but no MFA) are allowed to login but warned and restricted from adding new credentials.

---

## Technical Implementation Plan

### 1. Database & Domain Logic ✅
- [x] **Table Schema**: Add `RequireMfaForPasskey` (bool) column to `SecurityPolicies` table.
- [x] **Entity Update**: Update `SecurityPolicy` entity and DTOs.
- [x] **Data Migration**: Create and apply EF Core migrations for SQL Server and PostgreSQL.

### 2. Backend Logic (Security Enforcement) ✅
- [x] **Passkey Controller**: Update `MakeCredentialOptions` to check `RequireMfaForPasskey` policy.
    - If `true` and user has no MFA, return `403 Forbidden` with error message.
- [x] **MFA Service**: Update `DisableMfaAsync` (TOTP) and `DisableEmailMfaAsync`.
    - If `RequireMfaForPasskey` is `true`, check if the disabled method was the last active MFA.
    - If yes, **automatically delete** all `UserCredentials` (Passkeys) for the user.
    - Log warning: `PasskeysRevokedDueToMfaDisable`.
- [x] **Account Security API**: New `/api/account/security-policy` endpoint for user-facing policy.

### 3. Frontend UI (User Profile) ✅
- [x] **MfaSettings.vue**:
    - Fetch the `RequireMfaForPasskey` setting from backend.
    - **Reorder UI**: Move Passkey section to the bottom (below Email MFA).
    - **Conditional Logic**: Disable "Add Passkey" button if no MFA is active (when policy is on).
    - **Warning Alert**: Display a warning if the user is in a "Non-compliant" state (Has Passkey, No MFA, Policy On).

### 4. Frontend UI (Admin Console) ✅
- [x] **SecurityApp.vue**:
    - Add a toggle switch for "Require MFA for Passkeys".
    - Add localization for toggle (en-US, zh-TW).

---

## User Impact

| Scenario | Policy: OFF (Default) | Policy: ON (Strong Mode) |
| :--- | :--- | :--- |
| **Register Passkey** | Allowed anytime | **Blocked** unless TOTP/Email MFA is active |
| **Disable MFA** | Allowed, Passkeys remain | **Allowed**, but Passkeys are **Auto-Deleted** |
| **Login (Passkey Only)** | Allowed (Direct Login) | Allowed (Legacy users only) + Warning shown in settings |
| **Login (Password)** | Direct Login | Challenge MFA (if TOTP/Email active) |

## Verification Strategy ✅

1. **System Tests**: `PasskeyApiTests` - API enforcement tests (6/6 passed)
2. **System Tests**: `AccountSecurityApiTests` - Policy endpoint tests (2/2 passed)
3. **Vitest**: `MfaSettings.test.js` - Frontend component tests (6/6 passed)
4. **All Quick Tests**: 183/183 passed

