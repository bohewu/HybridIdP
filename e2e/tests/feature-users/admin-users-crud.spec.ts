import { test, expect } from '../fixtures';

// Users CRUD tests using hybrid pattern - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users CRUD', () => {
  test('Create user via API', async ({ api }) => {
    const timestamp = Date.now();
    const email = `e2e-user-${timestamp}@hybridauth.local`;

    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    });
    expect(user.id).toBeTruthy();
    expect(user.email).toBe(email);

    // Cleanup
    await api.users.deleteUser(user.id);
  });

  test('Verify user appears in admin list (UI)', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-ui-${timestamp}@hybridauth.local`;

    // Create user via API
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'UiTest',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    });

    // Navigate to admin users page
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Admin\/Users/);

    // Search for user
    const searchInput = page.locator('input[placeholder*="Search" i]');
    await searchInput.fill(email);
    await page.waitForTimeout(500);

    // Verify user row appears using data-test-id
    const userRow = page.locator(`[data-test-id="user-row-${user.id}"]`);
    await expect(userRow).toBeVisible({ timeout: 10000 });

    // Cleanup
    await api.users.deleteUser(user.id);
  });
});
