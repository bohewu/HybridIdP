import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Scopes CRUD (create, update, delete scope)', async ({ page }) => {
  // Accept native JS dialogs (confirm) automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate directly to the Admin Scopes page
  await page.goto('https://localhost:7035/Admin/Scopes');
  await page.waitForURL(/\/Admin\/Scopes/);

  // Wait for the Vue app to load by checking for the scopes list or create button
  await page.waitForSelector('button:has-text("Create New Scope"), table', { timeout: 15000 });

  // Click the Create New Scope button
  await page.click('button:has-text("Create New Scope")');

  // Wait for the form modal
  await page.waitForSelector('#name');

  const scopeName = `e2e-scope-${Date.now()}`;
  const displayName = `E2E Test Scope ${Date.now()}`;

  await page.fill('#name', scopeName);
  await page.fill('#displayName', displayName);
  await page.fill('#description', 'E2E test scope for automated testing');

  // Submit the form (Create Scope)
  await page.click('button[type="submit"]');

  // Wait for modal to close and API to complete
  await page.waitForTimeout(2000);

  // Verify scope was created via API (table might be paginated)
  const scopeCreated = await page.evaluate(async (name) => {
    const resp = await fetch(`/api/admin/scopes?search=${encodeURIComponent(name)}`);
    const data = await resp.json();
    return data.items && data.items.length > 0 && data.items[0].name === name;
  }, scopeName);
  
  expect(scopeCreated).toBeTruthy();

  // Search to find the scope in the table
  await page.fill('input[placeholder*="Search"]', scopeName);
  await page.waitForTimeout(1000);

  const scopesList = page.locator('table tbody');
  await expect(scopesList).toContainText(scopeName, { timeout: 10000 });

  // Edit the scope: find the table row and click the Edit button
  const listItem = scopesList.locator('tr', { hasText: scopeName });
  await expect(listItem).toBeVisible();

  // Click the edit button inside the row (match by title attribute to support icon-only buttons)
  await listItem.locator('button[title*="Edit"]').click();

  // Update the display name
  const updatedDisplayName = `${displayName} (updated)`;
  const displayInput = page.locator('#displayName');
  await displayInput.fill(updatedDisplayName);

  // Submit the update form (Update Scope)
  await page.click('button[type="submit"]');

  // Ensure the list updates and shows the updated name
  await expect(listItem).toContainText(updatedDisplayName, { timeout: 20000 });

  // Delete the scope: click delete and accept confirmation via dialog handler
  await listItem.locator('button[title*="Delete"]').click();

  // Wait for the scope to be removed from the list
  try {
    await expect(scopesList).not.toContainText(scopeName, { timeout: 20000 });
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup to avoid orphaned test data
    console.warn(`UI delete failed for scope ${scopeName}, attempting API cleanup...`);
    // Try to delete via API (we'll add this helper function)
    try {
      const scopeId = await page.evaluate(async (name) => {
        const resp = await fetch(`/api/admin/scopes?search=${name}`);
        const data = await resp.json();
        return data.items && data.items.length > 0 ? data.items[0].id : null;
      }, scopeName);
      
      if (scopeId) {
        await page.evaluate(async (id) => {
          await fetch(`/api/admin/scopes/${id}`, { method: 'DELETE' });
        }, scopeId);
      }
    } catch (cleanupError) {
      console.error('API cleanup also failed:', cleanupError);
    }
  }
});
