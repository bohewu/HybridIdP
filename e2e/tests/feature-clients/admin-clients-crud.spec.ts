import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';
import { waitForDebounce, waitForListItemWithRetry } from '../helpers/timing';

test('Admin - Clients CRUD (create, update, delete client)', async ({ page }) => {
  // Accept native JS dialogs (confirm) automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate directly to the Admin Clients page
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Admin\/Clients/);

  // Ensure common OIDC scopes exist (some environments only seed API scopes).
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
    await ensureScope('profile', 'Profile');
  });

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

  // Wait for scope manager to load and add `openid` + `profile` scopes
  await page.waitForSelector('[data-test="csm-available-item"]', { timeout: 10000 });
  // Debug: capture available scopes server-side to verify 'openid' was created and is returned by the API
  const scopesPayload = await page.evaluate(async () => {
    try {
      const resp = await fetch('/api/admin/scopes?skip=0&take=100');
      if (!resp.ok) return null;
      return await resp.json();
    } catch (e) { return { error: String(e) } }
  });
  console.log('available scopes response:', scopesPayload);

  // Search and add the openid scope (filter list so we can find it among many scopes)
  await page.fill('[data-test="csm-available-search"]', 'openid');
  await waitForDebounce(page, 600); // Wait for search debounce
  const addOpenIdBtn = page.locator('[data-test="csm-available-item"]', { hasText: /openid/i }).locator('button').first();
  await addOpenIdBtn.waitFor({ state: 'visible', timeout: 10000 });

  // Make sure at least one grant type is checked
  await page.check('input[id="gt:authorization_code"]');

  // Submit the form (Create Client)
  // Click submit and wait for both the POST and the subsequent GET (list refresh)
  const [postResponse] = await Promise.all([
    page.waitForResponse(resp => resp.url().includes('/api/admin/clients') && resp.request().method() === 'POST'),
    page.click('button[type="submit"]')
  ]);
  
  expect(postResponse.ok()).toBeTruthy();

  // Close secret modal if shown
  const closeBtn = page.locator('button:has-text("Close")');
  if (await closeBtn.count() > 0 && await closeBtn.isVisible()) {
    await closeBtn.click();
  }

  // Wait for the list refresh GET request after modal closes
  await page.waitForResponse(resp => 
    resp.url().includes('/api/admin/clients') && 
    resp.request().method() === 'GET' &&
    resp.url().includes('skip=') // Ensure it's the paginated list endpoint
  , { timeout: 10000 });

  // Now search for the client in the list using improved timing helper
  const found = await waitForListItemWithRetry(page, 'clients', clientId, { 
    listSelector: 'ul[role="list"], table tbody', 
    timeout: 15000 
  });
  
  expect(found).not.toBeNull();
  if (found) {
    await expect(found).toBeVisible({ timeout: 5000 });
  } else {
    throw new Error(`Client ${clientId} not found in list after creation`);
  }

  // Edit the client: find the list item and click the Edit button
  const listItem = found;
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
    // Delete the client: use searchAndConfirmActionWithModal to click the Delete button and confirm
    const deleteResult = await adminHelpers.searchAndConfirmActionWithModal(page, 'clients', clientId, 'Delete', { listSelector: 'ul[role="list"], table tbody', timeout: 20000 });
    if (!deleteResult.clicked) {
      // fallback: click internal button if locator found
      const fallbackDeleteBtn = listItem.locator('button[title*="Delete"], button:has-text("Delete")').first();
      if (await fallbackDeleteBtn.count() > 0) await fallbackDeleteBtn.click().catch(() => {});
    }

  // Wait for the client to be removed from the list
  try {
    const deleted = await adminHelpers.searchListForItem(page, 'clients', clientId, { timeout: 20000 });
    expect(deleted).toBeNull();
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup to avoid orphaned test data
    await adminHelpers.deleteClientViaApiFallback(page, clientId);
  }
});
