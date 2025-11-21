---
title: "Phase 8 — e2e Test Refactor"
owner: HybridIdP Team
date: 2025-11-21
---

# Phase 8 — e2e Test Refactor

This document tracks the Phase 8 effort to refactor and expand the E2E test suite, add helper functions, cover edge cases, and improve robustness.

## Objectives

- Centralize admin helper utilities (`e2e/tests/helpers/admin.ts`) to remove duplication.
- Add more CRUD tests for edge cases (validation errors, duplicates, regenerate secrets), and permission-denied tests.
- Add a fallback API deletion to ensure tests are robust against UI failures.
- Add negative tests and permission denied scenarios.
- Make tests resilient to different application flows (UI redirects to Access Denied vs in-place disabled buttons).

## Completed

- Implemented `e2e/tests/helpers/admin.ts` with helpers:
  - `loginAsAdminViaIdP`, `login` (explicit login page navigation)
  - `createRole`, `deleteRole`
  - `createUserWithRole` (now accepts role ID (GUID) as canonical input and resolves to role name internally)
  - `deleteUser` and `deleteClientViaApiFallback`
  - `regenerateSecretViaApi`
- Added test files:
  - `e2e/tests/helpers/admin.spec.ts` — helper smoke tests
  - `e2e/tests/admin-clients-negative.spec.ts` — validation & duplicate tests
  - `e2e/tests/admin-clients-regenerate-secret.spec.ts` — secret regeneration tests
  - `e2e/tests/admin-clients-permissions.spec.ts` — permission denied tests (robust to page redirect to Access Denied)
  - `e2e/tests/admin-users-role-assignment.spec.ts` — comprehensive role assignment tests (9 tests: 4 positive + 5 negative scenarios)
  - Updated `e2e/tests/admin-clients-crud.spec.ts` to use the helper
- Ensured tests are robust to both `role.id` and `role.name`, with a canonical preference (roleId).
- Updated `e2e/tests/helpers/README.md` to document helper API and the roleId canonicalization mechanism.
- Deleted `test-debug.ps1` and updated tests accordingly.
- **Backend Implementation: AssignRolesByIdAsync**
  - Implemented `IUserManagementService.AssignRolesByIdAsync` method accepting `IEnumerable<Guid> roleIds`
  - Resolves role IDs to names via `RoleManager.FindByIdAsync`, validates all IDs exist
  - Delegates to existing `AssignRolesAsync` for actual role assignment (maintains backward compatibility)
  - Returns detailed error messages for invalid role IDs
  - Added comprehensive unit tests (11 tests total: 7 for AssignRolesAsync + 4 for AssignRolesByIdAsync)
  - Added API endpoint: `PUT /api/admin/users/{id}/roles/ids` with `AssignRolesByIdRequest` DTO
  - Maintained backward compatibility with name-based endpoint: `PUT /api/admin/users/{id}/roles`

## In-Progress / Issues

- Permission test behavior: the app sometimes redirects to `/Account/AccessDenied` for read-only users on `Admin/Clients`. Tests were updated to accept either the Access Denied page or a filtered Admin Clients UI with disabled Create/Edit/Delete controls.

## TODOs

1. [x] ~~Add negative tests to assert invalid role IDs return 4xx when assigning via API.~~ **COMPLETED**
   - Added 5 comprehensive negative test scenarios in `e2e/tests/admin-users-role-assignment.spec.ts`
   - Tests cover: non-existent user (404), malformed GUID (400), mixed valid/invalid IDs (404), empty array (removes all roles), duplicate IDs (handled gracefully)
2. [x] ~~Add one or two integration tests verifying that role permissions are reflected in user claims after login (end-to-end) — this might require explicit token inspection.~~ **COMPLETED**
   - Implemented permission claims in OIDC tokens via `MyUserClaimsPrincipalFactory`, `Token.cshtml.cs`, and `Authorize.cshtml.cs`
   - Fixed permission claim reading from `role.Permissions` property (comma-separated string)
   - Added "permission" claim type to `GetDestinations` in both Token and Authorize endpoints
   - Created `e2e/tests/role-permissions-claims.spec.ts` with 2 comprehensive E2E tests:
     - Test 1: Verifies permissions appear in user claims after OIDC login
     - Test 2: Verifies permission changes after role reassignment with fresh browser context
   - Tests use separate browser contexts for admin setup and user login to avoid session contamination
