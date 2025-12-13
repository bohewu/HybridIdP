import { test, expect } from '../fixtures';

// Scopes CRUD tests - API + simple UI flow.
// Note: ScopesApi not yet implemented, using page API for now.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Scopes CRUD', () => {
  test('Scopes page loads and shows list (UI)', async ({ page }) => {
    // Navigate to admin scopes page
    await page.goto('https://localhost:7035/Admin/Scopes');
    await page.waitForURL(/\/Admin\/Scopes/);

    // Wait for table to load
    await page.waitForSelector('table', { timeout: 10000 });

    // Verify Create button is visible
    const createBtn = page.locator('button:has-text("Create New Scope")');
    await expect(createBtn).toBeVisible({ timeout: 5000 });

    // Verify table has some content (should have standard scopes like openid, profile)
    await expect(page.locator('table')).toContainText(/openid|profile/i, { timeout: 10000 });
  });
});
