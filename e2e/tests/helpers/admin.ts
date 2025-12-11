import { Page, Response as PlaywrightResponse } from '@playwright/test'

export async function loginAsAdminViaIdP(page: Page) {
  // Ensure admin API is reachable before attempting to navigate/login
  await ensureAdminAvailable(page);

  // Ensure any existing session is logged out, then go to login page
  await page.goto('https://localhost:7035/Account/Logout');
  await page.goto('https://localhost:7035/Account/Login');
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');
  await page.click('button.auth-btn-primary');
  // Wait for admin layout to load (admin is redirected to /Admin after login)
  await page.waitForSelector('.user-info-name, .user-name', { timeout: 20000 });
  // Ensure we are on the Admin page (explicit navigation if not redirected)
  if (!page.url().includes('/Admin')) {
    await page.goto('https://localhost:7035/Admin');
    await page.waitForSelector('.user-info-name, .user-name', { timeout: 10000 });
  }
}
export async function login(page: Page, email: string, password: string) {
  // Go directly to login page
  await ensureAdminAvailable(page);
  await page.goto('https://localhost:7035/Account/Login');
  await page.waitForSelector('#Input_Login');
  await page.fill('#Input_Login', email);
  await page.fill('#Input_Password', password);
  await page.click('button.auth-btn-primary');
  
  // Wait for either successful login (user-name appears) or navigation away (e.g., AccessDenied)
  // Some users may not have access to admin pages, so don't fail if .user-name doesn't appear
  await Promise.race([
    page.waitForSelector('.user-name', { timeout: 20000 }).catch(() => {}),
    page.waitForURL(/\/(Admin|Account\/(AccessDenied|MyAccount))/, { timeout: 20000 }).catch(() => {})
  ]);
}

// Utility: Ensure admin site and its API health endpoint is reachable before attempting UI interactions
export async function ensureAdminAvailable(page: Page, timeout = 120_000, interval = 2000) {
  const base = 'https://localhost:7035';
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    try {
      // Try health endpoint first
      const resp = await page.goto(`${base}/api/admin/health`, { waitUntil: 'networkidle', timeout: 5000 }).catch(() => null);
        // If the admin health endpoint returns 200 -> healthy.
        // If it returns 401/403 it means the service is reachable but requires auth — treat as available.
        if (resp && (resp.status() === 200 || resp.status() === 401 || resp.status() === 403)) {
        return true;
      }
    } catch (e) {
      // ignore and retry
    }
    await new Promise((r) => setTimeout(r, interval));
  }
  throw new Error(`Admin at ${base} not available after ${timeout}ms`);
}

// Helper to delete a client via the browser context (uses same session cookies)
export async function deleteClientViaApiFallback(page: Page, clientId: string) {
  try {
    await page.evaluate(async (id) => {
      try {
        // Find the client to delete by searching the list for the matching clientId
        const res = await fetch(`/api/admin/clients?search=${encodeURIComponent(id)}`);
        if (!res.ok) return;
        const json = await res.json();
        const client = Array.isArray(json) ? json.find((c: any) => c.clientId === id) : (json.items || []).find((c: any) => c.clientId === id);
        if (client && client.id) {
          await fetch(`/api/admin/clients/${client.id}`, { method: 'DELETE' });
        }
      } catch (err) {
        // swallow errors in cleanup
      }
    }, clientId);
  } catch (e) {
    // swallow errors
  }
}

