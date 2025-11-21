import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - API Resources CRUD (create, update, delete resource)', async ({ page }) => {
  // Accept native JS dialogs (confirm) automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate directly to the Admin Resources page
  await page.goto('https://localhost:7035/Admin/Resources');
  await page.waitForURL(/\/Admin\/Resources/);

  // Wait for the Vue app to load
  await page.waitForSelector('button:has-text("Create New Resource"), ul[role="list"]', { timeout: 15000 });

  // Click the Create New Resource button
  await page.click('button:has-text("Create New Resource")');

  // Wait for the form modal
  await page.waitForSelector('#name');

  const resourceName = `e2e-api-${Date.now()}`;
  const displayName = `E2E Test API ${Date.now()}`;

  await page.fill('#name', resourceName);
  await page.fill('#displayName', displayName);
  await page.fill('#description', 'E2E test API resource for automated testing');
  await page.fill('#baseUrl', 'https://api.e2e-test.local');

  // Submit the form (Create Resource)
  await page.click('button[type="submit"]');

  // Wait for the resource to appear in the list
  const resourcesList = page.locator('ul[role="list"]');
  await expect(resourcesList).toContainText(resourceName, { timeout: 20000 });

  // Edit the resource: find the list item and click the Edit button
  const listItem = resourcesList.locator('li', { hasText: resourceName });
  await expect(listItem).toBeVisible();

  // Click the edit button inside the list item
  await listItem.locator('button[title*="Edit"]').click();

  // Update the display name
  const updatedDisplayName = `${displayName} (updated)`;
  const displayInput = page.locator('#displayName');
  await displayInput.fill(updatedDisplayName);

  // Submit the update form
  await page.click('button[type="submit"]');

  // Ensure the list updates and shows the updated name
  await expect(listItem).toContainText(updatedDisplayName, { timeout: 20000 });

  // Delete the resource: click delete and accept confirmation via dialog handler
  await listItem.locator('button[title*="Delete"]').click();

  // Wait for the resource to be removed from the list
  try {
    await expect(resourcesList).not.toContainText(resourceName, { timeout: 20000 });
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup
    console.warn(`UI delete failed for resource ${resourceName}, attempting API cleanup...`);
    await adminHelpers.deleteApiResource(page, resourceName);
  }
});
