import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Claims CRUD', () => {
  test('Create and Delete Claim Type via UI', async ({ page }) => {
    const timestamp = Date.now();
    const claimType = `e2e-claim-${timestamp}`;

    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Claims');

    // Create using robust ID
    await page.click('[data-test-id="claims-create-btn"]');

    // Wait for modal and fill form
    await page.fill('[data-test-id="claim-name-input"]', claimType);
    await page.fill('[data-test-id="claim-display-name-input"]', claimType);
    await page.fill('[data-test-id="claim-claim-type-input"]', claimType);
    await page.fill('[data-test-id="claim-property-path-input"]', 'ExtensionData.' + claimType);
    await page.fill('[data-test-id="claim-description-input"]', 'E2E Test Claim');

    // Select String (if not default)
    await page.selectOption('[data-test-id="claim-data-type-select"]', 'String');

    await page.click('[data-test-id="claim-save-btn"]');

    // Verify success - Modal should close and we might see a toaster or just list update
    // ClaimsApp.vue doesn't seem to show a global success alert, but it re-fetches.
    // We can just verify the item appears in the list.

    // Search for it to isolate
    await page.fill('[data-test-id="claims-search-input"]', claimType);
    // Wait for table to update (simple way: check for text)
    await expect(page.locator('table')).toContainText(claimType);

    // Delete
    // Find row
    const row = page.locator(`tr:has-text("${claimType}")`);

    // Setup dialog listener for confirm()
    page.once('dialog', dialog => dialog.accept());

    await row.locator('[data-test-id="claims-delete-btn"]').click();

    // Verify gone
    // Wait for refetch
    await expect(row).not.toBeVisible();
    await expect(page.locator('table')).not.toContainText(claimType);
  });
});