export async function createRole(page: Page, roleName: string, permissions: string[] = []) {
  const payload = { name: roleName, description: `E2E role ${roleName}`, permissions };
  const result = await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/roles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to create role: ${r.status} - ${errorText}`);
    }
    return r.json();
  }, payload);
  return result;
}

export async function deleteRole(page: Page, roleId: string) {
  try {
    await page.evaluate(async (id) => {
      await fetch(`/api/admin/roles/${id}`, { method: 'DELETE' });
    }, roleId);
  } catch (e) {
    // swallow errors
  }
}

export async function createUserWithRole(page: Page, email: string, password: string, roleIdentifiers: string[]) {
  const userPayload = {
    email,
    userName: email,
    firstName: 'E2E',
    lastName: 'User',
    password
  };

  const created = await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) {
      const body = await r.text().catch(() => null);
      throw new Error(`Failed to create user: ${r.status} ${body || ''}`);
    }
    return r.json();
  }, userPayload);

  // Assign roles - roleIdentifiers can be role names or role ids (GUID)
  // Use the ID-based endpoint if all identifiers are GUIDs, otherwise use name-based endpoint
  const resolved = await page.evaluate(async (args) => {
    const isGuid = (s: string) => /[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}/.test(s);
    const allGuids = args.roles.every(r => isGuid(r));
    
    if (allGuids && args.roles.length > 0) {
      // Use the new ID-based endpoint for cleaner implementation
      await fetch(`/api/admin/users/${args.uid}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.roles })
      });
      return args.roles;
    } else {
      // Use the name-based endpoint for role names or mixed identifiers
      await fetch(`/api/admin/users/${args.uid}/roles`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Roles: args.roles })
      });
      return args.roles;
    }
  }, { uid: created.id, roles: roleIdentifiers });

  return created;
}

export async function deleteUser(page: Page, userId: string) {
  try {
    await page.evaluate(async (id) => {
      await fetch(`/api/admin/users/${id}`, { method: 'DELETE' });
    }, userId);
  } catch (e) {
    // swallow errors
  }
}

export async function regenerateSecretViaApi(page: Page, clientId: string) {
  return await page.evaluate(async (id) => {
    const res = await fetch('/api/admin/clients?search=' + encodeURIComponent(id));
    if (!res.ok) throw new Error('Not found');
    const json = await res.json();
    const client = Array.isArray(json) ? json.find((c: any) => c.clientId === id) : (json.items || []).find((c: any) => c.clientId === id);
    if (!client) throw new Error('Client not found');
    const regen = await fetch(`/api/admin/clients/${client.id}/regenerate-secret`, { method: 'POST' });
    if (!regen.ok) throw new Error('Failed to regenerate');
    return regen.json();
  }, clientId);
}

export async function createScope(page: Page, scopeName: string, displayName?: string, description?: string) {
  const payload = {
    name: scopeName,
    displayName: displayName || scopeName,
    description: description || `E2E scope ${scopeName}`,
    resources: []
  };
  return await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/scopes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) throw new Error(`Failed to create scope: ${r.status}`);
    return r.json();
  }, payload);
}

export async function deleteScope(page: Page, scopeIdOrName: string) {
  try {
    await page.evaluate(async (identifier) => {
      // First try to find the scope by name
      const searchRes = await fetch(`/api/admin/scopes?search=${encodeURIComponent(identifier)}`);
      if (searchRes.ok) {
        const data = await searchRes.json();
        const scope = data.items && data.items.length > 0 ? data.items[0] : null;
        if (scope && scope.id) {
          await fetch(`/api/admin/scopes/${scope.id}`, { method: 'DELETE' });
          return;
        }
      }
      // If not found by name, try using it directly as an ID
      await fetch(`/api/admin/scopes/${identifier}`, { method: 'DELETE' });
    }, scopeIdOrName);
  } catch (e) {
    // swallow errors in cleanup
  }
}

export async function createApiResource(page: Page, resourceName: string, displayName?: string, baseUrl?: string) {
  const payload = {
    name: resourceName,
    displayName: displayName || resourceName,
    description: `E2E API resource ${resourceName}`,
    baseUrl: baseUrl || null,
    scopeIds: []
  };
  return await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/resources', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) throw new Error(`Failed to create API resource: ${r.status}`);
    const data = await r.json();
    return data.id;
  }, payload);
}

export async function deleteApiResource(page: Page, resourceIdOrName: string | number) {
  try {
    await page.evaluate(async (identifier) => {
      // If it's a number, use it directly as ID
      if (typeof identifier === 'number') {
        await fetch(`/api/admin/resources/${identifier}`, { method: 'DELETE' });
        return;
      }
      // Otherwise, try to find by name first
      const searchRes = await fetch(`/api/admin/resources?search=${encodeURIComponent(identifier)}`);
      if (searchRes.ok) {
        const data = await searchRes.json();
        const resource = data.items && data.items.length > 0 ? data.items[0] : null;
        if (resource && resource.id) {
          await fetch(`/api/admin/resources/${resource.id}`, { method: 'DELETE' });
          return;
        }
      }
      // Last resort: try using it as ID directly
      await fetch(`/api/admin/resources/${identifier}`, { method: 'DELETE' });
    }, resourceIdOrName);
  } catch (e) {
    // swallow errors in cleanup
  }
}

