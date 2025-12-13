import { test, expect } from '../fixtures';

// Claims CRUD tests - simple UI flow test.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Claims CRUD', () => {
  test('Claims page loads and shows list (UI)', async ({ page }) => {
    // Navigate to admin claims page
    await page.goto('https://localhost:7035/Admin/Claims');
    await page.waitForURL(/\/Admin\/Claims/);

    // Wait for table to load
    await page.waitForSelector('table', { timeout: 10000 });

    // Verify Create button is visible
    const createBtn = page.locator('button:has-text("Create Claim")');
    await expect(createBtn).toBeVisible({ timeout: 5000 });

    // Verify table has some content (should have standard claims like sub, email)
    await expect(page.locator('table')).toContainText(/sub|email|name/i, { timeout: 10000 });
  });
});
