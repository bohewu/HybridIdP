# App role assignment: retained design decisions (2026-01-16)

Historical design record. Phase 22 is recorded as complete in
[project progress](../PROJECT_PROGRESS.md); current authority and claim behavior
are documented in the [permission system](../PERMISSION_SYSTEM.md).
This record is not an active implementation worklist.

- Client-specific `UserAppRole` assignments let one user hold different roles
  in different applications. App-role issuance must remain scoped to the
  requesting client; the original plan called these `app_role` claims. Consult
  current permission/claim guidance for the delivered token representation.
- Assignment validates that both user and client exist and that each requested
  role appears in the client's `SupportedRoles` manifest.
- Updating assignments replaces only the selected user's roles for the selected
  client; it must not alter that user's roles in another application.
- The administrative flow selects a client, loads its supported and assigned
  roles, then saves the selected roles. Reopening the client selection should
  show persisted assignments.
- Verification must check both persisted assignments and client-scoped token
  issuance; seeing a checkbox alone does not establish token behavior.

Machine-specific source links, proposed file edits and session verification
steps were removed from this retained record. No current API, schema or
implementation authority is granted by the historical plan.
