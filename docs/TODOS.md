---
title: "TODOs & Backlog"
own er: HybridIdP Team
last-updated: 2025-12-03
---

# TODOs & Technical Backlog

This file replaces `backlog-and-debt.md`. It contains current high-priority backlog items and Phase 12 planning tasks extracted from `phase-12-admin-api-hr-integration.md` and related planning docs.

## Short-term (high priority)
- Phase 5.6 Part 3: Frontend scope authorization UX improvements
- Implement Resource table usage for consent multi-language text (Part 2)

## Medium-term (tech debt)
- Add icon preview in Admin UI for Consent customization
- Implement Cancel/Disable logic for required scopes on consent screen
- Fix intermittent failing tests in `SettingsServiceTests`

## Phase 12 (Planned / Extracted tasks)
- Phase 12.1: Admin API Endpoints (Person CRUD, User management, Role assignment)
- Phase 12.2: OAuth2 Client Credentials flow for machine-to-machine auth
- Phase 12.3: Webhook support for real-time HR sync events
- Phase 12.4: Bulk operations API (batch user provisioning, bulk role updates)
- Phase 12.5: Audit logging for all admin API operations
- Phase 12.6: API rate limiting and IP whitelisting
- Phase 12.7: Reconciliation API for periodic full sync
- Phase 12.8: External IdP integration (LDAP/AD federation)
- Phase 12.9: SSO Entry Portal
  - Create standalone SSO Portal app (Next.js/React/Vue)
  - Register as OIDC client to IdP
  - Display app catalog with role-based filtering and Launch buttons

## Notes
- Extract outstanding TODOs from `docs/PROJECT_STATUS.md` before that file is archived.
- Use this file for sprint planning and issue creation.

---
_Last updated: 2025-12-03_