// Utility: Poll an API URL (relative) until a predicate function returns true or timeout
export async function pollApiUntil(page: Page, url: string, predicate: (json: any) => boolean, timeout = 15000, interval = 500) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    try {
      const data = await page.evaluate(async (u) => {
        const r = await fetch(u, { cache: 'no-store' });
        if (!r.ok) return null;
        try { return await r.json(); } catch { return null; }
      }, url);
      if (data && predicate(data)) return data;
    } catch (e) {
      // swallow transient errors while polling
    }
    await new Promise((r) => setTimeout(r, interval));
  }
  return null;
}

// Utility: Wait for a specific API response matching the predicate and return parsed JSON
export async function waitForResponseJson(page: Page, predicate: (resp: PlaywrightResponse) => boolean, timeout = 10000) {
  const resp = await page.waitForResponse((r) => predicate(r as PlaywrightResponse), { timeout });
  try {
    // Try to parse JSON and return it; fallback to null if not JSON
    return await resp.json();
  } catch (e) {
    return null;
  }
}

// Utility: Search for an item via the Search input and wait for list update
export async function searchListForItem(page: Page, entity: string, query: string, options?: {
  searchInputSelector?: string,
  listSelector?: string,
  predicate?: (item: any) => boolean,
  timeout?: number
}) {
  const searchInput = options?.searchInputSelector ?? 'input[placeholder*="Search"]';
  const listSelector = options?.listSelector ?? 'ul[role="list"]';
  const timeout = options?.timeout ?? 10000;

  // Wait for the API to return (GET /api/admin/{entity}?search=...)
  const apiRespPromise = waitForResponseJson(page, (r) => 
    r.url().includes(`/api/admin/${entity}`) && 
    r.url().includes(`search=${encodeURIComponent(query)}`) &&
    r.request().method() === 'GET', timeout).catch(() => null);

  // Click/clear then fill to ensure it triggers the filter
  await page.fill(searchInput, '');
  await page.fill(searchInput, query);

  const apiResp = await apiRespPromise;

  let found = null as any;
  if (apiResp) {
    // Normalize shape: array or { items: [] }
    const items = Array.isArray(apiResp) ? apiResp : (apiResp.items || []);
    const matcher = options?.predicate ?? ((i: any) => i.clientId === query || i.name === query || i.displayName === query || i.id === query);
    found = items.find(matcher);
  }

  // Wait for UI list to contain the item text (fallback to query text)
  // Support both UL/LI and TABLE/TR list styles. Use a flexible selector to catch matching list rows.
  const locator = page.locator(`${listSelector} li, ${listSelector} tr, ${listSelector} *`, { hasText: query }).first();
  try {
    await locator.waitFor({ state: 'visible', timeout });
    return locator;
  } catch (e) {
    // If not visible, but we found an item in API, try to match by ID in list
    if (found && found.id) {
      const byIdLocator = page.locator(`${listSelector} li`, { hasText: String(found.id) }).first();
      try {
        await byIdLocator.waitFor({ state: 'visible', timeout });
        return byIdLocator;
      } catch (e2) {
        // nothing
      }
    }
  }
  return null;
}

