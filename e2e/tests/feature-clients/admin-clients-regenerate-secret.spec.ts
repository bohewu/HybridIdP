import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Regenerate secret for confidential client', async ({ page }) => {
  page.on('dialog', async (d) => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Admin\/Clients/);

  await page.click('button:has-text("Create New Client")');
  await page.waitForSelector('#clientId');

  const clientId = `e2e-confidential-${Date.now()}`;
  await page.fill('#clientId', clientId);
  await page.fill('#displayName', 'E2E Confidential Client');
  await page.check('#type-confidential');
  await page.fill('#redirectUris', 'https://localhost:7001/signin-oidc');
  await page.check('input[id="scope-openid"]');
  await page.check('input[id="gt:authorization_code"]');
  await page.click('button[type="submit"]');

  // Read the generated client secret from modal
  const secretInput = page.locator('input[readonly][class*="font-mono"]');
  await expect(secretInput).toBeVisible({ timeout: 10000 });
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
  // modal shows new secret
  const newSecretInput = page.locator('div.fixed input[readonly].font-mono');
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
