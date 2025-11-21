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
    if (!r.ok) throw new Error(`Failed to create role: ${r.status}`);
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
  // Resolve identifiers that look like GUIDs to role names by fetching the role
  const resolved = await page.evaluate(async (args) => {
    const isGuid = (s: string) => /[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}/.test(s);
    const resolvedRoles: string[] = [];
    for (const r of args.roles) {
      try {
        if (isGuid(r)) {
          // Look up the role by id
          const resp = await fetch(`/api/admin/roles/${r}`);
          if (resp.ok) {
            const roleJson = await resp.json();
            if (roleJson && roleJson.name) resolvedRoles.push(roleJson.name);
            else resolvedRoles.push(r);
          } else {
            // if not found, just pass the identifier through
            resolvedRoles.push(r);
          }
        } else {
          // treat as name
          resolvedRoles.push(r);
        }
      } catch (e) {
        // fallback to original identifier
        resolvedRoles.push(r);
      }
    }
    // perform the assignment
    await fetch(`/api/admin/users/${args.uid}/roles`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Roles: resolvedRoles })
    });
    return resolvedRoles;
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

export default {
  loginAsAdminViaIdP,
  login,
  deleteClientViaApiFallback,
  createRole,
  deleteRole,
  createUserWithRole,
  deleteUser,
  regenerateSecretViaApi
}
