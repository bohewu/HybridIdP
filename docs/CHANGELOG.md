# Changelog

This file tracks notable changes and releases for HybridAuth IdP.

## [1.5.0] - 2026-09-21

This release finalizes the 1.5.0 source tree. Container publication is performed
by the tag workflow; production migration and deployment remain separate release
steps and are not claimed by this entry.

### Added

- Added native password recovery with recovery-email proof, OTP and
  administrator-assistance ceremonies, required-password-change handling,
  one-time temporary credentials, pending directory-operation settlement and
  configurable OSS-neutral recovery guidance.
- Added default-disabled directory credential migration and recovery paths with
  exact provider-scoped identity bindings, protected directory transports and
  operator-controlled schema migration support for SQL Server and PostgreSQL.
- Added Provider Proof Contract 1.0, the affiliation-free Provider Metadata
  Contract 1.0 with JSON Schema and fixtures, and the independently unversioned
  Legacy Password Sync aggregate contract.

### Changed

- Upgraded the .NET servicing, identity, observability, frontend and test
  dependency sets, including Tailwind CSS 4.3.
- Decoupled Provider Metadata from IdM and affiliation ownership. Withdrawn
  draft affiliation data remains quarantined and is neither imported nor used
  as identity, role, group or linking authority.
- Hardened scheduled Person lifecycle processing with cancellation propagation,
  bounded batches and durable recovery for incomplete token revocation.
- Reduced the published application dependency surface by referencing only the
  required OpenIddict server and validation packages and excluding EF Design
  runtime assets.

### Security

- Preserved default-off activation, fail-closed provider selection, bounded
  timeouts, durable claim-before-dispatch, replay suppression and the
  no-auto-retry barrier for unknown or possibly dispatched credential writes.
- Recovery and lifecycle transitions preserve local password-hash ownership,
  rotate security state and revoke sessions, authorizations and tokens at the
  established completion boundaries.
- A Person cannot be restored to Active while scheduled token-revocation
  recovery remains pending, including when the proposed activation date is in
  the future.

### Deployment notes

- This release contains additive migrations for both supported database
  providers. Production must use the documented backup-first, schema-only
  migration procedure with the exact image later selected for deployment.
- Directory integration, credential migration, Provider Metadata refresh and
  Legacy Password Sync remain disabled until their independent configuration,
  connected-validation and rollout gates are explicitly approved.
- Local tests and release-candidate image verification do not establish
  production deployment or external-provider interoperability.

## 2025-12-05 — Email Settings Feature — Completed ✅
- Implemented `IEmailService` using MailKit/MimeKit.
- Added Email Settings section in Admin UI (Host, Port, Credentials, SSL, From Address).
- Added "Test Email" functionality in Admin UI.
- Secured settings storage with `ISettingsService`.
- Added unit tests for EmailService logic.

## 2025-11-29 — Phase 9 — Scope Authorization & Management — Completed ✅
- Completed Phase 9.7: E2E tests and documentation for scope authorization flows
- Fixed critical OAuth consent POST bug (scope inputs inside form, resolved redirect loop)
- Improved tampering detection and audit logging for consent submissions
- Admin UI: Client Required Scopes configuration and UI improvements
- E2E tests: All 102 tests passing; feature-auth: 16/16 passing

> See `./archive/phases/phase-9-scope-authorization.md` and `./PROJECT_PROGRESS.md` for details.

---
