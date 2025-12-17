# Phase 20 — MFA & WebAuthn

**Status**: 75% (Phase 20.1, 20.2, 20.3, 20.4 Backend Complete)
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

### Phase 20.2: Email Architecture Enhancement (Queue System) ✅ COMPLETED
**Complexity**: ⭐⭐ Medium | **Completed**: 2025-12-16

> **Goal**: Replace synchronous SMTP sending with a robust BackgroundService queue (Producer-Consumer pattern) to improve performance and reliability.

- [x] **Infrastructure**:
  - [x] Add **Mailpit** to `docker-compose.dev.yml` for local testing.
  - [x] Implement `EmailQueue` (Singleton) using `System.Threading.Channels`.
  - [x] Implement `SmtpDispatcher` (Scoped) for actual SMTP transmission using MailKit.
  - [x] Implement `EmailQueueProcessor` (HostedService) to consume messages in background.
  - [x] Update `EmailService` to act as Producer (enqueue only).

### Phase 20.3: Custom JsonStringLocalizer Implementation ✅ COMPLETED
**Complexity**: ⭐⭐ Medium | **Completed**: 2025-12-17

> **Goal**: Replace unreliable `My.Extensions.Localization.Json` with custom lightweight JSON localizer.

**Implementation Details:**
- [x] **Core Components**:
  - [x] `JsonStringLocalizer`: IStringLocalizer implementation with multi-path search and caching.
  - [x] `JsonStringLocalizerFactory`: IStringLocalizerFactory with Options pattern.
  - [x] `JsonLocalizationOptions`: Configuration for ResourcesPath and AdditionalAssemblyPrefixes.
  - [x] `JsonLocalizationServiceExtensions`: DI registration via `AddJsonLocalization()`.
- [x] **Features**:
  - [x] Multi-path resource search (Production, Development, Cross-Project).
  - [x] Culture fallback support (zh-TW → zh → default).
  - [x] In-memory caching with ConcurrentDictionary.
  - [x] Assembly prefix scanning for Infrastructure/Resources.
- [x] **Testing**:
  - [x] Unit Tests: 13/13 passed (JsonStringLocalizer).
  - [x] Integration Tests: 3/3 passed (EmailTemplateLocalization).
  - [x] System Tests: 197/197 non-Slow tests passed.
- [x] **UX Improvements**:
  - [x] Enhanced homepage avatar (10x10, 2-letter initials, subtitle).
  - [x] Fixed MfaRateLimitTests test categorization.

### Phase 20.4: WebAuthn Passkey - Database & Backend ✅ COMPLETED
**Complexity**: ⭐⭐⭐ Medium-High | **Completed**: 2025-12-17

- [x] Install `Fido2.AspNet` NuGet package.
- [x] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName).
- [x] EF Core migration for `UserCredentials` table.
- [x] Fido2 configuration in DI (RelyingPartyId, Origins).
- [x] API: `POST /api/passkey/register-options` - Generate registration challenge.
- [x] API: `POST /api/passkey/register` - Store credential after browser attestation.
- [x] API: `POST /api/passkey/login-options` - Generate authentication challenge.
- [x] API: `POST /api/passkey/login` - Verify signature + SignIn.
- [x] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check.

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

