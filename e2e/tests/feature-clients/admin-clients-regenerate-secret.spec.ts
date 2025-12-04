import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Regenerate secret for confidential client', async ({ page }) => {
  page.on('dialog', async (d) => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Admin\/Clients/);

  await page.evaluate(async () => {
    const ensureScope = async (name: string, displayName: string) => {
      try {
        const resp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(name)}`);
        if (resp.ok) {
          const json = await resp.json();
          const items = Array.isArray(json) ? json : (json.items || []);
          if (items.some((i: any) => i.name === name)) return;
        }
      } catch {}
      await fetch('/api/admin/scopes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, displayName, description: '' }) });
    };
    await ensureScope('openid', 'OpenID');
  });
  await page.click('button:has-text("Create New Client")');
  await page.waitForSelector('#clientId');

  const clientId = `e2e-confidential-${Date.now()}`;
  await page.fill('#clientId', clientId);
  await page.fill('#displayName', 'E2E Confidential Client');
  await page.check('#type-confidential');
  await page.fill('#redirectUris', 'https://localhost:7001/signin-oidc');
  // Add openid scope via scope manager (Client scope UI replaced checkboxes with a manager)
  await page.waitForSelector('[data-test="csm-available-item"]', { timeout: 10000 });
  await page.fill('[data-test="csm-available-search"]', 'openid');
  const addOpenIdBtn = page.locator('[data-test="csm-available-item"]', { hasText: /openid/i }).locator('button').first();
  if (await addOpenIdBtn.count() > 0) await addOpenIdBtn.click();
  await page.check('input[id="gt:authorization_code"]');
  await page.click('button[type="submit"]');

  // Wait for secret modal to appear and render
  await page.waitForSelector('div.fixed', { timeout: 10000, state: 'visible' });
  await page.waitForTimeout(1000); // Allow modal animation to complete
  
  // Wait for any input to appear in the modal - try multiple selectors
  const secretInput = page.locator('div.fixed input[readonly], div.fixed input[type="text"][readonly], div.fixed input.font-mono').first();
  await secretInput.waitFor({ state: 'visible', timeout: 10000 });

  // Read the generated client secret from modal
  await expect(secretInput).toBeVisible({ timeout: 5000 });
  const firstSecret = await secretInput.inputValue();
  expect(firstSecret.length).toBeGreaterThan(16);

  // Close modal
  const closeBtn = page.locator('button:has-text("Close")');
  if (await closeBtn.count() > 0 && await closeBtn.isVisible()) {
    await closeBtn.click();
  }

  const found = await adminHelpers.searchListForItemWithApi(page, 'clients', clientId, { listSelector: 'ul[role="list"], table tbody', timeout: 20000 });
  // `apiItem` may be null if the list/view implementation doesn't perform a typed search; prefer the UI locator.
  expect(found.locator).not.toBeNull();
  if (found.locator) await expect(found.locator).toBeVisible({ timeout: 20000 });

  // Click regenerate secret button in the list and confirm the modal shows a new secret
  // Use searchAndClickAction to press 'Regenerate' button in the row
  const actionResult = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Regenerate', { listSelector: 'ul[role="list"], table tbody', timeout: 20000 });
  expect(actionResult.clicked).toBeTruthy();
  
  // Wait for modal and secret input to appear
  await page.waitForSelector('div.fixed', { timeout: 10000, state: 'visible' });
  await page.waitForTimeout(500); // Allow modal animation
  
  // Try multiple selectors for the secret input (modal structure may vary)
  const newSecretInput = page.locator('div.fixed input[readonly]').first();
  await expect(newSecretInput).toBeVisible({ timeout: 10000 });
  const regenerated = await newSecretInput.inputValue();
  expect(regenerated.length).toBeGreaterThan(16);
  expect(regenerated).not.toBe(firstSecret);

  // Close modal and cleanup
  const closeModalBtn = page.locator('button:has-text("Close")');
  if (await closeModalBtn.count() > 0) await closeModalBtn.click();

  try {
    // Delete via searchAndConfirmActionWithModal for the row / confirm logic (prefer modal wait)
    const delResult = await adminHelpers.searchAndConfirmActionWithModal(page, 'clients', clientId, 'Delete', { listSelector: 'ul[role="list"], table tbody', timeout: 20000 });
    if (!delResult.clicked) {
      // fallback to direct locator if present
      const locator = found.locator;
      if (locator) {
        const deleteBtn = locator.locator('button[title*="Delete"], button:has-text("Delete")').first();
        if (await deleteBtn.count() > 0) await deleteBtn.click().catch(() => {});
      }
    }
  } catch (e) {
    await adminHelpers.deleteClientViaApiFallback(page, clientId);
  }
});
