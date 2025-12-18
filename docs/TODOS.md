---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2025-12-18
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

### Phase 20.3: Custom JsonStringLocalizer Implementation ✅ COMPLETED
- [x] Remove unreliable `My.Extensions.Localization.Json` package
- [x] Backend: Custom `JsonStringLocalizer` with multi-path resource search
- [x] Backend: `JsonStringLocalizerFactory` with Options pattern
- [x] Backend: `JsonLocalizationOptions` for configuration  
- [x] Backend: `JsonLocalizationServiceExtensions` for DI registration
- [x] Configure `AdditionalAssemblyPrefixes` for Infrastructure resource scanning
- [x] Update `EmailTemplateService` to accept optional culture parameter
- [x] Unit Tests: 13 tests for JsonStringLocalizer (all passing)
- [x] Integration Tests: 3 tests for EmailTemplateLocalization (all passing)
- [x] UX: Enhanced homepage avatar to match admin layout style
- [x] Fix: MfaRateLimitTests test categorization ([Trait] instead of [Skip])

### Phase 20.4: WebAuthn Passkey - Database & Backend ✅ COMPLETED
- [x] Install `Fido2.AspNet` NuGet package
- [x] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName)
- [x] EF Core migration for UserCredentials table
- [x] Fido2 configuration in DI (RelyingPartyId, Origins)
- [x] API: `POST /api/passkey/register-options` - Generate registration challenge
- [x] API: `POST /api/passkey/register` - Store credential after browser attestation
- [x] API: `POST /api/passkey/login-options` - Generate authentication challenge
- [x] API: `POST /api/passkey/login` - Verify signature + SignIn
- [x] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check

### Phase 20.4: WebAuthn Passkey - Frontend UI ✅ COMPLETED
- [x] User Settings page: "Manage Passkeys" section
- [x] "Add Passkey" button with WebAuthn registration flow
- [x] List registered passkeys with device names
- [x] Delete passkey functionality
- [x] Login page: "Sign in with Passkey" option
- [x] JavaScript integration with `navigator.credentials` API

### Phase 20.4 Enhancement: Configurable Strong Security Model ✅ COMPLETED
- [x] Database: `RequireMfaForPasskey` in `SecurityPolicies`
- [x] Backend: Enforce MFA prerequisite for registration (API validation)
- [x] Backend: Auto-revoke passkeys when last MFA is disabled
- [x] Admin UI: Configurable toggle in Security Settings
- [x] User Profile UI: Disable button & warning for non-compliant users
- [x] API: `/api/account/security-policy` endpoint for frontend
- [x] System Tests: `PasskeyApiTests`, `AccountSecurityApiTests`

### Phase 20.2: Email Architecture Enhancement (Queue System) ✅ COMPLETED
- [x] Add Mailpit to dev environment.
- [x] Implement `EmailQueue` (Channel-based) and `EmailQueueProcessor` (HostedService).
- [x] Implement `SmtpDispatcher` for actual sending.
- [x] Refactor `EmailService` to producer pattern.

### Phase 20.3: Custom JsonStringLocalizer Implementation ✅ COMPLETED
- [x] Remove unreliable `My.Extensions.Localization.Json` package
- [x] Backend: Custom `JsonStringLocalizer` with multi-path resource search
- [x] Backend: `JsonStringLocalizerFactory` with Options pattern
- [x] Backend: `JsonLocalizationOptions` for configuration  
- [x] Backend: `JsonLocalizationServiceExtensions` for DI registration
- [x] Configure `AdditionalAssemblyPrefixes` for Infrastructure resource scanning
- [x] Update `EmailTemplateService` to accept optional culture parameter
- [x] Unit Tests: 13 tests for JsonStringLocalizer (all passing)
- [x] Integration Tests: 3 tests for EmailTemplateLocalization (all passing)
- [x] UX: Enhanced homepage avatar to match admin layout style
- [x] Fix: MfaRateLimitTests test categorization ([Trait] instead of [Skip])

### Phase 20.4: WebAuthn Passkey - Database & Backend ✅ COMPLETED
- [x] Install `Fido2.AspNet` NuGet package
- [x] Create `UserCredential` entity (CredentialId, PublicKey, SignCount, DeviceName)
- [x] EF Core migration for UserCredentials table
- [x] Fido2 configuration in DI (RelyingPartyId, Origins)
- [x] API: `POST /api/passkey/register-options` - Generate registration challenge
- [x] API: `POST /api/passkey/register` - Store credential after browser attestation
- [x] API: `POST /api/passkey/login-options` - Generate authentication challenge
- [x] API: `POST /api/passkey/login` - Verify signature + SignIn
- [x] Integrate with `ValidatePersonStatusAsync()` for Person Lifecycle check

### Phase 20.5: Testing & Documentation ✅ COMPLETED
- [x] E2E tests (Verified via Manual Testing)
- [x] Update `README.md` with WSL/Java prerequisites for ZAP
- [x] Integrate Radical/Deep ZAP Attack tests
- [x] Implement system-wide `ITimeProvider` for testability
- [x] Update `docs/SECURITY.md` with MFA documentation
- [x] User guide for setting up Passkeys (See `docs/MFA_TESTING_GUIDE.md`)

## Notes

- Security scanning scripts available: `CI/security-scan.ps1`, `CI/dependency-scan.ps1`
- Test project warnings suppressed via `<NoWarn>` in .csproj

---
_Last updated: 2025-12-18_

