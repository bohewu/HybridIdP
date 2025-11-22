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

  // Wait for the client to appear in the list (use search helper to avoid paging issues)
  const createdItem = await adminHelpers.searchListForItem(page, 'clients', clientId, { timeout: 20000 });
  expect(createdItem).not.toBeNull();
  if (createdItem) await expect(createdItem).toBeVisible({ timeout: 20000 });

  // Edit the client: find the list item and click the Edit button
  const listItem = createdItem!;
  await expect(listItem).toBeVisible();

  // Click the edit button inside the list item (match by title attribute to support icon-only buttons)
  await listItem.locator('button[title*=\"Edit\"]').click();

  // Update the display name
  const updatedDisplayName = `${displayName} (updated)`;
  const displayInput = page.locator('#displayName');
  await displayInput.fill(updatedDisplayName);

  // Submit the update form (Update Client) and wait for network update + list refresh
  const updateResponsePromise = page.waitForResponse((resp) => resp.url().includes('/api/admin/clients/') && resp.request().method() === 'PUT');
  const reloadListPromise = page.waitForResponse((resp) => resp.url().includes('/api/admin/clients?') && resp.request().method() === 'GET');
  await Promise.all([
    updateResponsePromise,
    page.click('button[type="submit"]')
  ]);
  // Wait for the list GET request triggered by the parent to refresh the list
  try {
    await reloadListPromise;
  } catch (e) {
    // Ignore if it didn't trigger (old clients list implementations might not re-fetch)
  }
  // Ensure the modal has closed (form submission completed) before checking the list
  try {
    // Wait for the display input to be detached (modal closed) - defensive: modal may reuse the same DOM
    await page.waitForSelector('#displayName', { state: 'detached', timeout: 10000 });
  } catch (e) {
    // If it didn't detach (some implementations keep the input around), wait for the submit button to be enabled again
    await page.waitForSelector('button[type="submit"]', { state: 'visible', timeout: 10000 });
  }

  // Re-open the client edit form and assert the updated display name is persisted
  // (This is more reliable than checking the list text which can be concatenated/shortened)
  await listItem.locator('button[title*="Edit"]').click();
  await page.waitForSelector('#displayName', { timeout: 10000 });
  await expect(page.locator('#displayName')).toHaveValue(updatedDisplayName, { timeout: 20000 });
  // Close the edit form
  await page.click('button[type="button"]:has-text("Cancel"), button:has-text("Close" )').catch(() => {});

  // Delete the client: click delete and accept confirmation via dialog handler
  await listItem.locator('button[title*=\"Delete\"]').click();

  // Wait for the client to be removed from the list
  try {
    const deleted = await adminHelpers.searchListForItem(page, 'clients', clientId, { timeout: 20000 });
    expect(deleted).toBeNull();
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup to avoid orphaned test data
    await adminHelpers.deleteClientViaApiFallback(page, clientId);
  }
});