// Helper: Search for an item and return both the API item and the UI locator
export async function searchListForItemWithApi(page: Page, entity: string, query: string, options?: {
  searchInputSelector?: string,
  listSelector?: string,
  predicate?: (item: any) => boolean,
  timeout?: number
}) {
  const listSelector = options?.listSelector ?? 'ul[role="list"]';
  const timeout = options?.timeout ?? 10000;

  // Prepare to capture the GET /api/admin/{entity}?search... response that will be triggered below
  const apiRespPromise = waitForResponseJson(page, (r) => 
    r.url().includes(`/api/admin/${entity}?`) && 
    r.url().includes(`search=${encodeURIComponent(query)}`) &&
    r.request().method() === 'GET', timeout).catch(() => null);
  // Use existing searchListForItem flow to find the UI element (this will trigger a search GET)
  const locator = await searchListForItem(page, entity, query, { searchInputSelector: options?.searchInputSelector, listSelector: options?.listSelector, predicate: options?.predicate, timeout });
  // Also capture the API response from the GET (same one used by searchListForItem)
  const apiResp = await apiRespPromise;
  let apiItem = null as any;
  if (apiResp) {
    const items = Array.isArray(apiResp) ? apiResp : (apiResp.items || []);
    const matcher = options?.predicate ?? ((i: any) => i.clientId === query || i.name === query || i.displayName === query || i.id === query);
    apiItem = items.find(matcher);
  }

  // Return both the parsed API item, the raw API response (as parsed JSON) and the UI locator
  return { apiItem, apiResp, locator } as { apiItem: any | null, apiResp: any | null, locator: import('@playwright/test').Locator | null };
}

// Helper: Find item and click an action button inside the row (e.g., 'Edit', 'Delete', 'Regenerate')
export async function searchAndClickAction(page: Page, entity: string, query: string, action: string, options?: {
  listSelector?: string,
  actionSelector?: string,
  timeout?: number
}) {
  const timeout = options?.timeout ?? 10000;
  const listSelector = options?.listSelector ?? 'ul[role="list"], table tbody';
  const actionSelector = options?.actionSelector ?? `button[title*="${action}"], button:has-text("${action}"), a[title*="${action}"], a:has-text("${action}"), *[aria-label*="${action}"]`;

  const result = await searchListForItemWithApi(page, entity, query, { listSelector, timeout });
  const locator = result.locator;
  if (!locator) return { apiItem: result.apiItem, clicked: false };

  const btn = locator.locator(actionSelector).first();
  if (await btn.count() > 0) {
    await btn.click();
    return { apiItem: result.apiItem, clicked: true };
  }
  return { apiItem: result.apiItem, clicked: false };
}

