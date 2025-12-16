---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2025-12-15
---

# TODOs & Technical Backlog

This file contains current high-priority backlog items and remaining tasks.

## ✅ Recently Completed

- [x] Phase 12.2: OAuth2 Client Credentials flow (M2M auth) - `TestClient.M2M`
- [x] Phase 12.5: Audit logging for admin API operations - `AuditService`
- [x] Phase 12.6: API rate limiting - `RateLimitingOptions`, `TokenController`
- [x] Phase 16: User Impersonation - `TestClient.Impersonation`
- [x] Device Flow implementation - `TestClient.Device`
- [x] PII Masking for audit logs - `PiiMasker`, `AuditOptions`
- [x] Configurable Audit Retention via SystemSettings UI
- [x] Implement Resource table usage for consent multi-language text - `AuthorizationService`, `LocalizationService`

## ✅ UI/UX Refactoring (Google Style 2024) - COMPLETE

- [x] Login Page (Layout, Button text, Footer, Full-bleed background)
- [x] Register Page (Dynamic Password Policy, Localized Validation Messages)
- [x] Logout & Access Denied Pages (Converted to standalone `_SimpleLayout`)
- [x] Shared Footer (Localized `_GoogleFooter` with Language Selector)
- [x] Connect/Logout View (OIDC Logout styling)
- [x] Main Layout (`_Layout.cshtml`) Refactoring
- [x] Vue components: Google Account Style with Tailwind CSS v4
- [x] `_AdminLayout.cshtml` & `_ApplicationManagerLayout.cshtml` consistency
- [x] Global Design Tokens update
- [x] CookieConsent banner refactoring with localization

## Short-term (high priority)

### Phase 19: Frontend Testing Strategy (Vitest) & System Tests ✅ COMPLETED
- [x] Phase 19.1: Vitest Setup & Configuration
  - [x] Configure Vitest in `Web.IdP/ClientApp`
  - [x] Setup `happy-dom`
- [x] Phase 19.2: Test High-Value Composables (Logic)
  - [x] `usePasswordValidation`, `useIdentityValidation` (Taiwan ID validation fix)
- [x] Phase 19.3: Test Complex Components (High Risk)
  - [x] Form validations (UserForm)
  - [x] Permission service tests
- [x] Phase 19.4: Verify OIDC Flows via SystemTests
  - [x] Device Flow (`DeviceFlowSystemTests.cs`)
  - [x] Client Credentials (`M2MSystemTests.cs`)
  - [x] Resource Owner Password / Legacy (`LegacyAuthSystemTests.cs`)
  - [x] Authorization Code Flow smoke tests (`AuthCodeSystemTests`)
  - [x] WebIdPServerFixture for auto server lifecycle
- [x] Phase 19.5: API Endpoint Tests
  - [x] Admin API endpoint validation (`AdminApiTests`)
  - [x] Token endpoint validation
  - Consolidated into SystemTests (KISS principle)


## Medium-term (tech debt)

- [x] ~~Add icon preview in Admin UI for Consent customization~~ (Done in `ScopeForm.vue`)
- [x] ~~Implement Cancel/Disable logic for required scopes on consent screen~~ (Done in `Authorize.cshtml`)
- [x] ~~Fix intermittent failing tests in `SettingsServiceTests`~~ (Resolved)

## Phase 12 (Remaining tasks)

- [x] Phase 12.1: Admin API Endpoints (Person CRUD, User management)
- [x] Phase 12.2: OAuth2 Client Credentials flow ✅
- [ ] Phase 12.3: Webhook support for real-time HR sync events
- [ ] Phase 12.4: Bulk operations API (batch user provisioning)
- [x] Phase 12.5: Audit logging ✅
- [x] Phase 12.6: API rate limiting ✅
- [ ] Phase 12.7: Reconciliation API for periodic full sync
- [ ] Phase 12.8: External IdP integration (LDAP/AD federation)
- [ ] Phase 12.9: SSO Entry Portal
  - Create standalone SSO Portal app
  - Register as OIDC client to IdP
  - Display app catalog with role-based filtering

## Phase 18 (Personnel Lifecycle) ✅ COMPLETED

- [x] Phase 18.1: Schema & Entity (Person Status, Dates)
- [x] Phase 18.2: Logic & Security (Login Checks, IPersonService)
- [x] Phase 18.3: Admin UI (Person List Status, Editing)
- [x] Phase 18.4: Automation (PersonLifecycleJob w/ Quartz)
- [x] E2E & System Tests verified (2.2s / 2.9s)

## Phase 20 (Multi-Factor Authentication)

### Phase 20.1: TOTP MFA Implementation ✅ COMPLETED

- [x] MfaService (TDD) - 11+ tests
- [x] MFA API endpoints (status, setup, verify, disable, recovery-codes)
- [x] Login flow with MFA redirect (LoginMfa.cshtml)
- [x] Recovery code verification 
- [x] MfaSettings.vue component in Profile page
- [x] Support for passwordless users (Legacy/SSO) - TOTP-based disable
- [x] Modular i18n files (mfa.json)
- [x] MfaApiTests system tests

### Phase 20.2: Email Architecture Enhancement (Queue System) ✅ COMPLETED
- [x] Add Mailpit to dev environment.
- [x] Implement `EmailQueue` (Channel-based) and `EmailQueueProcessor` (HostedService).
- [x] Implement `SmtpDispatcher` for actual sending.
- [x] Refactor `EmailService` to producer pattern.

### Phase 20.3: Email MFA (OTP) Logic (Pending)
- [ ] Backend: `EmailTwoFactorEnabled` field + `IEmailSender` extensions
- [ ] Logic: `EmailMfaProvider` for 6-digit code generation/validation
- [ ] API: `POST /api/account/mfa/email/send` & `verify`
- [ ] UI: Login fallback logic & Profile toggle

### Phase 20.4: WebAuthn Passkey - Database & Backend (Pending)
- [ ] Install `Fido2.AspNet` NuGet package
- [ ] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName)
- [ ] EF Core migration for UserCredentials table
- [ ] Fido2 configuration in DI (RelyingPartyId, Origins)
- [ ] API: `POST /api/passkey/register-options` - Generate registration challenge
- [ ] API: `POST /api/passkey/register` - Store credential after browser attestation
- [ ] API: `POST /api/passkey/login-options` - Generate authentication challenge
- [ ] API: `POST /api/passkey/login` - Verify signature + SignIn
- [ ] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check

### Phase 20.5: WebAuthn Passkey - Frontend UI (Pending)
- [ ] User Settings page: "Manage Passkeys" section
- [ ] "Add Passkey" button with WebAuthn registration flow
- [ ] List registered passkeys with device names
- [ ] Delete passkey functionality
- [ ] Login page: "Sign in with Passkey" option
- [ ] JavaScript integration with `navigator.credentials` API

### Phase 20.4: Testing & Documentation (Pending)
- [ ] Unit tests for TOTP validation
- [ ] Integration tests for Fido2 flows
- [ ] E2E tests with Playwright (using Chrome's Virtual Authenticator)
- [ ] Update `SECURITY.md` with MFA documentation
- [ ] User guide for setting up Passkeys

## Notes

- Security scanning scripts available: `CI/security-scan.ps1`, `CI/dependency-scan.ps1`
- Test project warnings suppressed via `<NoWarn>` in .csproj

---
_Last updated: 2025-12-13_

