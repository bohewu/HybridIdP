# User App Role Assignment Implementation Plan

## Goal Description
Enable administrators to assign Client-specific Roles (`UserAppRole`) to users. This allows users to have different roles in different applications (e.g., "Editor" in Client A, "Viewer" in Client B), which are then issued as `app_role` claims in the token.

## User Review Required
> [!NOTE]
> This requires adding new API endpoints and a new UI component.

## Proposed Changes

### Backend (Core & Infrastructure)

#### [MODIFY] [IUserManagementService.cs](file:///c:/repos/HybridIdP/Core.Application/IUserManagementService.cs)
- Add `GetUserAppRolesAsync(Guid userId, string clientId)`
- Add `AssignUserAppRolesAsync(Guid userId, string clientId, List<string> roleNames)`

#### [MODIFY] [UserManagementService.cs](file:///c:/repos/HybridIdP/Infrastructure/Services/UserManagementService.cs)
- Implement the new methods.
- Logic:
    1. Validate User and Client exist.
    2. Get `SupportedRoles` from Client Properties.
    3. Validate that requested roles are present in `SupportedRoles`.
    4. Update `UserAppRole` table (Delete existing for this Client+User, Insert new).

#### [MODIFY] [UsersController.cs](file:///c:/repos/HybridIdP/Web.IdP/Controllers/Admin/UsersController.cs)
- `GET {id}/app-roles/{clientId}`: Get assigned roles.
- `PUT {id}/app-roles/{clientId}`: Assign roles.

### Frontend (Web.IdP)

#### [NEW] [ClientRoleAssignment.vue](file:///c:/repos/HybridIdP/Web.IdP/ClientApp/src/admin/users/components/ClientRoleAssignment.vue)
- A new component similar to `RoleAssignment.vue` but with a Client Selector.
- **Flow**:
    1. User selects a Client (dropdown).
    2. UI fetches `SupportedRoles` (from Client Manifest) and `AssignedRoles` (from UserAppRoles).
    3. User checks/unchecks roles.
    4. Save calls `PUT .../users/{id}/app-roles/{clientId}`.

#### [MODIFY] [UserForm.vue](file:///c:/repos/HybridIdP/Web.IdP/ClientApp/src/admin/users/components/UserForm.vue)
- Add a new "App Roles" tab or button to trigger the `ClientRoleAssignment` modal.

## Verification Plan

### Manual Verification
1.  **Configure Client**: Ensure "TestClient" has "Admin", "User" in `SupportedRoles`.
2.  **Assign Role**: Go to User Profile > App Roles > Select "TestClient" > Assign "Admin".
3.  **Verify UI**: Refresh page, verify "Admin" is checked for TestClient.
4.  **Verify Token**: Login to TestClient with that user. Check Token contains `app_role: Admin`.
