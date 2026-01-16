---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2026-01-14
---

# TODOs & Technical Backlog

This file contains the roadmap for upcoming features and technical improvements.

## 🔄 Active: Phase 22 - App-Specific Roles & Permission Isolation

- [x] **Data Model & Schema**
  - [x] Create `UserAppRole` entity (UserId, ClientId, RoleName)
  - [x] Add EF Core Migrations (PostgreSQL & SQL Server)
- [x] **Token Enrichment Logic**
  - [x] Refactor `ClaimsEnrichmentService` for conditional permission filtering
  - [x] Implement `AddAppSpecificRolesAsync` with `client_id` context
  - [x] Support Role Mapping/Transformation
- [x] **Client Manifest**
  - [x] Extend Client Registration (OpenIddict Application Properties) to define `SupportedRoles`
- [x] **UI Integration** (Next Session)
  - [x] User/Person Management: App-specific role assignment UI
- [x] **Testing & Verification**
  - [x] Update `ClientSeeder` for system test compatibility
  - [x] Verify permission isolation via JWT inspections

---

## 📅 Short-term Backlog (High Priority)

- [ ] **Phase 12: Admin API Enhancements**
  - [ ] Webhook support for real-time HR sync events
  - [ ] Bulk operations API (batch user provisioning)
  - [ ] Reconciliation API for periodic full sync

---

## 🛠️ Tech Debt & Optimization

- [ ] **Performance**
  - [ ] `AuthorizationService`: Add `IMemoryCache` for Client/Scope lookups.
  - [ ] Evaluate Scope caching strategy in `LoadScopeInfosAsync`.
- [ ] **Quality**
  - [ ] Refactor legacy `AdminController` endpoints into specific services.
  - [ ] Improve parallel execution stability of OIDC System Tests.

---

<details>
<summary><b>Recently Completed (History)</b></summary>

- [x] **Phase 21**: External IdP Federation (Google & Microsoft)
- [x] **Phase 20**: MFA & WebAuthn Complete (TOTP, Passkeys, AMR Claims)
- [x] **Phase 19**: Vitest Strategy & System Test Overhaul
- [x] **Phase 18**: Personnel Lifecycle Management
- [x] **Phase 16**: User Impersonation (TestClient.Impersonation)
- [x] **OAuth2 Enhancements**: M2M, Device Flow, Client Credentials
- [x] **Security**: Global CSRF Protection, Google Style 2024 UI Refactor

</details>

---
_Last updated: 2026-01-14_
