import { Page, Response as PlaywrightResponse } from '@playwright/test'

export async function loginAsAdminViaIdP(page: Page) {
  // Ensure any existing session is logged out, then go to login page
  await page.goto('https://localhost:7035/Account/Logout');
  await page.goto('https://localhost:7035/Account/Login');
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');
  await page.click('button.auth-btn-primary');
  await page.waitForSelector('.user-name', { timeout: 20000 });
}
export async function login(page: Page, email: string, password: string) {
  // Go directly to login page
  await page.goto('https://localhost:7035/Account/Login');
  await page.waitForSelector('#Input_Login');
  await page.fill('#Input_Login', email);
  await page.fill('#Input_Password', password);
  await page.click('button.auth-btn-primary');
  await page.waitForSelector('.user-name', { timeout: 20000 });
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

  // Click/clear then fill to ensure it triggers the filter
  await page.fill(searchInput, '');
  await page.fill(searchInput, query);

  // Wait for the API to return (GET /api/admin/{entity}?search=...)
  const apiResp = await waitForResponseJson(page, (r) => r.url().includes(`/api/admin/${entity}?`) && r.request().method() === 'GET', timeout).catch(() => null);

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
    await page.waitForURL('**/Account/Profile', { timeout: 20000 }).catch(() => {
      // If not found, try to navigate back to TestClient root and wait for login to settle
      // (use fallback to check presence of profile indicator)
    });
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
    waitForResponseJson,
    searchListForItem
}
