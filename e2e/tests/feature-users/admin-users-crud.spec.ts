import { test, expect } from '../fixtures';

// Users CRUD tests - comprehensive UI flow tests.
// CRITICAL: Test actual UI interactions, not just API calls.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users CRUD (UI Flows)', () => {
  test('Create user via UI form', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-ui-user-${timestamp}@hybridauth.local`;

    // Navigate to users page
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Admin\/Users/);

    // Click Create User button
    await page.click('button:has-text("Create User"), button:has-text("Create New User")');

    // Wait for form modal
    await page.waitForSelector('form', { timeout: 10000 });

    // Fill form fields
    await page.fill('input[name="email"], #email', email);
    await page.fill('input[name="userName"], #userName', email);
    await page.fill('input[name="firstName"], #firstName', 'E2E');
    await page.fill('input[name="lastName"], #lastName', 'Test User');
    await page.fill('input[name="password"], #password, input[type="password"]', `E2E!${timestamp}a`);

    // Submit form
    await page.click('button[type="submit"]:has-text("Save"), button[type="submit"]:has-text("Create")');

    // Wait for success and modal close
    await page.waitForTimeout(2000);

    // Verify user appears in list
    await expect(page.locator('table, ul')).toContainText(email, { timeout: 10000 });

    // Cleanup via API
    const users = await api.users.list();
    const createdUser = users.items.find((u: any) => u.email === email);
    if (createdUser) {
      await api.users.deleteUser(createdUser.id);
    }
  });

  test('Update user details via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-update-${timestamp}@hybridauth.local`;

    // Create user via API for setup
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'Original',
      lastName: 'Name',
      password: `E2E!${timestamp}a`
    });

    // Navigate to users page
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Admin\/Users/);

    // Search for user
    const searchInput = page.locator('input[placeholder*="Search"], input[type="search"]').first();
    if (await searchInput.isVisible().catch(() => false)) {
      await searchInput.fill(email);
      await page.waitForTimeout(600);
    }

    // Find user row and click Edit
    const userRow = page.locator('tr, li').filter({ hasText: email }).first();
    await expect(userRow).toBeVisible({ timeout: 10000 });
    await userRow.locator('button[title*="Edit"], button:has-text("Edit")').first().click();

    // Wait for edit form
    await page.waitForSelector('form', { timeout: 10000 });

    // Update first name
    const firstNameInput = page.locator('input[name="firstName"], #firstName');
    await firstNameInput.fill('Updated');

    // Submit
    await page.click('button[type="submit"]:has-text("Save"), button[type="submit"]:has-text("Update")');
    await page.waitForTimeout(2000);

    // Verify update via API
    const updated = await page.evaluate(async (userId: string) => {
      const r = await fetch(`/api/admin/users/${userId}`);
      return r.ok ? r.json() : null;
    }, user.id);

    expect(updated.firstName).toBe('Updated');

    // Cleanup
    await api.users.deleteUser(user.id);
  });

  test('Assign role to user via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-role-${timestamp}@hybridauth.local`;

    // Create user and role via API
    const role = await api.roles.create(`e2e-role-${timestamp}`, 'Test Role', ['users.read']);
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'Role',
      lastName: 'Test',
      password: `E2E!${timestamp}a`
    });

    // Navigate to users page
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Admin\/Users/);

    // Find user and open role assignment
    const userRow = page.locator('tr, li').filter({ hasText: email }).first();
    await expect(userRow).toBeVisible({ timeout: 10000 });

    // Click Roles/Assign button
    await userRow.locator('button[title*="Role"], button:has-text("Roles"), button:has-text("Assign")').first().click();

    // Wait for role selection UI
    await page.waitForTimeout(2000);

    // Select the role (checkbox or dropdown)
    const roleCheckbox = page.locator(`input[type="checkbox"][value="${role.id}"], label:has-text("${role.name}")`).first();
    if (await roleCheckbox.isVisible().catch(() => false)) {
      await roleCheckbox.click();
    }

    // Save role assignment
    await page.click('button[type="submit"]:has-text("Save"), button:has-text("Assign")');
    await page.waitForTimeout(2000);

    // Verify role assignment via API
    const userWithRoles = await page.evaluate(async (userId: string) => {
      const r = await fetch(`/api/admin/users/${userId}`);
      return r.ok ? r.json() : null;
    }, user.id);

    expect(userWithRoles.roles || []).toContain(role.id);

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role.id);
  });

  test('User appears in admin list (data-test-id)', async ({ page, api }) => {
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
});
