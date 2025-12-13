import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Localization', () => {
  test('Edit resource string via UI', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Localization');

    // Wait for grid
    await page.waitForSelector('table', { timeout: 10000 });

    // Search for a known key "Login"
    await page.fill('input[placeholder*="Search"]', 'Login');

    // Wait for filter
    await page.waitForTimeout(500);

    // Find the row (assuming standard table structure)
    // Click Edit on the first row
    await page.locator('button[title="Edit"], a:has-text("Edit")').first().click();

    // Modal should appear
    await page.waitForSelector('div[role="dialog"]');

    // Modify a value for a specific culture (e.g., en-US or zh-TW)
    // Assuming inputs are named by culture or dynamic
    // Try to find an input that has a value (to verify it's loaded)
    const inputs = page.locator('div[role="dialog"] input[type="text"]');
    await expect(inputs.first()).toBeVisible();

    const originalValue = await inputs.first().inputValue();
    const newValue = originalValue + ' [Test]';

    await inputs.first().fill(newValue);

    // Save
    await page.click('button[type="submit"]');

    // Verify success
    await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();

    // Re-open to revert (optional but good practice)
    // For E2E, we might skip revert if database is reset, but let's try to be clean
    await page.locator('button[title="Edit"], a:has-text("Edit")').first().click();
    await page.waitForSelector('div[role="dialog"]');
    await inputs.first().fill(originalValue);
    await page.click('button[type="submit"]');
    await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();
  });
});
