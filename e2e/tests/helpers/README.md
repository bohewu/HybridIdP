# Admin helper functions

Helper utilities for E2E tests used to interact with the IdP admin UI and APIs.

## createUserWithRole(page, email, password, roleIds)

- This helper expects an array of role IDs (GUIDs) as the canonical format.
- The helper will resolve each GUID to the role name via `/api/admin/roles/{id}` and then call `/api/admin/users/{id}/roles` with the resolved role names. This is required because the backend identity APIs operate using role names in AddToRolesAsync.
- Tests should prefer passing `role.id`; the helper will do the necessary resolution automatically.

## loginAsAdminViaIdP(page)

- Logs out any current session, then authenticates as `admin@hybridauth.local`.

## login(page, email, password)

- Navigates to `/Account/Login`, fills in the credentials, and logs in.

## deleteClientViaApiFallback(page, clientId)

- Uses the current page session to call the clients API and delete the test client by id.

## Other helper methods

- `createRole`, `deleteRole`, `deleteUser`, `regenerateSecretViaApi` are thin wrappers around server endpoints used for the smoke and cleanup tests.

## Notes

- `createUserWithRole` intentionally supports both role name and role id to reduce friction in test scenarios and make re-use easier.

If you prefer strict types or only one format in your tests, the helper can be adjusted to accept only role names or only role ids.
# Admin helper functions

Helper utilities for E2E tests used to interact with the IdP admin UI and APIs.

## createUserWithRole(page, email, password, roleIdentifiers)

- Accepts an array of role identifiers that can be role names or role IDs (GUIDs).
- If an identifier looks like a GUID, the helper resolves it to the role name using `/api/admin/roles/{id}` before sending the assignment request to `/api/admin/users/{id}/roles`.
- This allows tests to pass whatever value is most convenient (role name if you want clarity, or id if you retrieved it from the API earlier).

## loginAsAdminViaIdP(page)

- Logs out any current session, then authenticates as `admin@hybridauth.local`.

## login(page, email, password)

- Navigates to `/Account/Login`, fills in the credentials, and logs in.

## deleteClientViaApiFallback(page, clientId)

- Uses the current page session to call the clients API and delete the test client by id.

## Other helper methods

- `createRole`, `deleteRole`, `deleteUser`, `regenerateSecretViaApi` are thin wrappers around server endpoints used for the smoke and cleanup tests.

## Notes

- `createUserWithRole` intentionally supports both role name and role id to reduce friction in test scenarios and make re-use easier.

If you want the helper to only accept one format (name or id), change `createUserWithRole` accordingly and update tests to pass the expected format.
Admin helper functions
======================

Helper utilities for E2E tests used to interact with the IdP admin UI and APIs.

createUserWithRole(page, email, password, roleIdentifiers)
--------------------------------------------------------
- Accepts an array of role identifiers that can be role names or role IDs (GUIDs).
- If an identifier looks like a GUID, the helper resolves it to the role name using `/api/admin/roles/{id}` before sending the assignment request to `/api/admin/users/{id}/roles`.
- This allows tests to pass whichever value is most convenient (role name if you want clarity, or id if you retrieved it from the API earlier).

loginAsAdminViaIdP(page)
------------------------
- Logs out the current session (if any) and authenticates as `admin@hybridauth.local`.

login(page, email, password)
----------------------------
- Navigates to `/Account/Login`, fills in the credentials, and logs in.

deleteClientViaApiFallback(page, clientId)
-----------------------------------------
- Uses the current page session to call the clients API and delete the test client by id.

Other helper methods
--------------------
- `createRole`, `deleteRole`, `deleteUser`, `regenerateSecretViaApi` - thin wrappers around server endpoints used for the smoke and cleanup tests.

Notes
-----
- `createUserWithRole` intentionally supports both role name and role id to reduce friction in test scenarios and make re-use easier.

If you want these helpers to only accept one format (name or id), change `createUserWithRole` accordingly and update tests to pass the expected format.
