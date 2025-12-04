import { test, expect, Page } from '@playwright/test';
import adminHelpers from '../helpers/admin';

/**
 * E2E Tests for ApplicationManager Ownership-Based Access Control
 * 
 * Test user: appmanager@hybridauth.local / AppManager@123
 * This user has ApplicationManager role and should only be able to see/manage their own clients and scopes.
 */

// Helper: Login as ApplicationManager
async function loginAsApplicationManager(page: Page) {
  await adminHelpers.login(page, 'appmanager@hybridauth.local', 'AppManager@123');
}

// Helper: Login as Admin (uses admin@hybridauth.local)
async function loginAsAdmin(page: Page) {
  await adminHelpers.loginAsAdminViaIdP(page);
}

// Helper: Create a client via API (returns the client ID and internal id)
async function createClientViaApi(page: Page, clientId: string, displayName: string) {
  return await page.evaluate(async ({ clientId, displayName }) => {
    const resp = await fetch('/api/admin/clients', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        clientId,
        displayName,
        clientType: 'Confidential',
        grantTypes: ['authorization_code'],
        scopes: [],
        redirectUris: ['https://localhost:7001/signin-oidc']
      })
    });
    if (!resp.ok) {
      const errorText = await resp.text();
      throw new Error(`Failed to create client: ${resp.status} - ${errorText}`);
    }
    return await resp.json();
  }, { clientId, displayName });
}

// Helper: Delete a client via API using clientId
async function deleteClientViaApi(page: Page, clientId: string) {
  await page.evaluate(async (clientId) => {
    try {
      const searchResp = await fetch(`/api/admin/clients?search=${encodeURIComponent(clientId)}`);
      if (!searchResp.ok) return;
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const client = items.find((c: any) => c.clientId === clientId);
      if (client?.id) {
        await fetch(`/api/admin/clients/${client.id}`, { method: 'DELETE' });
      }
    } catch (e) {
      // Swallow errors in cleanup
    }
  }, clientId);
}

// Helper: Get all visible clients via API
async function getVisibleClients(page: Page): Promise<any[]> {
  return await page.evaluate(async () => {
    const resp = await fetch('/api/admin/clients?skip=0&take=100');
    if (!resp.ok) return [];
    const data = await resp.json();
    return Array.isArray(data) ? data : (data.items || []);
  });
}

// Helper: Create a scope via API
async function createScopeViaApi(page: Page, scopeName: string, displayName: string) {
  return await page.evaluate(async ({ scopeName, displayName }) => {
    const resp = await fetch('/api/admin/scopes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: scopeName,
        displayName,
        description: `E2E test scope ${scopeName}`
      })
    });
    if (!resp.ok) {
      const errorText = await resp.text();
      throw new Error(`Failed to create scope: ${resp.status} - ${errorText}`);
    }
    return await resp.json();
  }, { scopeName, displayName });
}

