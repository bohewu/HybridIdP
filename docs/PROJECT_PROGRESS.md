---
title: "Project Progress Summary"
owner: HybridAuthIdP Team
last-updated: 2026-01-14
---

# Project Progress

This document tracks the evolution of the HybridAuthIdP project across different phases.

## 🚀 Active Phase

- **Phase 22**: App-Specific Roles & Permission Isolation (🔄 In Progress) — `phase-22-app-roles.md`
  - **Goal**: Decouple IdP-internal permissions from external apps and implement client-specific roles.

## 📊 Phase Summary

- [x] **Phase 1-11**: Core Infrastructure, OIDC Flow, Admin UI, Account Balancing
- [ ] **Phase 12**: Admin API & HR Integration (📋 Planned)
- [x] **Phase 13-20**: OAuth Enhancements, Impersonation, Personnel Lifecycle, MFA & WebAuthn
- [x] **Phase 21**: External Identity Providers ✅
- [🔄] **Phase 22**: App-Specific Roles & Permission Isolation (In Progress)

---

## 🕒 Recent Activity Log

### [2026-01-09] Phase 21: External Identity Providers ✅

Phase 21 integrated external OIDC providers with the HybridIdP ecosystem, supporting seamless federation and security controls.

**Key Achievements:**
- 🎯 **Social Login**: Full integration with **Google** and **Microsoft** Accounts.
- 🎯 **Domain Restriction**: Support for Google Workspace `hd` (Hosted Domain) parameter.
- 🎯 **UX Consolidation**: "Force Account Selection" logic to prevent user sticking.
- 🎯 **Auto-Linking**: Intelligent account linking based on verified email matches.

---

### [2026-01-14] Phase 22: App-Specific Roles & Permission Isolation (Initiated) 🔄

Phase 22 addresses token bloat and security concerns by isolating internal permissions and introducing application-specific roles.

**Key Objectives:**
- 🎯 **Permission Isolation**: Ensure internal IdP permissions are only issued to the Admin portal.
- 🎯 **App-Specific Roles**: Introduced `UserAppRole` for fine-grained, client-scoped role assignments.
- 🎯 **Contextual Issuance**: token roles are now filtered by the requesting `client_id`.
- 🎯 **Role Mapping**: Translation of `App:Role` to standard OIDC `role` claims.

**Status:**
- Planning & Architecture: ✅ Complete
- Implementation Plan: ✅ Approved
- Data Model: 🔄 In Progress

---

<details>
<summary><b>Historical Archive (Phases 1 - 20)</b></summary>

#### Phase 20: MFA & WebAuthn (2025-12) ✅
- **20.6**: OIDC AMR Claims & Zero Trust MFA enforcement.
- **20.5**: Security Automation (OWASP ZAP) & `ITimeProvider` refactor.
- **20.4**: WebAuthn/Passkey backend and UI (Fido2).
- **20.1-20.3**: TOTP MFA, Email Queue System, Custom JSON Localizer.

#### Phase 18: Personnel Lifecycle Management ✅
- Implemented `PersonStatus`, automated activation/termination, and token revocation.

#### Phase 16: User Impersonation ✅
- Secure "Login As" feature for administrators with full audit logging.

#### Phase 11.6: Security Hardening & UI Refactor ✅
- CSP compliance, secure cookie policies, and homepage redesign.

#### Phase 10: Person & Identity ✅
- Migrated to person-centric identity model supporting multiple accounts.

#### Phase 1-9: Foundation ✅
- PostgreSQL/EF Core, OpenIddict Integration, Admin UI, Scope Authorization.

</details>

---
_Last updated: 2026-01-14_
