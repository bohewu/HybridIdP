# Admin helper functions

Helper utilities for E2E tests used to interact with the IdP admin UI and APIs.

## Quick Examples

```ts
// Login as admin
await adminHelpers.loginAsAdminViaIdP(page);

// Create a client via API and verify it is present in the admin UI using the search helper
const client = await page.evaluate(async () => { /* create client via POST /api/admin/clients */ });
await page.goto('https://localhost:7035/Admin/Clients');
const listItem = await adminHelpers.searchListForItem(page, 'clients', client.clientId, { timeout: 20000 });
expect(listItem).not.toBeNull();
```

## Available helpers (short reference)

- `loginAsAdminViaIdP(page)`
  - Logs out any current session and logs in with `admin@hybridauth.local`.

- `login(page, email, password)`
  - Navigates to the IdP login page and performs login with provided credentials.

- `createRole(page, roleName, permissions?)`
  - Creates a role via the admin API and returns the created DTO.

- `deleteRole(page, roleId)`
  - Deletes the role by id via the admin API.

- `createUserWithRole(page, email, password, roleIdentifiers)`
  - Accepts array of role identifiers (names or GUIDs). GUIDs are resolved to names if necessary, then the assignment endpoint is called.

- `deleteUser(page, userId)`
  - Deletes a user via API.

- `deleteClientViaApiFallback(page, clientId)`
  - Searches for a client by clientId and deletes it. Useful for cleanup when the UI is flaky.

- `regenerateSecretViaApi(page, clientId)`
  - Calls POST `/api/admin/clients/{id}/regenerate-secret` for a confidential client and returns the secret object.

- `createScope(page, scopeName, displayName?, description?)`
  - API helper to create a scope via POST.

- `deleteScope(page, scopeIdOrName)`
  - Deletes a scope via API (by name or ID).

- `createApiResource(page, resourceName, displayName?, baseUrl?)`
  - API helper to create API resources.

- `deleteApiResource(page, resourceIdOrName)`
  - Deletes an API resource by ID or name.

- `waitForResponseJson(page, predicate, timeout)`
  - Waits for a Playwright response matching the `predicate`, parses JSON if possible, and returns the parsed JSON or null.
  - Example: `await adminHelpers.waitForResponseJson(page, r => r.url().includes('/api/admin/clients?') && r.request().method() === 'GET', 10000)`

- `searchListForItem(page, entity, query, options?)`
  - Standardized helper to search the Admin UI lists.
  - It fills the search input (`input[placeholder*="Search"]` by default), waits for a GET `/api/admin/{entity}?search=...` API response, and returns a `Locator` for the matching list item or `null`.
  - Default selectors: `input[placeholder*="Search"]` and `ul[role="list"]`.
  - Example usage:

  ```ts
  // Navigate to the Clients admin UI
  await page.goto('https://localhost:7035/Admin/Clients');
  const clientListItem = await adminHelpers.searchListForItem(page, 'clients', clientId, { timeout: 15000 });
  expect(clientListItem).not.toBeNull();
  await expect(clientListItem).toBeVisible();
  ```

- `searchListForItemWithApi(page, entity, query, options?)`
  - Similar to `searchListForItem` but also returns parsed API item if available.
  - Returns `{ apiItem, locator }` where `apiItem` is the API object or `null` and `locator` is a Playwright Locator or `null`.

  Example:

  ```ts
  const { apiItem, locator } = await adminHelpers.searchListForItemWithApi(page, 'clients', clientId);
  expect(apiItem).toBeDefined();
  expect(locator).not.toBeNull();
  ```

- `searchAndClickAction(page, entity, query, action, options?)`
  - Finds the row and clicks the named action (Edit/Delete/Regenerate). Returns `{ apiItem, clicked }`.

  Example:

  ```ts
  const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Regenerate');
  expect(result.clicked).toBeTruthy();
  ```

## Notes and recommendations

- Tests should navigate to the appropriate Admin page (e.g., `/Admin/Clients`, `/Admin/Scopes`) before calling `searchListForItem` — it relies on the search input and list rendering present on that page.
- Use `waitForResponseJson` when you need to parse JSON returned by an API call and use the predicate to assert the correct response (for instance, wait for PUT/GET to complete and return JSON). This helps avoid timing-related flakiness.
- Prefer `page.evaluate` API-based actions for setup/cleanup as they are faster and less likely to be flaky than using the UI for those tasks.

If you'd like, I can also add a short README example for `searchListForItem` inside each feature folder where it's relevant (Clients/Scopes/Resources).
