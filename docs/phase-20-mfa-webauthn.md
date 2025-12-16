# Phase 20 — MFA & WebAuthn

**Status**: 30% (Phase 20.1 Complete)
**Goal**: Implement comprehensive Multi-Factor Authentication (TOTP, Email, WebAuthn).

---

## Sub-Phases

### Phase 20.1: TOTP (Time-based One-Time Password) ✅ COMPLETED
**Complexity**: ⭐⭐ Medium | **Completed**: 2025-12-16

**Implementation Details:**
- **Core Logic**: Used `OtpNet` for RFC 6238 TOTP generation and validation.
- **Database**: 
  - Added `TwoFactorEnabled` (bool) and `AuthenticatorKey` (string) to `AspNetUsers`.
  - Added `RecoveryCodes` (text) for storing 10 encrypted backup codes.
- **API Endpoints**:
  - `POST /api/account/2fa/setup`: Generates secret and QR code (rendered internally as base64).
  - `POST /api/account/2fa/verify`: Verifies code and enables 2FA.
  - `POST /api/account/2fa/disable`: Disables 2FA (requires password confirmation).
  - `POST /api/account/2fa/recovery-codes`: Generates new recovery codes.
- **UI/UX**:
  - **Login Flow**: Intercepts login process; if 2FA enabled, redirects to `LoginMfa` page.
  - **Profile Management**: New "Security" section in User Profile for managing 2FA.
  - **Admin Console**: Admins can view 2FA status and reset it for users (e.g., lost device).
- **Security**:
  - Rate limiting applied to verification endpoints.
  - Session security stamp refreshed upon successful MFA verification.
  - "Force Re-authentication" policy supported for sensitive actions.
- **i18n**: Modularized translations in `mfa.json`.

### Phase 20.2: Email Architecture Enhancement (Queue System) 📋 PLANNED
**Complexity**: ⭐⭐ Medium | **Estimate**: 1-2 days

> **Goal**: Replace synchronous SMTP sending with a robust BackgroundService queue (Producer-Consumer pattern) to improve performance and reliability.

- [ ] **Infrastructure**:
  - [ ] Add **Mailpit** to `docker-compose.dev.yml` for local testing.
  - [ ] Implement `EmailQueue` (Singleton) using `System.Threading.Channels`.
  - [ ] Implement `SmtpDispatcher` (Scoped) for actual SMTP transmission using MailKit.
  - [ ] Implement `EmailQueueProcessor` (HostedService) to consume messages in background.
  - [ ] Update `EmailService` to act as Producer (enqueue only).

### Phase 20.3: Email MFA (OTP) Logic 📋 PLANNED
**Complexity**: ⭐ Low-Medium | **Estimate**: 1-2 days

> **Goal**: Implement OTP generation/validation logic on top of the enhanced email infrastructure.

- [ ] **Data Model**:
  - [ ] Add `EmailTwoFactorEnabled` field to `ApplicationUser`.
  - [ ] EF Core Migration.
- [ ] **Logic**:
  - [ ] Create `EmailMfaProvider` logic (generate 6-digit numeric code, 5-10 min expiry).
  - [ ] Implement HTML Email Template for OTP codes.
- [ ] **API Endpoints**:
  - [ ] `POST /api/account/mfa/email/send`: Trigger code delivery.
  - [ ] `POST /api/account/mfa/email/verify`: Verify code and enable Email MFA.
- [ ] **UI**:
  - [ ] Profile: Enable/Disable Email MFA toggle.
  - [ ] Login: MFA method selection.

### Phase 20.4: WebAuthn Passkey - Database & Backend 📋 PLANNED
**Complexity**: ⭐⭐⭐ Medium-High | **Estimate**: 2-3 days

- [ ] Install `Fido2.AspNet` NuGet package.
- [ ] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName).
- [ ] EF Core migration for `UserCredentials` table.
- [ ] Fido2 configuration in DI (RelyingPartyId, Origins).
- [ ] API: `POST /api/passkey/register-options` - Generate registration challenge.
- [ ] API: `POST /api/passkey/register` - Store credential after browser attestation.
- [ ] API: `POST /api/passkey/login-options` - Generate authentication challenge.
- [ ] API: `POST /api/passkey/login` - Verify signature + SignIn.
- [ ] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check.

### Phase 20.4: WebAuthn Passkey - Frontend UI 📋 PLANNED
**Complexity**: ⭐⭐ Medium | **Estimate**: 1-2 days

- [ ] User Settings page: "Manage Passkeys" section.
- [ ] "Add Passkey" button with WebAuthn registration flow.
- [ ] List registered passkeys with device names.
- [ ] Delete passkey functionality.
- [ ] Login page: "Sign in with Passkey" option.
- [ ] JavaScript integration with `navigator.credentials` API.

---

## WebAuthn: How It Works for Users
*(Section functionality unchanged, see prev doc for details)*

### Supported Platforms
| Platform | Authenticator |
|----------|--------------|
| Windows 10/11 | Windows Hello (PIN/Fingerprint/Face) |
| macOS | Touch ID on MacBook / iCloud Keychain |
| iOS | Face ID / Touch ID + Safari |
| Android | Fingerprint / Face unlock + Chrome |

---

## Dependencies
- `Fido2.AspNet` (Pending)
- `OtpNet` (Implemented)

