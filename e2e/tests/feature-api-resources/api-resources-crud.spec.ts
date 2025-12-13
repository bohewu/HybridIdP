import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe('Admin - API Resources CRUD', () => {
  test('Create and Delete API Resource via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const resourceName = `e2e-api-${timestamp}`;
    const displayName = `E2E API ${timestamp}`;

    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/ApiResources');

    // Create using robust ID
    await page.click('[data-test-id="resources-create-btn"]');

    // Form might be a modal
    await page.waitForSelector('[data-test-id="resources-name-input"]');

    await page.fill('[data-test-id="resources-name-input"]', resourceName);
    await page.fill('[data-test-id="resources-display-name-input"]', displayName);
    await page.fill('[data-test-id="resources-description-input"]', 'Test Resource');

    // Submit
    await page.click('[data-test-id="resources-save-btn"]');

    // Verify success (list update)
    // Search to isolate
    await page.fill('[data-test-id="resources-search-input"]', resourceName);
    // Wait for list to update
    await expect(page.locator('table')).toContainText(resourceName);

    // Delete
    const row = page.locator(`tr:has-text("${resourceName}")`);
    await expect(row).toBeVisible();

    // Handle dialog
    page.once('dialog', dialog => dialog.accept());

    await row.locator('[data-test-id="resources-delete-btn"]').click();

    // Verify gone
    await expect(row).not.toBeVisible();
    await expect(page.locator('table')).not.toContainText(resourceName);
  });
});
