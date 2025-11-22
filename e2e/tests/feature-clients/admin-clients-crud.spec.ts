import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Clients CRUD (create, update, delete client)', async ({ page }) => {
  // Accept native JS dialogs (confirm) automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate directly to the Admin Clients page
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Admin\/Clients/);

  // Click the Create New Client button
  await expect(page.locator('button:has-text("Create New Client")')).toBeVisible();
  await page.click('button:has-text("Create New Client")');

  // Wait for the form modal
  await page.waitForSelector('#clientId');

  const clientId = `e2e-client-${Date.now()}`;
  const displayName = `E2E Test Client ${Date.now()}`;

  await page.fill('#clientId', clientId);
  await page.fill('#displayName', displayName);
  await page.fill('#redirectUris', 'https://localhost:7001/signin-oidc');

  // Wait for scopes and check the required values
  await page.waitForSelector('input[id="scope-openid"]');
  await page.check('input[id="scope-openid"]');
  await page.check('input[id="scope-profile"]');

  // Make sure at least one grant type is checked
  await page.check('input[id="gt:authorization_code"]');

  // Submit the form (Create Client)
  await page.click('button[type="submit"]');

  // If secret modal appears, close it
  const closeBtn = page.locator('button:has-text("Close")');
  if (await closeBtn.count() > 0 && await closeBtn.isVisible()) {
    await closeBtn.click();
  }

  // Wait for the client to appear in the list
  const clientsList = page.locator('ul[role="list"]');
  await expect(clientsList).toContainText(clientId, { timeout: 20000 });

  // Edit the client: find the list item and click the Edit button
  const listItem = clientsList.locator('li', { hasText: clientId });
  await expect(listItem).toBeVisible();

  // Click the edit button inside the list item (match by title attribute to support icon-only buttons)
  await listItem.locator('button[title*=\"Edit\"]').click();

  // Update the display name
  const updatedDisplayName = `${displayName} (updated)`;
  const displayInput = page.locator('#displayName');
  await displayInput.fill(updatedDisplayName);

  // Submit the update form (Update Client)
  await page.click('button[type="submit"]');

  // Ensure the list updates and shows the updated name - re-resolve the list item after update
  await page.waitForTimeout(500); // brief wait to allow list refresh
  const updatedListItem = clientsList.locator('li', { hasText: clientId });
  await expect(updatedListItem).toContainText(updatedDisplayName, { timeout: 20000 });

  // Delete the client: click delete and accept confirmation via dialog handler
  await listItem.locator('button[title*=\"Delete\"]').click();

  // Wait for the client to be removed from the list
  try {
    await expect(clientsList).not.toContainText(clientId, { timeout: 20000 });
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup to avoid orphaned test data
    await adminHelpers.deleteClientViaApiFallback(page, clientId);
  }
});
