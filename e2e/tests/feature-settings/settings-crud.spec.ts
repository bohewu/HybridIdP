import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Settings', () => {
  test('Toggle system setting via UI', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Settings');
    await page.waitForSelector('form');

    // Find a boolean setting, e.g. "Self Registration" or similar if available
    // OR just find the first checkbox in the form
    const checkbox = page.locator('input[type="checkbox"]').first();

    // Ensure it's visible or clickable (handle custom checkbox UI)
    // If it's a "switch", the input might be hidden. Force click label or wrapper.
    const wrapper = checkbox.locator('xpath=..'); // Parent label usually
    await expect(wrapper).toBeVisible();

    const initialChecked = await checkbox.isChecked();

    // Toggle
    await wrapper.click();

    // Save
    await page.click('button[type="submit"]');

    // Verify success
    await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();

    // Reload to verify persistence
    await page.reload();
    await page.waitForSelector('form');

    // Check state flipped
    const newChecked = await page.locator('input[type="checkbox"]').first().isChecked();
    expect(newChecked).toBe(!initialChecked);

    // Revert
    await page.locator('input[type="checkbox"]').first().locator('xpath=..').click();
    await page.click('button[type="submit"]');
    await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();
  });
});
