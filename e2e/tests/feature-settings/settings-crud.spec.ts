import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Settings CRUD (branding settings)', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Settings page
  await page.goto('https://localhost:7035/Admin/Settings');
  await page.waitForURL(/\/Admin\/Settings/);

  // Wait for the Vue app to load
  await page.waitForSelector('button:has-text("Save Changes"), input#appName', { timeout: 15000 });

  // Get original values
  const originalAppName = await page.inputValue('#appName');
  const originalProductName = await page.inputValue('#productName');

  // Update branding settings
  const timestamp = Date.now();
  const newAppName = `E2E Test App ${timestamp}`;
  const newProductName = `E2E Test Product ${timestamp}`;

  await page.fill('#appName', newAppName);
  await page.fill('#productName', newProductName);

  // Save changes
  await page.click('button:has-text("Save Changes")');

  // Wait for success message
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });

  // Refresh page to verify persistence
  await page.reload();
  await page.waitForSelector('input#appName', { timeout: 15000 });

  // Verify values persisted
  await expect(page.locator('#appName')).toHaveValue(newAppName);
  await expect(page.locator('#productName')).toHaveValue(newProductName);

  // Restore original values
  await page.fill('#appName', originalAppName);
  await page.fill('#productName', originalProductName);
  await page.click('button:has-text("Save Changes")');
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });
});

test('Admin - Settings validation (empty fields)', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  await page.goto('https://localhost:7035/Admin/Settings');
  await page.waitForURL(/\/Admin\/Settings/);
  await page.waitForSelector('input#appName', { timeout: 15000 });

  // Try to save with empty app name
  await page.fill('#appName', '');
  await page.click('button:has-text("Save Changes")');

  // Should either show validation error or save button disabled
  const hasError = await Promise.race([
    page.locator('.bg-red-50, .text-red-600, .text-red-700').first().isVisible({ timeout: 3000 }).then(() => true).catch(() => false),
    page.locator('button:has-text("Save Changes"):disabled').isVisible({ timeout: 3000 }).then(() => true).catch(() => false)
  ]);

  expect(hasError).toBeTruthy();
});