// Helper: Delete a scope via API
async function deleteScopeViaApi(page: Page, scopeName: string) {
  await page.evaluate(async (scopeName) => {
    try {
      const searchResp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(scopeName)}`);
      if (!searchResp.ok) return;
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const scope = items.find((s: any) => s.name === scopeName);
      if (scope?.id) {
        await fetch(`/api/admin/scopes/${scope.id}`, { method: 'DELETE' });
      }
    } catch (e) {
      // Swallow errors in cleanup
    }
  }, scopeName);
}

// Helper: Get all visible scopes via API
async function getVisibleScopes(page: Page): Promise<any[]> {
  return await page.evaluate(async () => {
    const resp = await fetch('/api/admin/scopes?skip=0&take=100');
    if (!resp.ok) return [];
    const data = await resp.json();
    return Array.isArray(data) ? data : (data.items || []);
  });
}

// Use serial mode to ensure tests run in order within each describe block
test.describe.configure({ mode: 'serial' });

test.describe('ApplicationManager Ownership - Clients', () => {
  const timestamp = Date.now();
  const appManagerClientId = `e2e-appmgr-client-${timestamp}`;
  const adminClientId = `e2e-admin-client-${timestamp}`;

  test.afterAll(async ({ browser }) => {
    // Cleanup: Delete test clients as Admin
    const page = await browser.newPage();
    await loginAsAdmin(page);
    await deleteClientViaApi(page, appManagerClientId);
    await deleteClientViaApi(page, adminClientId);
    await page.close();
  });

  test('ApplicationManager can create and see their own client', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Create a client as ApplicationManager
    const created = await createClientViaApi(page, appManagerClientId, `AppManager Test Client ${timestamp}`);
    expect(created).toBeTruthy();
    expect(created.clientId).toBe(appManagerClientId);

    // Verify the client is visible in the list
    const clients = await getVisibleClients(page);
    const myClient = clients.find(c => c.clientId === appManagerClientId);
    expect(myClient).toBeTruthy();
  });

  test('ApplicationManager cannot see clients created by Admin', async ({ page }) => {
    // First, create a client as Admin
    await loginAsAdmin(page);
    const adminCreated = await createClientViaApi(page, adminClientId, `Admin Test Client ${timestamp}`);
    expect(adminCreated).toBeTruthy();

    // Now login as ApplicationManager and verify Admin's client is NOT visible
    await loginAsApplicationManager(page);
    const clients = await getVisibleClients(page);
    
    // ApplicationManager should see their own client
    const myClient = clients.find(c => c.clientId === appManagerClientId);
    expect(myClient).toBeTruthy();

    // ApplicationManager should NOT see Admin's client (ownership filtering)
    const adminClient = clients.find(c => c.clientId === adminClientId);
    expect(adminClient).toBeFalsy();
  });

  test('Admin can see all clients including ApplicationManager\'s', async ({ page }) => {
    await loginAsAdmin(page);
    const clients = await getVisibleClients(page);

    // Admin should see both clients
    const appMgrClient = clients.find(c => c.clientId === appManagerClientId);
    const adminClient = clients.find(c => c.clientId === adminClientId);
    
    expect(appMgrClient).toBeTruthy();
    expect(adminClient).toBeTruthy();
  });

  test('ApplicationManager cannot delete Admin\'s client', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Try to delete Admin's client directly via API (bypassing UI filtering)
    const result = await page.evaluate(async (clientId) => {
      // First find the client ID (internal ID)
      const searchResp = await fetch(`/api/admin/clients?search=${encodeURIComponent(clientId)}`);
      if (!searchResp.ok) return { error: 'not_found', status: searchResp.status };
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const client = items.find((c: any) => c.clientId === clientId);
      
      if (!client?.id) {
        // Client not visible to ApplicationManager - this is expected behavior
        return { error: 'not_visible', message: 'Client not visible to current user' };
      }

      // Try to delete
      const deleteResp = await fetch(`/api/admin/clients/${client.id}`, { method: 'DELETE' });
      return { status: deleteResp.status, ok: deleteResp.ok };
    }, adminClientId);

    // Either the client should not be visible OR the delete should fail with 403/404
    expect(
      result.error === 'not_visible' || 
      result.error === 'not_found' || 
      result.status === 403 || 
      result.status === 404
    ).toBeTruthy();
  });

  test('ApplicationManager can update their own client', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Update own client
    const updateResult = await page.evaluate(async (clientId) => {
      // Find the client
      const searchResp = await fetch(`/api/admin/clients?search=${encodeURIComponent(clientId)}`);
      if (!searchResp.ok) return { error: 'search_failed', status: searchResp.status };
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const client = items.find((c: any) => c.clientId === clientId);
      
      if (!client?.id) return { error: 'not_found' };

      // Update the display name
      const updateResp = await fetch(`/api/admin/clients/${client.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...client,
          displayName: client.displayName + ' (updated)'
        })
      });
      return { status: updateResp.status, ok: updateResp.ok };
    }, appManagerClientId);

    expect(updateResult.ok).toBeTruthy();
    expect(updateResult.status).toBe(200);
  });
});

