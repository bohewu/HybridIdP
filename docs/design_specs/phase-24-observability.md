# Phase 24: Enterprise Observability & Self-Service

## Delivery status (2026-10-17)

Partly delivered. `Web.IdP/Controllers/Admin/AuditController.cs` and
`admin/audit/AuditApp.vue` / `components/AuditLogViewer.vue` implement the admin
Audit viewer. Its search endpoint is `GET /api/admin/audit/events`; event types
and per-event export are also implemented. The proposed Profile My Sessions
endpoints and `SessionList.vue` below are still pending: current
`ProfileManagementController` and the Profile SPA do not expose them. Existing
admin user-session management is a separate surface.

The My Sessions section is a proposal, including its revocation semantics;
this plan does not establish runtime acceptance of those proposed behaviors.

## Goal
Enhance system transparency and security visibility by exposing existing backend data to the UI.
1.  **User Self-Service**: Allow users to view and manage their active login sessions ("My Sessions").
2.  **Admin Observability**: Provide a searchable interface for system audit logs ("Audit Trail").

## 1. My Sessions (User Security Center)

### Feature Description
A new tab in the User Profile (`/profile/security`) showing a list of active sessions (devices) linked to the user's account.

### Implementation Details
*   **Backend (`ProfileController`)**:
    *   `GET /api/profile/sessions`: Returns list of `UserSession` for current user.
        *   Map fields: `DeviceInfo`, `IpAddress`, `LastActivityUtc`, `CreatedUtc`, `IsCurrent` (match current session ID).
    *   `DELETE /api/profile/sessions/{sessionId}`: Revoke a specific session.
        *   **Action**: 
            1.  Mark `UserSession` as revoked.
            2.  Revoke OpenIddict tokens associated with `AuthorizationId`.
            3.  (Optional) Rotate User Security Stamp if "Revoke All" is clicked.
*   **Frontend (`ProfileApp`)**:
    *   New Component: `SessionList.vue`.
    *   Display cards/table with icons (Desktop/Mobile), Location (GeoIP if available, or just IP), and "Active Now" indicator.
    *   "Revoke" button for non-current sessions.

## 2. Audit Logs (Admin Console)

### Feature Description
A centralized Audit Log viewer in the Admin Portal for investigating security events and administrative actions.

### Implementation Details
*   **Delivered backend (`AuditController`)**:
    *   `GET /api/admin/audit/events`: Filtered, paginated search.
    *   `GET /api/admin/audit/event-types`: Available event types.
    *   `GET /api/admin/audit/events/{id}/export`: Per-event export.
*   **Delivered frontend**:
    *   `admin/audit/AuditApp.vue` hosts `components/AuditLogViewer.vue`.
    *   Search/filter controls, result details and export use the existing admin permissions.

## Proposed My Sessions: Session Revocation Considerations
*   **Scope**: Revocation applies primarily to the **IdP Session** and **Token Refresh capability**.
*   **Effect**:
    *   **IdP**: The session cookie is invalidated. The next time the browser hits the IdP, the user is redirected to Login.
    *   **Downstream Apps (RPs)**: 
        *   Existing **Access Tokens** (JWTs) remain valid until they naturally expire (typically 5-60 minutes).
        *   **Refresh Tokens** stop working immediately. When the App tries to get a new Access Token, it will fail, forcing the user to re-authenticate.