3. [x] ~~Optionally, update the backend to provide a direct AssignRolesById endpoint to simplify test helper logic; see 'Backend suggestion' below.~~ **COMPLETED**
   - Implemented `AssignRolesByIdAsync` in `UserManagementService`
   - Added API endpoint `PUT /api/admin/users/{id}/roles/ids`
   - Comprehensive unit tests and E2E tests added
   - See implementation details in "Completed" section above
4. [ ] Consider splitting e2e suites by feature and running in parallel for speed.

## Backend Implementation Details — AssignRolesByIdAsync

**Status: ✅ COMPLETED**

The backend now supports accepting role IDs directly through a dedicated endpoint, eliminating the need for client-side role name resolution.

### Implementation

**API Endpoint:**

```http
PUT /api/admin/users/{userId}/roles/ids
Content-Type: application/json
Authorization: Required (Permissions.Users.Update)

{
  "RoleIds": ["<role-guid-1>", "<role-guid-2>"]
}
```

**Response:**

- `200 OK`: Returns updated `UserDetailDto` with new roles
- `400 Bad Request`: Malformed GUID format
- `404 Not Found`: User not found or invalid role ID(s) with detailed error messages
- `403 Forbidden`: Insufficient permissions

**Service Layer (`UserManagementService.AssignRolesByIdAsync`):**

1. Resolves each role ID to role name via `RoleManager.FindByIdAsync(roleId)`
2. Validates all role IDs exist before making any changes
3. Returns detailed error messages for invalid role IDs (e.g., "Role with ID 'xxx' not found")
4. Delegates to existing `AssignRolesAsync(userId, roleNames)` for actual role assignment
5. Maintains transactional integrity and domain event publishing

**Benefits Achieved:**

- Simplifies front-end and test flows (no need to fetch role names before assignment)
- Reduces API round-trips
- Maintains backward compatibility with name-based endpoint `PUT /api/admin/users/{id}/roles`
- Comprehensive error handling for edge cases
- Full unit test coverage (11 tests) and E2E test coverage (9 tests)

## Run & Validation

Run the following tests to validate the refactor:

```pwsh
npm run install:browsers # (from e2e folder) if needed
npm run test --prefix e2e
```

Or to run a subset:

```pwsh
npx playwright test e2e/tests/helpers/admin.spec.ts e2e/tests/admin-clients-permissions.spec.ts
```

## Test Coverage Summary

### E2E Tests (`e2e/tests/admin-users-role-assignment.spec.ts`)

**9 comprehensive tests covering:**

**Positive Scenarios (4 tests):**

- Assign multiple roles using role IDs endpoint
- Handle invalid role ID errors correctly
- Maintain backward compatibility with name-based endpoint
- Switch between ID-based and name-based endpoints

**Negative Scenarios (5 tests):**

- Return 404 for non-existent user
- Return 400 for malformed role ID (invalid GUID format)
- Return 404 for mixed valid and invalid role IDs
- Successfully remove all roles with empty array
- Handle duplicate role IDs gracefully

### Unit Tests (`Tests.Application.UnitTests/UserManagementTests.cs`)

**11 tests for role assignment:**

- `AssignRolesAsync` (7 tests): user not found, empty roles list, invalid role name, Identity errors, idempotency, event verification
- `AssignRolesByIdAsync` (4 tests): valid IDs, invalid ID, all invalid IDs, delegation verification

## Notes and Next Steps

- The `createUserWithRole` helper in `e2e/tests/helpers/admin.ts` intelligently detects GUID format and uses the appropriate endpoint (ID-based for GUIDs, name-based for strings)
- Both API endpoints are fully functional and tested, providing flexibility for different client needs
- Next: Consider adding integration tests for role permissions in user claims after login (TODO #2)