test.describe('ApplicationManager Ownership - Scopes', () => {
  const timestamp = Date.now();
  const appManagerScopeName = `e2e_appmgr_scope_${timestamp}`;
  const adminScopeName = `e2e_admin_scope_${timestamp}`;

  test.afterAll(async ({ browser }) => {
    // Cleanup: Delete test scopes as Admin
    const page = await browser.newPage();
    await loginAsAdmin(page);
    await deleteScopeViaApi(page, appManagerScopeName);
    await deleteScopeViaApi(page, adminScopeName);
    await page.close();
  });

  test('ApplicationManager can create and see their own scope', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Create a scope as ApplicationManager
    const created = await createScopeViaApi(page, appManagerScopeName, `AppManager Test Scope ${timestamp}`);
    expect(created).toBeTruthy();
    expect(created.name).toBe(appManagerScopeName);

    // Verify the scope is visible in the list
    const scopes = await getVisibleScopes(page);
    const myScope = scopes.find(s => s.name === appManagerScopeName);
    expect(myScope).toBeTruthy();
  });

  test('ApplicationManager cannot see scopes created by Admin', async ({ page }) => {
    // First, create a scope as Admin
    await loginAsAdmin(page);
    const adminCreated = await createScopeViaApi(page, adminScopeName, `Admin Test Scope ${timestamp}`);
    expect(adminCreated).toBeTruthy();

    // Now login as ApplicationManager and verify Admin's scope is NOT visible
    await loginAsApplicationManager(page);
    const scopes = await getVisibleScopes(page);
    
    // ApplicationManager should see their own scope
    const myScope = scopes.find(s => s.name === appManagerScopeName);
    expect(myScope).toBeTruthy();

    // ApplicationManager should NOT see Admin's scope (ownership filtering)
    const adminScope = scopes.find(s => s.name === adminScopeName);
    expect(adminScope).toBeFalsy();
  });

  test('Admin can see all scopes including ApplicationManager\'s', async ({ page }) => {
    await loginAsAdmin(page);
    const scopes = await getVisibleScopes(page);

    // Admin should see both scopes
    const appMgrScope = scopes.find(s => s.name === appManagerScopeName);
    const adminScope = scopes.find(s => s.name === adminScopeName);
    
    expect(appMgrScope).toBeTruthy();
    expect(adminScope).toBeTruthy();
  });

  test('ApplicationManager cannot delete Admin\'s scope', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Try to delete Admin's scope directly via API
    const result = await page.evaluate(async (scopeName) => {
      const searchResp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(scopeName)}`);
      if (!searchResp.ok) return { error: 'not_found', status: searchResp.status };
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const scope = items.find((s: any) => s.name === scopeName);
      
      if (!scope?.id) {
        return { error: 'not_visible', message: 'Scope not visible to current user' };
      }

      const deleteResp = await fetch(`/api/admin/scopes/${scope.id}`, { method: 'DELETE' });
      return { status: deleteResp.status, ok: deleteResp.ok };
    }, adminScopeName);

    // Either the scope should not be visible OR the delete should fail
    expect(
      result.error === 'not_visible' || 
      result.error === 'not_found' || 
      result.status === 403 || 
      result.status === 404
    ).toBeTruthy();
  });

  test('ApplicationManager can update their own scope', async ({ page }) => {
    await loginAsApplicationManager(page);

    // Update own scope
    const updateResult = await page.evaluate(async (scopeName) => {
      const searchResp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(scopeName)}`);
      if (!searchResp.ok) return { error: 'search_failed', status: searchResp.status };
      const data = await searchResp.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      const scope = items.find((s: any) => s.name === scopeName);
      
      if (!scope?.id) return { error: 'not_found' };

      const updateResp = await fetch(`/api/admin/scopes/${scope.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...scope,
          displayName: scope.displayName + ' (updated)'
        })
      });
      return { status: updateResp.status, ok: updateResp.ok };
    }, appManagerScopeName);

    expect(updateResult.ok).toBeTruthy();
    expect(updateResult.status).toBe(200);
  });
});

test.describe('ApplicationManager UI Access', () => {
  test('ApplicationManager can access Admin Clients page', async ({ page }) => {
    await loginAsApplicationManager(page);
    
    // Navigate to Admin Clients page
    await page.goto('https://localhost:7035/Admin/Clients');
    
    // Wait for page to load
    await page.waitForLoadState('networkidle');
    
    // Check if Access Denied dialog appeared (indicates missing permissions)
    const accessDeniedDialog = page.locator('dialog:has-text("Access Denied")');
    const hasAccessDenied = await accessDeniedDialog.isVisible().catch(() => false);
    
    if (hasAccessDenied) {
      // This means ApplicationManager role doesn't have correct permissions yet
      // The test should fail with a clear message
      throw new Error('ApplicationManager role does not have clients.read permission. Please ensure DataSeeder has run to update the role permissions.');
    }
    
    // Should not be redirected to AccessDenied page
    expect(page.url()).not.toContain('AccessDenied');
    
    // Should see the clients management UI (wait for loading to complete)
    await page.waitForSelector('[data-test="csm-available-search"], button:has-text("Create New Client"), .client-list', { timeout: 15000 }).catch(() => {});
    
    // Check for Create button or loaded client list
    const createButton = page.locator('button:has-text("Create New Client")');
    const isCreateVisible = await createButton.isVisible().catch(() => false);
    expect(isCreateVisible).toBeTruthy();
  });

  test('ApplicationManager can access Admin Scopes page', async ({ page }) => {
    await loginAsApplicationManager(page);
    
    // Navigate to Admin Scopes page
    await page.goto('https://localhost:7035/Admin/Scopes');
    
    // Wait for page to load
    await page.waitForLoadState('networkidle');
    
    // Check if Access Denied dialog appeared
    const accessDeniedDialog = page.locator('dialog:has-text("Access Denied")');
    const hasAccessDenied = await accessDeniedDialog.isVisible().catch(() => false);
    
    if (hasAccessDenied) {
      throw new Error('ApplicationManager role does not have scopes.read permission. Please ensure DataSeeder has run to update the role permissions.');
    }
    
    // Should not be redirected to AccessDenied page
    expect(page.url()).not.toContain('AccessDenied');
    
    // Should see the scopes management UI (wait for loading to complete)
    await page.waitForSelector('button:has-text("Create New Scope"), .scope-list', { timeout: 15000 }).catch(() => {});
    
    // Check for Create button
    const createButton = page.locator('button:has-text("Create New Scope")');
    const isCreateVisible = await createButton.isVisible().catch(() => false);
    expect(isCreateVisible).toBeTruthy();
  });
});
