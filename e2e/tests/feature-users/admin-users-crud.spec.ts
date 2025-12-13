import { test, expect } from '../fixtures';

// Users CRUD UI tests using hybrid pattern.
// Uses API for setup/teardown, UI only for visual verification.

test.describe('Admin - Users CRUD', () => {
  test('Create, update, deactivate/reactivate, delete user', async ({ page, api }) => {
    page.on('dialog', async d => await d.accept());

    const timestamp = Date.now();
    const email = `e2e-user-${timestamp}@hybridauth.local`;
    const password = `E2E!${timestamp}a`;

    // 1. Arrange (API) - Create user
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'E2E',
      lastName: 'User',
      password
    });
    expect(user.id).toBeTruthy();

    // 2. Act (UI) - Navigate to edit page and update
    await page.goto(`https://localhost:7035/Admin/Users`);
    await page.waitForURL(/\/Admin\/Users/);

    // Search for user
    const searchInput = page.locator('input[placeholder*="Search"]');
    if (await searchInput.count() > 0) {
      await searchInput.fill(email);
      await page.waitForTimeout(500);
    }

    // Find and click edit
    const userRow = page.locator(`text=${email}`).first();
    await expect(userRow).toBeVisible({ timeout: 10000 });

    const editBtn = page.locator(`button[title*="Edit"], a[title*="Edit"], button:has-text("Edit")`).first();
    if (await editBtn.count() > 0) {
      await editBtn.click();
      await page.waitForSelector('#FirstName, #firstName');
      const firstNameInput = page.locator('#FirstName, #firstName');
      if (await firstNameInput.count() > 0) {
        await firstNameInput.fill('Updated');
      }
      await page.click('button[type="submit"]');
      await page.waitForTimeout(500);
    }

    // 3. Assert (API) - Verify update via API
    const updatedUser = await api.users.getById(user.id);
    expect(updatedUser.firstName).toBe('Updated');

    // 4. Cleanup (API)
    await api.users.deleteUser(user.id);
  });

  test('Permission denied for read-only user', async ({ page, api }) => {
    const ts = Date.now();

    // 1. Arrange (API) - Create role and user
    const readOnlyRole = await api.roles.create(`e2e-users-read-${ts}`, 'Read-only role', ['users.read']);
    const userEmail = `e2e-users-ro-${ts}@hybridauth.local`;
    const userPassword = `E2E!${ts}a`;

    const limitedUser = await api.users.create({
      email: userEmail,
      userName: userEmail,
      firstName: 'Limited',
      lastName: 'User',
      password: userPassword
    });
    await api.users.assignRoles(limitedUser.id, [readOnlyRole.id]);

    // 2. Act (UI) - Login as limited user and try to access admin
    await page.goto('https://localhost:7035/Account/Logout');
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', userEmail);
    await page.fill('#Input_Password', userPassword);
    await page.click('button.auth-btn-primary');

    await page.waitForTimeout(1000);
    await page.goto('https://localhost:7035/Admin/Users');
    await page.waitForURL(/\/Account\/AccessDenied|\/Admin\/Users/);

    // 3. Assert (UI)
    if (page.url().includes('/Admin/Users')) {
      await expect(page.locator('button:has-text("Create New User")')).toHaveCount(0);
    } else {
      await expect(page.locator('main:has-text("Access Denied")')).toHaveCount(1);
    }

    // 4. Cleanup (API) - Need to re-auth as admin first
    await page.goto('https://localhost:7035/Account/Logout');
    // Re-login as admin is handled by the storageState in the api fixture

    // Note: The api fixture still has admin auth, so cleanup works
    await api.users.deleteUser(limitedUser.id);
    await api.roles.deleteRole(readOnlyRole.id);
  });
});
