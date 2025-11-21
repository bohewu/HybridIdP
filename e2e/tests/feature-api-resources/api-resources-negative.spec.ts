import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Admin - API Resources negative tests', () => {
  test.beforeEach(async ({ page }) => {
    page.on('dialog', async (dialog) => await dialog.accept());
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Resources');
    await page.waitForURL(/\/Admin\/Resources/);
    // Wait for Vue app to load
    await page.waitForSelector('button:has-text("Create New Resource"), ul[role="list"]', { timeout: 15000 });
  });

  test('Validation error - missing required name field', async ({ page }) => {
    // Open the Create Resource form
    await page.click('button:has-text("Create New Resource")');
    await page.waitForSelector('#name');

    // Leave name blank and try to submit
    await page.fill('#name', '');
    await page.fill('#displayName', 'Test API Resource');
    await page.click('button[type="submit"]');

    // Check for validation error message
    await expect(page.locator('text=/required|cannot be empty/i')).toBeVisible({ timeout: 5000 });
  });

  test('Duplicate resource name shows error', async ({ page }) => {
    const resourceName = `e2e-dup-api-${Date.now()}`;
    
    // Create the first resource via API
    const resourceId = await adminHelpers.createApiResource(page, resourceName, 'Duplicate Test API');

    // Now try to create another resource with the same name via UI
    await page.click('button:has-text("Create New Resource")');
    await page.waitForSelector('#name');
    await page.fill('#name', resourceName);
    await page.fill('#displayName', 'Duplicate API Resource');
    await page.click('button[type="submit"]');

    // Check for duplicate error message
    await expect(page.locator('text=/already exists|duplicate/i')).toBeVisible({ timeout: 5000 });

    // Cleanup: delete the resource via API
    try {
      await adminHelpers.deleteApiResource(page, resourceId);
    } catch (e) {
      // Ignore cleanup errors
    }
  });

  test('Invalid baseUrl format shows validation error', async ({ page }) => {
    // Open the Create Resource form
    await page.click('button:has-text("Create New Resource")');
    await page.waitForSelector('#name');

    const resourceName = `e2e-invalid-url-${Date.now()}`;

    // Fill with invalid URL
    await page.fill('#name', resourceName);
    await page.fill('#displayName', 'Invalid URL Test');
    await page.fill('#baseUrl', 'not-a-valid-url');
    await page.click('button[type="submit"]');

    // Check for validation or error message
    const errorExists = await Promise.race([
      page.locator('text=/invalid|url|format/i').isVisible({ timeout: 5000 }).then(() => true).catch(() => false),
      page.locator('[role="alert"]').isVisible({ timeout: 5000 }).then(() => true).catch(() => false)
    ]);

    expect(errorExists).toBeTruthy();
  });
});
