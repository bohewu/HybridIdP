# Client Manifest (UI) Implementation Plan

## Goal Description
Implement the frontend UI to allow administrators to manage `SupportedRoles` for OIDC clients. This builds upon the recently completed backend support.

## User Review Required
> [!NOTE]
> Confirm if the `SupportedRoles` input should be a free-text tag input or restricted to a pre-defined list of system roles (or both). Assuming free-text tags for now as these are app-specific roles.

## Proposed Changes

### Frontend (Web.IdP)
#### [MODIFY] [ClientService.ts](file:///c:/repos/HybridIdP/Web.IdP/ClientApp/src/services/ClientService.ts) (Verify Path)
- Update `CreateClientRequest` and `UpdateClientRequest` interfaces to include `supportedRoles: string[]`.
- Update `ClientDetail` interface.

#### [MODIFY] [ClientForm.vue](file:///c:/repos/HybridIdP/Web.IdP/ClientApp/src/components/clients/ClientForm.vue) (Verify Path)
- Add a "Supported Roles" section.
- Use a tag-input component (e.g., `PrimeVue Chips` or similar) to allow adding/removing role strings.
- Bind to `model.supportedRoles`.

## Verification Plan

### Manual Verification
1.  Navigate to Admin > Clients.
2.  Edit a Client.
3.  Add "Admin", "User" to Supported Roles.
4.  Save.
5.  Refresh and verify roles persist.