// Helper: search, click an action button inside the row and click a confirm button that appears.
export async function searchAndConfirmAction(page: Page, entity: string, query: string, action: string, options?: {
  listSelector?: string,
  actionSelector?: string,
  confirmSelector?: string,
  waitForApi?: boolean,
  waitForApiPredicate?: (r: PlaywrightResponse) => boolean,
  timeout?: number
}) {
  const timeout = options?.timeout ?? 10000;
  const listSelector = options?.listSelector ?? 'ul[role="list"], table tbody';
  const actionSelector = options?.actionSelector ?? `button[title*="${action}"], button:has-text("${action}")`;
  const confirmSelector = options?.confirmSelector ?? `button:has-text("Confirm"), button:has-text("Delete"), button.confirm`;
  const waitForApi = options?.waitForApi ?? true;

  const result = await searchListForItemWithApi(page, entity, query, { listSelector, timeout });
  const locator = result.locator;
  if (!locator) return { apiItem: result.apiItem, apiResp: result.apiResp, clicked: false, confirmed: false, confirmationResponse: null };

  let btn = locator.locator(actionSelector).first();
  // If action button not visible directly (e.g. inside a "more" dropdown), attempt to open the dropdown and find the action
  if (await btn.count() === 0) {
    const moreBtn = locator.locator('button[aria-haspopup], button[title*="More"], button:has-text("More"), button[aria-label*="More"]').first();
    if (await moreBtn.count() > 0) {
      await moreBtn.click();
      // Look for the action inside an opened menu within the row
      // We attempt several menu/anchor selectors to maximize chance of finding the correct action
      const menuActionCandidates = [
        `ul[role=\"menu\"] li:has-text(\"${action}\")`,
        `button:has-text(\"${action}\")`,
        `a:has-text(\"${action}\")`,
        `li:has-text(\"${action}\")`,
        `*[aria-label*="${action}"]`,
        `*[title*="${action}"]`
      ];
      for (const sel of menuActionCandidates) {
        const candidate = page.locator(sel).first();
        if (await candidate.count() > 0) {
          btn = candidate;
          break;
        }
      }
    }
  }
  if (await btn.count() === 0) return { apiItem: result.apiItem, apiResp: result.apiResp, clicked: false, confirmed: false, confirmationResponse: null };

  // Prepare waiting for API response (any method other than GET) if requested
  let apiWaitPromise: Promise<any> | null = null;
  if (waitForApi) {
    const predicate = options?.waitForApiPredicate ?? ((r: PlaywrightResponse) => r.url().includes(`/api/admin/${entity}`) && r.request().method() !== 'GET');
    apiWaitPromise = waitForResponseJson(page, predicate, timeout).catch(() => null);
  }

  await btn.click();

  // If confirmation modal appears, click confirm
  let confirmed = false;
  let confirmationResponse = null as any;
  const confirmBtnLocator = page.locator(confirmSelector).first();
  if (await confirmBtnLocator.count() > 0) {
    try {
      await confirmBtnLocator.waitFor({ state: 'visible', timeout });
      await confirmBtnLocator.click();
      confirmed = true;
      if (apiWaitPromise) {
        confirmationResponse = await apiWaitPromise;
      }
    } catch (e) {
      // ignore - modal did not appear or confirm failed
      confirmed = false;
      confirmationResponse = apiWaitPromise ? await apiWaitPromise : null;
    }
  } else {
    // If no modal, the action itself might trigger the API; wait if requested
    if (apiWaitPromise) {
      confirmationResponse = await apiWaitPromise;
      confirmed = !!confirmationResponse;
    }
  }

  // If we didn't see confirmation and no API response, attempt a resilient fallback:
  //  - Try clicking any 'confirm' or 'delete' button again with force, and wait for API or for the row to disappear
  if (!confirmed && !confirmationResponse) {
    // Re-evaluate the confirm button
    const confirmBtn2 = page.locator(confirmSelector).first();
    if (await confirmBtn2.count() > 0) {
      try {
        await confirmBtn2.waitFor({ state: 'visible', timeout: 1000 }).catch(() => null);
        await confirmBtn2.click({ force: true });
        if (apiWaitPromise) confirmationResponse = await apiWaitPromise;
        if (confirmationResponse) confirmed = true;
      } catch (e) {
        // ignore
      }
    }

    // If no modal/confirm button, try to re-click the action button forcefully
    if (!confirmed && !confirmationResponse) {
      try {
        await btn.click({ force: true });
        if (apiWaitPromise) confirmationResponse = await apiWaitPromise;
        if (confirmationResponse) confirmed = true;
      } catch (e) {
        // ignore
      }
    }

    // As a final check, wait a short time and see if the row disappears (UI-only deletion)
    if (!confirmed && !confirmationResponse && locator) {
      try {
        await locator.waitFor({ state: 'hidden', timeout: 1000 });
        confirmed = true;
      } catch (e) {
        // still present
      }
    }
  }

  return { apiItem: result.apiItem, apiResp: result.apiResp, clicked: true, confirmed, confirmationResponse };
}

