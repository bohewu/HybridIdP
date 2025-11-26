# Phase 9 Implementation Roadmap

## Overview
Phase 9 focuses on comprehensive scope authorization and management, enabling fine-grained access control with client-specific required scopes.

## Sub-phases Summary

### 9.1: Consent Page Required Scope Support
**Focus:** Database + Backend + Consent UI  
**Duration:** ~1-2 days  
**Key Deliverables:**
- ClientRequiredScope entity & migration
- Service methods for managing required scopes
- Consent page UI with disabled required scopes
- Server-side validation against tampering

**Verification:**
- Unit tests for service methods
- Manual test: required scope checkbox disabled
- E2E test: consent flow with required scopes

---

### 9.2: Scope Authorization Handler & Policy Provider
**Focus:** Authorization infrastructure  
**Duration:** ~1 day  
**Key Deliverables:**
- ScopeRequirement, ScopeAuthorizationHandler, ScopeAuthorizationPolicyProvider
- [Authorize(Policy = "RequireScope:...")] attribute support
- Registration in Program.cs

**Verification:**
- Unit tests: handler and policy provider
- Integration tests: in-memory API endpoint protection
- Test controller with protected endpoints

---

### 9.3: OpenID Userinfo Endpoint Scope Protection
**Focus:** OIDC compliance  
**Duration:** ~0.5 day  
**Key Deliverables:**
- Protect /connect/userinfo with openid scope requirement
- E2E tests for userinfo access control

**Verification:**
- E2E test: userinfo succeeds with openid scope
- E2E test: userinfo fails without openid scope

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
- ✅ Planning complete
- ✅ Documentation created
- ✅ Working tree clean (ready to start)
- ⏳ Next: Begin Phase 9.1 implementation

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
