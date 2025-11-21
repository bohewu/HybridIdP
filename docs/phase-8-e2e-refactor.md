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
  - Updated `e2e/tests/admin-clients-crud.spec.ts` to use the helper
- Ensured tests are robust to both `role.id` and `role.name`, with a canonical preference (roleId).
- Updated `e2e/tests/helpers/README.md` to document helper API and the roleId canonicalization mechanism.
- Deleted `test-debug.ps1` and updated tests accordingly.

## In-Progress / Issues

- Permission test behavior: the app sometimes redirects to `/Account/AccessDenied` for read-only users on `Admin/Clients`. Tests were updated to accept either the Access Denied page or a filtered Admin Clients UI with disabled Create/Edit/Delete controls.

## TODOs

1. [ ] Add negative tests to assert invalid role IDs return 4xx when assigning via API.
2. [ ] Add one or two integration tests verifying that role permissions are reflected in user claims after login (end-to-end) — this might require explicit token inspection.
3. [ ] Optionally, update the backend to provide a direct AssignRolesById endpoint to simplify test helper logic; see 'Backend suggestion' below.
4. [ ] Consider splitting e2e suites by feature and running in parallel for speed.

## Backend Suggestion — Role ID Support

The `AssignRolesAsync` flow currently expects role names (Identity `AddToRolesAsync`). This is natural for the server side because it manipulates role membership by name.

Suggestion: add backend support to accept role IDs in the `AssignRoles` endpoint (API) and internally resolve IDs to names right before `AddToRolesAsync`. This will:
- Simplify front-end / test flows (role list often returns IDs; it avoids the need to explicitly fetch the role name first).
- Reduce round-trips or client-side resolve logic in tests and scripts.

Implementation notes:
- Add a server-side path that accepts role IDs and resolves to the name with a call to `RoleManager.FindByIdAsync(roleId)`.
- Re-use existing `AssignRolesAsync` behavior to manipulate roles by name after lookup, and maintain backward compatibility with name-based requests.

### AssignRolesById Endpoint (optional implementation plan)

To make the API simpler for clients and e2e tests, consider adding an endpoint that accepts role IDs directly and resolves them on the server. Example:

```http
PUT /api/admin/users/{userId}/roles/ids
Content-Type: application/json

{
  "RoleIds": ["<role-guid-1>", "<role-guid-2>"]
}
```

Server-side handling (sketch):
 - Validate the user exists and caller has users.update permission.
 - For each roleId in RoleIds, call `roleManager.FindByIdAsync(roleId)` to obtain the role name.
 - Build the list of role names and call existing `AssignRolesAsync(userId, roleNames)` to update roles.
 - Return 200 on success, 4xx (400/404/403) on errors.

Benefits:
- Simplifies front-end and tests since `Role` search/list APIs commonly return both `id` and `name`, where `id` is more reliably used in code flow.
- Avoids extra client-side round-trips or helper code to resolve names before calling APIs.

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

## Notes and Next Steps

- If you prefer the backend to only accept role ID, we can implement that and then simplify tests by removing the helper resolution logic.
- If desired, I can add the backend `AssignRolesById` endpoint and update helper/clients to call it.