// Wrapper: specifically for actions that produce a confirmation modal (e.g., Delete).
// This wrapper will ensure the modal confirm button is clicked and an appropriate API predicate is used.
export async function searchAndConfirmActionWithModal(page: Page, entity: string, query: string, action: string, options?: {
  listSelector?: string,
  actionSelector?: string,
  confirmSelector?: string,
  waitForApi?: boolean,
  waitForApiPredicate?: (r: PlaywrightResponse) => boolean,
  timeout?: number
}) {
  const timeout = options?.timeout ?? 10000;
  const confirmSelector = options?.confirmSelector ?? `button:has-text("Delete"), button:has-text("Confirm"), button.confirm`;
  // By default, wait for a DELETE call to /api/admin/{entity}
  const defaultPredicate = (r: PlaywrightResponse) => r.url().includes(`/api/admin/${entity}`) && r.request().method() === 'DELETE';
  const predicate = options?.waitForApiPredicate ?? defaultPredicate;

  // Call the general helper with the confirm selector and predicate
  const result = await searchAndConfirmAction(page, entity, query, action, {
    listSelector: options?.listSelector,
    actionSelector: options?.actionSelector,
    confirmSelector,
    waitForApi: options?.waitForApi ?? true,
    waitForApiPredicate: predicate,
    timeout
  });
  return result;
}

  export async function updateUser(page: Page, userId: string, updates: {
    email?: string,
    firstName?: string,
    lastName?: string,
    isActive?: boolean,
    roles?: string[]
  }) {
    // If roles are provided and they look like GUIDs, call the ID-based endpoint
    const hasRoles = updates && Array.isArray(updates.roles) && updates.roles.length > 0;
    const isGuid = (s: string) => /^[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}$/.test(s);
    const allIds = hasRoles && updates.roles!.every(r => isGuid(r));

    if (allIds && updates && Object.keys(updates).length === 1) {
      // Use the IDs endpoint
      return await page.evaluate(async (args) => {
        const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ RoleIds: args.updates.roles })
        });
        if (!r.ok) {
          const errorText = await r.text();
          throw new Error(`Failed to update user roles by id: ${r.status} - ${errorText}`);
        }
        return r.json();
      }, { userId, updates });
    }
    // If there are any GUIDs mixed with names, resolve GUIDs to names and continue with update
    const someIds = hasRoles && updates.roles!.some(r => isGuid(r));
    if (someIds && !allIds) {
      // Resolve GUIDs to names
      return await page.evaluate(async (args) => {
        const isGuidLocal = (s: any) => /^[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}$/.test(s);
        const resolvedRoles = [];
        for (const r of (args.updates.roles || [])) {
          if (isGuidLocal(r)) {
            const res = await fetch(`/api/admin/roles/${r}`);
            if (!res.ok) throw new Error(`Failed to resolve role by id: ${r}`);
            const role = await res.json();
            resolvedRoles.push(role.name);
          } else {
            resolvedRoles.push(r);
          }
        }
        // Update roles array to names
        args.updates.roles = resolvedRoles;
        const r = await fetch(`/api/admin/users/${args.userId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(args.updates)
        });
        if (!r.ok) {
          const errorText = await r.text();
          throw new Error(`Failed to update user: ${r.status} - ${errorText}`);
        }
        return r.json();
      }, { userId, updates });
    }

    // Otherwise call the normal update endpoint
    return await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(args.updates)
      });
      if (!r.ok) {
        const errorText = await r.text();
        throw new Error(`Failed to update user: ${r.status} - ${errorText}`);
      }
      return r.json();
    }, { userId, updates });
  }

  export async function createMultipleSessions(page: Page, email: string, password: string, count: number) {
    const contexts = [];
    for (let i = 0; i < count; i++) {
        const context = await page.context().browser()!.newContext({ ignoreHTTPSErrors: true });
        const newPage = await context.newPage();
        // Use OIDC via TestClient to ensure an authorization (session) is created
        await loginViaTestClient(newPage, email, password);
      contexts.push({ context, page: newPage });
    }
    return contexts;
  }

  export async function loginViaTestClient(page: Page, email: string, password: string, testClientUrl = 'https://localhost:7001/') {
    // Navigate to TestClient root
    await page.goto(testClientUrl);
    // Click the login/profile link to trigger OIDC challenge
    const loginLink = page.locator('a:has-text("Login")');
    if (await loginLink.count() > 0) {
      await loginLink.first().click();
    } else {
      // fallback: navigate to '/Account/Login' on IdP
      await page.goto('https://localhost:7035/Account/Login');
    }

    // The IdP will serve the login page under https://localhost:7035
    await page.waitForURL(/https:\/\/localhost:7035/);

    // Fill in credentials
    await page.fill('#Input_Login', email);
    await page.fill('#Input_Password', password);
    // Submit the login form
    await page.click('button.auth-btn-primary');

    // If consent page appears, click Allow
    const allowBtn = page.locator('button[name="submit"][value="allow"]');
    if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
      await allowBtn.click();
    }

    // Wait for redirect back to TestClient profile or root
    await page.waitForURL('**/Account/Profile', { timeout: 90000 });
  }

  export async function getDashboardStats(page: Page) {
    return await page.evaluate(async () => {
      // Updated endpoint path to match refactored DashboardController route: /api/admin/dashboard/stats
      const r = await fetch('/api/admin/dashboard/stats');
      if (!r.ok) throw new Error('Failed to fetch dashboard stats');
      return r.json();
    });
  }

  export async function waitForSessionRevocation(page: Page, userId: string, authorizationId: string, timeout = 5000) {
    const startTime = Date.now();
    while (Date.now() - startTime < timeout) {
      const sessions = await page.evaluate(async (uid) => {
        const r = await fetch(`/api/admin/users/${uid}/sessions`);
        if (!r.ok) return [];
        const json = await r.json();
        // Normalize response shape: some endpoints may return { items: [] } or an array directly
        if (Array.isArray(json)) return json;
        if (json && Array.isArray(json.items)) return json.items;
        return [];
      }, userId);

      const session = sessions.find((s: any) => s.authorizationId === authorizationId);
      if (!session) {
        return true; // Session not found => revoked/removed
      }
      // If session exists but status is not valid, consider it revoked as well
      const status = (session.status || '').toLowerCase();
      if (status !== 'valid') {
        return true; // Session revoked
      }

      await page.waitForTimeout(200); // Poll every 200ms
    }
    return false; // Timeout
  }

// Person management helpers (Phase 10.3)
export async function createPerson(page: Page, firstName: string, lastName: string, employeeId?: string) {
  const payload = {
    firstName,
    lastName,
    employeeId: employeeId || null,
    department: 'E2E Department',
    jobTitle: 'E2E Tester'
  };
  
  return await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/people', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to create person: ${r.status} - ${errorText}`);
    }
    return r.json();
  }, payload);
}

