import { test, expect } from '../fixtures';

// Roles CRUD tests - comprehensive UI flow tests.
// Test creating roles with permission selection via UI.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Roles CRUD (UI Flows)', () => {
  test('Create role with permissions via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-ui-role-${timestamp}`;

    // Navigate to roles page
    await page.goto('https://localhost:7035/Admin/Roles');
    await page.waitForURL(/\/Admin\/Roles/);

    // Click Create Role button
    await page.click('button:has-text("Create Role")');

    // Wait for form modal
    await page.waitForSelector('#name, input[name="name"]', { timeout: 10000 });

    // Fill role name and description
    await page.fill('#name, input[name="name"]', roleName);
    await page.fill('#description, textarea[name="description"]', 'UI Test Role');

    // Select permissions (wait for permissions to load)
    await page.waitForSelector('input[type="checkbox"][id^="perm-"], input[id*="users.read"]', { timeout: 10000 });

    // Select a few permissions
    await page.check('input[id="perm-users.read"]', { force: true });
    await page.check('input[id="perm-roles.read"]', { force: true });

    // Submit form
    await page.click('button[type="submit"]:has-text("Save"), button[type="submit"]:has-text("Create")');

    // Wait for success
    await page.waitForTimeout(2000);

    // Verify role appears in table
    await expect(page.locator('table')).toContainText(roleName, { timeout: 10000 });

    // Verify permissions were saved
    const roleData = await page.evaluate(async (name: string) => {
      const r = await fetch(`/api/admin/roles?search=${encodeURIComponent(name)}`);
      if (!r.ok) return null;
      const data = await r.json();
      const items = Array.isArray(data) ? data : (data.items || []);
      return items.find((role: any) => role.name === name);
    }, roleName);

    expect(roleData).toBeTruthy();
    expect(roleData.permissions).toContain('users.read');
    expect(roleData.permissions).toContain('roles.read');

    // Cleanup
    await api.roles.deleteRole(roleData.id);
  });

  test('Update role permissions via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-update-role-${timestamp}`;

    // Create role via API
    const role = await api.roles.create(roleName, 'Original', ['users.read']);

    // Navigate to roles page
    await page.goto('https://localhost:7035/Admin/Roles');
    await page.waitForURL(/\/Admin\/Roles/);

    // Find role and click Edit
    const roleRow = page.locator('tr').filter({ hasText: roleName }).first();
    await expect(roleRow).toBeVisible({ timeout: 10000 });
    await roleRow.locator('button[title*="Edit"], button:has-text("Edit")').first().click();

    // Wait for edit form
    await page.waitForSelector('#description', { timeout: 10000 });

    // Add another permission
    await page.check('input[id="perm-roles.update"]', { force: true });

    // Submit
    await page.click('button[type="submit"]');
    await page.waitForTimeout(2000);

    // Verify permission was added
    const updated = await page.evaluate(async (id: string) => {
      const r = await fetch(`/api/admin/roles/${id}`);
      return r.ok ? r.json() : null;
    }, role.id);

    expect(updated.permissions).toContain('roles.update');
    expect(updated.permissions.length).toBeGreaterThanOrEqual(2);

    // Cleanup
    await api.roles.deleteRole(role.id);
  });

  test('Role appears in admin list (UI verification)', async ({ page, api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-list-role-${timestamp}`;

    // Create role via API
    const role = await api.roles.create(roleName, 'List Test Role', ['users.read']);

    // Navigate to roles page
    await page.goto('https://localhost:7035/Admin/Roles');
    await page.waitForURL(/\/Admin\/Roles/);

    // Wait for table to load
    await page.waitForSelector('table', { timeout: 10000 });

    // Verify role appears in table
    await expect(page.locator('table')).toContainText(roleName, { timeout: 10000 });

    // Verify permissions count shows in table
    const roleRow = page.locator('tr').filter({ hasText: roleName }).first();
    await expect(roleRow).toContainText('1'); // 1 permission

    // Cleanup
    await api.roles.deleteRole(role.id);
  });
});
