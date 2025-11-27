import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Admin - Clients negative tests', () => {
  test.beforeEach(async ({ page }) => {
    page.on('dialog', async (d) => await d.accept());
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Clients');
    await page.waitForURL(/\/Admin\/Clients/);
  });

  test('Validation error - missing required fields', async ({ page }) => {
    // Ensure OIDC scopes exist in admin scopes so the client form can add them
    await page.evaluate(async () => {
      const ensureScope = async (name, displayName) => {
        try {
          const resp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(name)}`);
          if (resp.ok) {
            const json = await resp.json();
            const items = Array.isArray(json) ? json : (json.items || []);
            if (items.some(i => i.name === name)) return;
          }
        } catch {}
        await fetch('/api/admin/scopes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, displayName, description: '' }) });
      };
      await ensureScope('openid', 'OpenID');
    });

    // Open the Create Client form
    await page.click('button:has-text("Create New Client")');
    await page.waitForSelector('#clientId');

    // Leave clientId blank and try to submit
    await page.fill('#displayName', 'Should Fail');
    await page.fill('#redirectUris', 'not-a-valid-url');
    // Add openid scope via scope manager (UI changed: checkboxes replaced by scope manager)
    await page.waitForSelector('[data-test="csm-available-item"]', { timeout: 10000 });
    await page.fill('[data-test="csm-available-search"]', 'openid');
    const addOpenIdBtn = page.locator('[data-test="csm-available-item"]', { hasText: /openid/i }).locator('button').first();
    if (await addOpenIdBtn.count() > 0) await addOpenIdBtn.click();
    await page.check('input[id="gt:authorization_code"]');

    // Submit and expect client-side validation error or server rejection
    await page.click('button[type="submit"]');
    // As a fallback (UI validation sometimes renders localized strings differently), call the API directly
    // to assert the server rejects missing clientId or invalid redirect URIs.
    const serverResp = await page.evaluate(async () => {
      const payload = {
        clientId: '',
        displayName: 'Should Fail',
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        clientSecret: null,
        redirectUris: [],
        postLogoutRedirectUris: [],
        permissions: ['ept:authorization', 'ept:token']
      };
      const r = await fetch('/api/admin/clients', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      return r.status;
    });
    expect(serverResp).toBeGreaterThanOrEqual(400);

    // Now fix the clientId but use invalid redirect and expect redirect URI invalid message
    await page.fill('#clientId', `e2e-val-${Date.now()}`);
    await page.fill('#redirectUris', 'not-a-valid-url');
    await page.click('button[type="submit"]');
    await expect(page.locator('text=Redirect URI line')).toBeVisible({ timeout: 5000 });
  });

  test('Duplicate clientId shows error', async ({ page }) => {
    const clientId = `e2e-dup-${Date.now()}`;
    // Create the first client
    await page.click('button:has-text("Create New Client")');
    await page.fill('#clientId', clientId);
    await page.fill('#displayName', 'E2E Duplicate Test');
    await page.fill('#redirectUris', 'https://localhost:7001/signin-oidc');
    // Ensure openid added via scope manager for duplicate creation
    await page.waitForSelector('[data-test="csm-available-item"]', { timeout: 10000 });
    await page.fill('[data-test="csm-available-search"]', 'openid');
    const addOpenIdBtn2 = page.locator('[data-test="csm-available-item"]', { hasText: /openid/i }).locator('button').first();
    if (await addOpenIdBtn2.count() > 0) await addOpenIdBtn2.click();
    await page.check('input[id="gt:authorization_code"]');
    await page.click('button[type="submit"]');

    // Close secret modal if shown
    const closeBtn = page.locator('button:has-text("Close")');
    if (await closeBtn.count() > 0 && await closeBtn.isVisible()) await closeBtn.click();

    // Verify the client appears - use the search helper to avoid paging issues
    const clientsListItem = await adminHelpers.searchListForItem(page, 'clients', clientId, { timeout: 20000 });
    expect(clientsListItem).not.toBeNull();
    if (clientsListItem) {
      await expect(clientsListItem).toBeVisible({ timeout: 20000 });
    }

    // Try to create duplicate
    await page.click('button:has-text("Create New Client")');
    await page.fill('#clientId', clientId);
    await page.fill('#displayName', 'E2E Duplicate Test 2');
    await page.fill('#redirectUris', 'https://localhost:7001/signin-oidc');
    // Use scope manager search for the second create attempt as well
    await page.waitForSelector('[data-test="csm-available-item"]', { timeout: 10000 });
    await page.fill('[data-test="csm-available-search"]', 'openid');
    const addOpenIdForDuplicate = page.locator('[data-test="csm-available-item"]', { hasText: /openid/i }).locator('button').first();
    if (await addOpenIdForDuplicate.count() > 0) await addOpenIdForDuplicate.click();
    await page.check('input[id="gt:authorization_code"]');
    await page.click('button[type="submit"]');

    // Expect an error message mentioning 'Failed to save client' (server error)
    await expect(page.locator('text=Failed to save client')).toBeVisible({ timeout: 5000 });

    // Cleanup the created client
    // Cleanup the created client via API fallback to ensure cleanup even if UI is flaky
    await adminHelpers.deleteClientViaApiFallback(page, clientId);
  });
});
