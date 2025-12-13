import { test, expect } from '../fixtures';

// Users CRUD tests - hybrid pattern with comprehensive UI test.
// CRITICAL: Test actual UI interactions with data-test-id.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users CRUD', () => {
  test('Create user via API and verify in list with data-test-id', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-list-${timestamp}@hybridauth.local`;

    // Create user via API
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'List',
      lastName: 'Test',
      password: `E2E!${timestamp}a`
    });

    // Navigate to users page
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Admin\/Users/);

    // Verify user appears using data-test-id selector
    const userRowSelector = `[data-test-id="user-row-${user.id}"]`;
    await expect(page.locator(userRowSelector)).toBeVisible({ timeout: 10000 });

    // Cleanup
    await api.users.deleteUser(user.id);
  });

  test('Create and delete user via API', async ({ api }) => {
    const timestamp = Date.now();
    const email = `e2e-crud-${timestamp}@hybridauth.local`;

    // Create user
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'CRUD',
      lastName: 'Test',
      password: `E2E!${timestamp}a`
    });

    expect(user.id).toBeTruthy();
    expect(user.email).toBe(email);

    // Delete user
    await api.users.deleteUser(user.id);
  });
});
