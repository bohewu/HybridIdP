---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2026-08-12
---

# TODOs & Technical Backlog

This file contains the roadmap for upcoming features and technical improvements.

## 🔄 Active Maintenance: OpenIddict 7.6 and .NET 10 Servicing

- [x] Align all OpenIddict package references from 7.2.0 to the latest stable
  7.x release, 7.6.0.
- [x] Align directly referenced ASP.NET Core, EF Core, and IdentityModel
  packages required by the upgrade to compatible servicing releases.
- [x] Restore and build the full solution without package downgrade warnings or
  compilation errors.
- [x] Run the complete backend test suite and the existing OAuth/OIDC system
  coverage for authorization code with PKCE, client credentials, device,
  refresh, userinfo, revocation, and logout behavior.
- [x] Confirm that the package-only update produces no unexpected SQL Server or
  PostgreSQL model changes or migrations.
- [x] Verify the runtime patch in the released production image before rollout;
  package validation does not replace checking the container's actual .NET
  runtime.

## ⏸️ Deferred: OpenBao External Secret Provider

OpenBao remains a deferred external secret-source option, analogous to Azure
Key Vault configuration integration. Any future implementation belongs at the
application configuration boundary and must remain independent of OpenIddict's
application, authorization, scope, and token stores. Do not add an OpenBao
server, client package, Agent, deployment setting, or OpenIddict-specific
adapter until the OpenBao infrastructure owner, machine-authentication method,
secret inventory, availability policy, and reuse requirements are approved.

## 🔄 Active: Phase 22 - App-Specific Roles & Permission Isolation (Completed)
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

## 📋 Planned Follow-ons: HIDP-20260806-1 to HIDP-20260806-4 Upstream Authentication and Credential Migration

Current behavior remains Local plus configurable LegacyAuth HTTP authentication;
direct AD/LDAP is not implemented. The future preferred credential authority is
deployment-configured direct AD/LDAP. A standardized, provider-neutral
authentication/profile API adapter may be selected only when direct directory
access cannot supply a required capability. Provider selection must be explicit
and fail closed, with no automatic credential-authority fallback.

- [x] **HIDP-20260806-4 -- Documentation specification**: Document the future,
  generic OSS, one-time legacy-proof-to-directory-credential ceremony and its
  security invariants. This documentation-only item neither implements nor
  validates a provider, configuration, package, migration, test, or connected
  directory operation.
- [ ] **HIDP-20260806-2 -- Future provider contract and migration state-machine
  implementation/tests**: Define and implement the generic provider contract,
  direct AD/LDAP provider, and any explicitly justified API adapter, together
  with the one-time migration state machine and its focused tests. Preserve
  directory ownership of credentials, lockout, password expiration/change, and
  password policy; do not add an IdP-side directory password-history or
  password-policy overlay. Cover immutable provider-key linking,
  assurance-gated matching, local lifecycle precedence, MFA trust, claim
  allowlisting, cookie/token/upstream revalidation, and migration completion
  boundaries.
- [ ] **HIDP-20260806-3 -- Opt-in sanitized connected non-production directory
  validation**: After HIDP-20260806-2, plan and execute separately approved,
  opt-in scenarios in a non-production directory environment. The future
  scenarios must cover migration success, uniform denial, replay rejection,
  and uncertain-commit/recovery boundaries using sanitized evidence. This
  documentation item performs no connected validation.

The boundary is generic OSS guidance: it must not depend on organization-
specific source systems, APIs, schemas, identifiers, databases, or policy.

## 📋 Planned: Phase 24 - Enterprise Observability & Self-Service
- See `docs/design_specs/phase-24-observability.md`
- [ ] **User Self-Service**: "My Sessions" UI in Profile App
  - [ ] `GET /api/profile/sessions` endpoint
  - [ ] `DELETE /api/profile/sessions/{id}` (Revocation logic)
  - [ ] `SessionList.vue` component
- [ ] **Admin Observability**: Audit Logs UI in Admin App
  - [ ] `GET /api/admin/audit-logs` endpoint with filtering
  - [ ] `AuditLogList.vue` component

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
_Last updated: 2026-08-12_