export async function deletePerson(page: Page, personId: string) {
  try {
    await page.evaluate(async (id) => {
      await fetch(`/api/admin/people/${id}`, { method: 'DELETE' });
    }, personId);
  } catch (e) {
    // swallow errors
  }
}

export async function linkAccountToPerson(page: Page, personId: string, userId: string) {
  return await page.evaluate(async (args) => {
    const r = await fetch(`/api/admin/people/${args.personId}/accounts`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId: args.userId })
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to link account: ${r.status} - ${errorText}`);
    }
  }, { personId, userId });
}

export async function unlinkAccountFromPerson(page: Page, userId: string) {
  return await page.evaluate(async (id) => {
    const r = await fetch(`/api/admin/people/accounts/${id}`, {
      method: 'DELETE'
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to unlink account: ${r.status} - ${errorText}`);
    }
  }, userId);
}

export async function createUnlinkedUser(page: Page, email: string, password: string, roleIdentifiers: string[] = []) {
  const user = await createUserWithRole(page, email, password, roleIdentifiers);
  
  // Check if user was auto-linked to a person and unlink it
  try {
    await unlinkAccountFromPerson(page, user.id);
  } catch (e) {
    // User might not be linked, which is fine
  }
  
  return user;
}

export async function getAvailableUsers(page: Page, searchTerm?: string) {
  return await page.evaluate(async (search) => {
    const params = search ? `?search=${encodeURIComponent(search)}` : '';
    const r = await fetch(`/api/admin/people/available-users${params}`);
    if (!r.ok) throw new Error(`Failed to get available users: ${r.status}`);
    return r.json();
  }, searchTerm || '');
}

// Person identity document helpers (Phase 10.6)
export async function createPersonWithIdentity(
  page: Page,
  personData: {
    firstName: string;
    lastName: string;
    employeeId?: string;
    identityDocumentType?: 'NationalId' | 'Passport' | 'ResidentCertificate';
    nationalId?: string;
    passportNumber?: string;
    residentCertificateNumber?: string;
    department?: string;
    jobTitle?: string;
  }
) {
  const payload = {
    firstName: personData.firstName,
    lastName: personData.lastName,
    employeeId: personData.employeeId || null,
    department: personData.department || 'E2E Department',
    jobTitle: personData.jobTitle || 'E2E Tester',
    identityDocumentType: personData.identityDocumentType || null,
    nationalId: personData.nationalId || null,
    passportNumber: personData.passportNumber || null,
    residentCertificateNumber: personData.residentCertificateNumber || null
  };
  
  return await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/people', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to create person with identity: ${r.status} - ${errorText}`);
    }
    return r.json();
  }, payload);
}

