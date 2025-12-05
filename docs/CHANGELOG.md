# Changelog

This file tracks notable changes and releases for HybridAuth IdP.

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

