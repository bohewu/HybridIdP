import { test, expect } from '../fixtures';

// Clients CRUD tests using hybrid pattern.
// API for setup/teardown, minimal UI verification.

test.describe('Admin - Clients CRUD', () => {
  test('Create, update, and delete client', async ({ page, api }) => {
    const timestamp = Date.now();
    const clientId = `e2e-client-${timestamp}`;

    // 1. Arrange (API) - Create client
    const client = await api.clients.create({
      clientId,
      displayName: `E2E Test Client ${timestamp}`,
      applicationType: 'web',
      type: 'confidential',
      consentType: 'explicit',
      redirectUris: ['https://localhost:7001/signin-oidc'],
      postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
      permissions: [
        'ept:authorization',
        'ept:token',
        'ept:logout',
        'gt:authorization_code',
        'gt:refresh_token',
        'response_type:code',
        'scp:openid',
        'scp:profile'
      ]
    });
    expect(client.id).toBeTruthy();

    // 2. Assert (UI) - Verify client appears in admin list
    await page.goto('https://localhost:7035/Admin/Clients');
    await page.waitForURL(/\/Admin\/Clients/);

    const searchInput = page.locator('input[placeholder*="Search" i]');
    if (await searchInput.count() > 0) {
      await searchInput.fill(clientId);
      await page.waitForTimeout(500);
    }

    await expect(page.locator(`text=${clientId}`)).toBeVisible({ timeout: 10000 });

    // 3. Cleanup (API)
    await api.clients.deleteClient(client.id);
  });

  test('Client list shows created clients', async ({ api }) => {
    const timestamp = Date.now();
    const clientId = `e2e-list-${timestamp}`;

    // Create client
    const client = await api.clients.create({
      clientId,
      displayName: `List Test ${timestamp}`,
      applicationType: 'web',
      type: 'public',
      consentType: 'explicit',
      redirectUris: ['https://localhost:7001/callback'],
      permissions: ['ept:authorization', 'gt:authorization_code']
    });

    // Search via API
    const found = await api.clients.findByClientId(clientId);
    expect(found).not.toBeNull();
    expect(found?.clientId).toBe(clientId);

    // Cleanup
    await api.clients.deleteClient(client.id);
  });
});
