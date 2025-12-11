---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2025-12-11
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

## Short-term (high priority)

- [ ] Phase 5.6 Part 3: Frontend scope authorization UX improvements
- [ ] Implement Resource table usage for consent multi-language text (Part 2)

## Medium-term (tech debt)

- [ ] Add icon preview in Admin UI for Consent customization
- [ ] Implement Cancel/Disable logic for required scopes on consent screen
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

## Notes

- Security scanning scripts available: `CI/security-scan.ps1`, `CI/dependency-scan.ps1`
- Test project warnings suppressed via `<NoWarn>` in .csproj

---
_Last updated: 2025-12-11_

