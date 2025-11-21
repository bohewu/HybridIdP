import { Page } from '@playwright/test'

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
        const client = Array.isArray(json) ? json.find(c => c.clientId === id) : (json.items || []).find(c => c.clientId === id);
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
    const client = Array.isArray(json) ? json.find(c => c.clientId === id) : (json.items || []).find(c => c.clientId === id);
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
  deleteApiResource
}
