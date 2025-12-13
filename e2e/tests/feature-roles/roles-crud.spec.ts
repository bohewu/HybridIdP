import { test, expect } from '../fixtures';

// Roles CRUD tests - hybrid pattern with simple UI flow.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Roles CRUD', () => {
  test('Create and delete role via API', async ({ api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-role-${timestamp}`;

    const role = await api.roles.create(roleName, 'E2E Test Role', ['users.read']);
    expect(role.id).toBeTruthy();
    expect(role.name).toBe(roleName);

    // Cleanup
    await api.roles.deleteRole(role.id);
  });

  test('Verify role appears in admin list (UI)', async ({ page, api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-ui-role-${timestamp}`;

    // Create role via API
    const role = await api.roles.create(roleName, 'UI Test Role', ['users.read']);

    // Navigate to admin roles page
    await page.goto('https://localhost:7035/Admin/Roles');
    await page.waitForURL(/\/Admin\/Roles/);

    // Wait for table to load
    await page.waitForSelector('table', { timeout: 10000 });

    // Verify role appears in table
    await expect(page.locator('table')).toContainText(roleName, { timeout: 10000 });

    // Cleanup
    await api.roles.deleteRole(role.id);
  });
});
