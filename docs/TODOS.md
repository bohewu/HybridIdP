---
title: "TODOs & Backlog"
owner: HybridIdP Team
last-updated: 2026-10-17
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

## Completed: Phase 22 - App-Specific Roles & Permission Isolation (Completed)
- [x] **Data Model & Schema**
  - [x] Create `UserAppRole` entity (UserId, ClientId, RoleName)
  - [x] Add EF Core Migrations (PostgreSQL & SQL Server)
- [x] **Token Enrichment Logic**
  - [x] Refactor `ClaimsEnrichmentService` for conditional permission filtering
  - [x] Implement `AddAppSpecificRolesAsync` with `client_id` context
  - [x] Support Role Mapping/Transformation
- [x] **Client Manifest**
  - [x] Extend Client Registration (OpenIddict Application Properties) to define `SupportedRoles`
- [x] **UI Integration**
  - [x] User/Person Management: App-specific role assignment UI
- [x] **Testing & Verification**
  - [x] Update `ClientSeeder` for system test compatibility
  - [x] Verify permission isolation via JWT inspections

## 📋 Planned Follow-ons: HIDP-20260806-1 to HIDP-20260806-4 Upstream Authentication and Credential Migration

Provider Proof 1.0 API login and staged directory integration/credential
migration now have delivered implementations. Local-password accounts remain
independent; the flat LegacyAuth adapter/settings are retired. See the current
[authentication integration guide](AUTHENTICATION_INTEGRATION.md) and
[Phase 23 status](design_specs/phase-23-ad-integration-plan.md). All directory
stage switches default off. Implementation presence and fixture tests do not
establish connected directory acceptance or deployment activation.

- [x] **HIDP-20260806-4 -- Documentation specification**: The generic boundary
  and one-time ceremony are documented; stable trace IDs remain in Phase 23.
- [x] **HIDP-20260806-2 -- Delivered staged implementation**: `IProofProvider`,
  Stage 1 binding, `Stage2CredentialMigrationService`, durable migration state
  and protected LDAP transport exist. This status does not mark every future
  generic provider extension or deployment configuration complete.
- [ ] **Generic direct AD/LDAP beyond staged migration**: Specify and implement
  any separately approved provider extension; retain one selected credential
  authority, fail-closed handling, local lifecycle/MFA and directory-owned
  password policy.
- [ ] **HIDP-20260806-3 -- Connected non-production directory validation and
  activation**: Separately approve capability checks for exact managed lookup,
  constrained reset, independent bind, migration denial/replay and uncertain
  commit/recovery; retain sanitized evidence and operator rollout/rollback gates.

The boundary is generic OSS guidance: it must not depend on organization-
specific source systems, APIs, schemas, identifiers, databases, or policy.

## Partly delivered: Phase 24 - Enterprise Observability & Self-Service
- See [Phase 24 status](design_specs/phase-24-observability.md)
- [ ] **User Self-Service**: "My Sessions" UI in Profile App
  - [ ] `GET /api/profile/sessions` endpoint
  - [ ] `DELETE /api/profile/sessions/{id}` (Revocation logic)
  - [ ] `SessionList.vue` component
- [x] **Admin Observability**: Audit viewer in Admin App
  - [x] `GET /api/admin/audit/events` with filtering/pagination, event types and per-event export
  - [x] `admin/audit/AuditApp.vue` and `components/AuditLogViewer.vue`

### Retained completed Phase 22 checklist

- [x] **Data Model & Schema**
  - [x] Create `UserAppRole` entity (UserId, ClientId, RoleName)
  - [x] Add EF Core Migrations (PostgreSQL & SQL Server)
- [x] **Token Enrichment Logic**
  - [x] Refactor `ClaimsEnrichmentService` for conditional permission filtering
  - [x] Implement `AddAppSpecificRolesAsync` with `client_id` context
  - [x] Support Role Mapping/Transformation
- [x] **Client Manifest**
  - [x] Extend Client Registration (OpenIddict Application Properties) to define `SupportedRoles`
- [x] **UI Integration**
  - [x] User/Person Management: App-specific role assignment UI
- [x] **Testing & Verification**
  - [x] Update `ClientSeeder` for system test compatibility
  - [x] Verify permission isolation via JWT inspections

## Pending activation: Provider Lifecycle

- [x] Default-disabled consumer, strict HTTP validation and local/remote eligibility checkpoints.
- [ ] Select authoritative producer/source, approved mapping and explicit account cohort.
- [ ] Connected interoperability/source validation, clock assurance and operator-approved enablement/rollback.
- See the [lifecycle integration handoff](implementation_plans/provider-api-lifecycle-plan.md); synthetic tests do not establish deployed authority.

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