export async function createPersonWithLifecycle(
  page: Page,
  personData: {
    firstName: string;
    lastName: string;
    status: 'Pending' | 'Active' | 'Suspended' | 'Resigned' | 'Terminated';
    startDate?: string | null;
    endDate?: string | null;
    employeeId?: string;
    department?: string;
    jobTitle?: string;
  }
) {
  const payload = {
    firstName: personData.firstName,
    lastName: personData.lastName,
    employeeId: personData.employeeId || null,
    department: personData.department || 'E2E Department',
    jobTitle: personData.jobTitle || 'E2E Tester',
    // Phase 18: Lifecycle fields
    status: personData.status,
    startDate: personData.startDate || null,
    endDate: personData.endDate || null
  };
  
  return await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/people', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to create person with lifecycle: ${r.status} - ${errorText}`);
    }
    return r.json();
  }, payload);
}

export async function updatePersonIdentity(
  page: Page,
  personId: string,
  identityData: {
    identityDocumentType?: 'NationalId' | 'Passport' | 'ResidentCertificate' | null;
    nationalId?: string | null;
    passportNumber?: string | null;
    residentCertificateNumber?: string | null;
  }
) {
  // First get existing person data
  const existingPerson = await getPersonDetails(page, personId);
  
  const payload = {
    ...existingPerson,
    identityDocumentType: identityData.identityDocumentType !== undefined ? identityData.identityDocumentType : existingPerson.identityDocumentType,
    nationalId: identityData.nationalId !== undefined ? identityData.nationalId : existingPerson.nationalId,
    passportNumber: identityData.passportNumber !== undefined ? identityData.passportNumber : existingPerson.passportNumber,
    residentCertificateNumber: identityData.residentCertificateNumber !== undefined ? identityData.residentCertificateNumber : existingPerson.residentCertificateNumber
  };
  
  return await page.evaluate(async (args) => {
    const r = await fetch(`/api/admin/people/${args.personId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(args.payload)
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to update person identity: ${r.status} - ${errorText}`);
    }
    return r.json();
  }, { personId, payload });
}

export async function verifyPersonIdentity(page: Page, personId: string) {
  return await page.evaluate(async (id) => {
    const r = await fetch(`/api/admin/people/${id}/verify-identity`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    if (!r.ok) {
      const errorText = await r.text();
      throw new Error(`Failed to verify person identity: ${r.status} - ${errorText}`);
    }
    // API might return 204 No Content, check content before parsing JSON
    const text = await r.text();
    return text ? JSON.parse(text) : null;
  }, personId);
}

export async function getPersonDetails(page: Page, personId: string) {
  return await page.evaluate(async (id) => {
    const r = await fetch(`/api/admin/people/${id}`);
    if (!r.ok) {
      throw new Error(`Failed to get person details: ${r.status}`);
    }
    return r.json();
  }, personId);
}

export default {
  loginAsAdminViaIdP,
  login,
  deleteClientViaApiFallback,
  createRole,
  deleteRole,
  createUserWithRole,
  deleteUser,
  regenerateSecretViaApi,
  createScope,
  deleteScope,
  createApiResource,
    deleteApiResource,
    updateUser,
    createMultipleSessions,
    loginViaTestClient,
    getDashboardStats,
    waitForSessionRevocation,
    pollApiUntil,
    waitForResponseJson,
    searchListForItem,
    searchListForItemWithApi,
    searchAndClickAction,
    searchAndConfirmAction
    ,
    searchAndConfirmActionWithModal,
    createPerson,
    deletePerson,
    linkAccountToPerson,
    unlinkAccountFromPerson,
    getAvailableUsers,
    createPersonWithIdentity,
    createPersonWithLifecycle,
    updatePersonIdentity,
    verifyPersonIdentity,
    getPersonDetails,
    createUnlinkedUser
}
