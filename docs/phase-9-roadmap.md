# Phase 9 Implementation Roadmap

## Overview
Phase 9 focuses on comprehensive scope authorization and management, enabling fine-grained access control with client-specific required scopes.

## Sub-phases Summary

### 9.1: Consent Page Required Scope Support ✅
**Status:** Complete (2025-11-26)  
**Focus:** Database + Backend + Consent UI  
**Duration:** ~4 hours  
**Key Deliverables:**
- ✅ ClientRequiredScope entity & migration (SQL Server + PostgreSQL)
- ✅ Service methods for managing required scopes (Get/Set/IsRequired)
- ✅ Consent page UI with disabled required scopes (global + client-specific merge)
- ✅ Server-side validation against tampering (audit logging)

**Verification:**
- ✅ Unit tests for service methods (15/15 passed)
- ✅ Integration tests for ClientRequiredScope (10/10 passed)
- ✅ E2E test: consent flow with required scopes (3/3 auth tests passed)

---

### 9.2: Scope Authorization Handler & Policy Provider ✅
**Status:** Complete (2025-11-26)  
**Focus:** Authorization infrastructure  
**Duration:** ~4 hours  
**Key Deliverables:**
- ✅ ScopeRequirement class implementing IAuthorizationRequirement
- ✅ ScopeAuthorizationHandler with OAuth2 "scope" and Azure AD "scp" claim support
- ✅ ScopeAuthorizationPolicyProvider for dynamic policy creation ("RequireScope:{scopeName}" pattern)
- ✅ Test controller (ScopeProtectedController) demonstrating attribute usage
- ✅ DI registration in Program.cs

**Verification:**
- ✅ Unit tests: ScopeAuthorizationHandlerTests (14/14 passed)
- ✅ Unit tests: ScopeAuthorizationPolicyProviderTests (14/14 passed)
- ✅ Integration tests: ScopeAuthorizationIntegrationTests (14/14 passed)
- ✅ Total: 42/42 tests passing

---

### 9.3: OpenID Userinfo Endpoint Scope Protection ✅
**Status:** Complete (2025-11-26)  
**Focus:** OIDC compliance  
**Duration:** ~1 hour  
**Key Deliverables:**
- ✅ Protected /connect/userinfo with [Authorize(Policy = "RequireScope:openid")]
- ✅ Verified with existing E2E test (TestClient login flow includes userinfo call)

**Verification:**
- ✅ E2E test: userinfo succeeds with openid scope (83/84 tests passing)
- ⏳ E2E test: userinfo fails without openid scope (deferred to Phase 9.4)
  - **Reason:** openid is globally required in DB, cannot be unchecked on consent page
  - **Solution:** After Phase 9.4 Admin UI implementation, remove global requirement and add negative test

---

### 9.4: Client Scope Management UI Optimization
**Focus:** Admin UI enhancement  
**Duration:** ~2-3 days  
**Key Deliverables:**
- Refactored client scope management UI
- Allowed/Required dual-column interface
- Search and pagination for large scope lists
- Client/server validation

**Verification:**
- Create/update client with required scopes
- Validation: required must be in allowed
- UI performance with 50+ scopes
- Search/filter functionality

---

### 9.5: Modal/Dialog UX Consistency
**Focus:** UX polish  
**Duration:** ~1-2 days  
**Key Deliverables:**
- Audit all modals in admin UI
- Implement ESC key handler
- Add close icon to all modals
- Standard modal component/pattern

**Verification:**
- Inventory of all modals completed
- ESC key works on all modals
- Close icon present on all modals
- E2E tests for modal behavior

---

### 9.6: E2E Testing & Documentation
**Focus:** Quality assurance & documentation  
**Duration:** ~1-2 days  
**Key Deliverables:**
- Comprehensive E2E test suite for scope flows
- Updated ARCHITECTURE.md
- New SCOPE_AUTHORIZATION.md guide
- Code examples and troubleshooting

**Verification:**
- All E2E tests pass (new + existing)
- Documentation reviewed and accurate
- Code examples tested
- README updated

---

## Total Estimated Duration
**8-11 days** (assuming one developer)

## Current Status
- ✅ Phase 9.1: Complete (2025-11-26)
- ✅ Phase 9.2: Complete (2025-11-26)
- ✅ Phase 9.3: Complete (2025-11-26)
- 📊 Progress: 50% (3/6 sub-phases completed)
- ⏳ Next: Phase 9.4 - Client Scope Management UI Optimization

## Implementation Strategy
1. **TDD Approach:** Write tests first, then implementation
2. **Incremental:** Complete one sub-phase before moving to next
3. **Validation:** Verify each sub-phase with all test types (unit, integration, E2E, manual)
4. **Documentation:** Update docs as features are implemented

## Success Metrics
- All unit tests pass
- All integration tests pass
- All E2E tests pass (including new scope tests)
- Manual testing confirms expected behavior
- Documentation complete and accurate
- Code reviewed and merged to master

## Notes
- Each sub-phase is independently testable
- Can pause between sub-phases for feedback
- Phase 9.5 (Modal UX) can be done in parallel with 9.4
- Phase 9.6 (E2E + Docs) should be last to capture all changes
