# Phase 24: Enterprise Observability & Self-Service

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
*   **Backend (`AuditController`)**:
    *   `GET /api/admin/audit-logs`: Paginated search.
    *   **Filters**: `UserId`, `EventType`, `DateRange`, `IPAddress`.
*   **Frontend (`AdminApp`)**:
    *   New View: `AuditLogList.vue`.
    *   Data Table with sortable columns.
    *   Expandable rows to show JSON `Details`.

## Technical Considerations: Session Revocation
*   **Scope**: Revocation applies primarily to the **IdP Session** and **Token Refresh capability**.
*   **Effect**:
    *   **IdP**: The session cookie is invalidated. The next time the browser hits the IdP, the user is redirected to Login.
    *   **Downstream Apps (RPs)**: 
        *   Existing **Access Tokens** (JWTs) remain valid until they naturally expire (typically 5-60 minutes).
        *   **Refresh Tokens** stop working immediately. When the App tries to get a new Access Token, it will fail, forcing the user to re-authenticate.
