import { test, expect } from '../fixtures';

/**
 * E2E Integration Tests: Role Permissions in User Claims
 * 
 * These tests verify that role permissions are correctly reflected in user claims
 * after login through the OIDC flow. Uses hybrid pattern - API for setup, UI for OIDC flow.
 */

test.describe('Role Permissions in User Claims - E2E Integration', () => {
  test('Should include role and permissions in user claims after login', async ({ browser, api }) => {
    const timestamp = Date.now();

    // 1. Arrange (API) - Create role with permissions and user
    const roleName = `e2e-claims-role-${timestamp}`;
    const permissions = ['users.read', 'clients.read'];
    const role = await api.roles.create(roleName, 'Test role', permissions);

    const testUserEmail = `e2e-claims-${timestamp}@hybridauth.local`;
    const testUserPassword = `Test@${timestamp}`;

    const testUser = await api.users.create({
      email: testUserEmail,
      userName: testUserEmail,
      firstName: 'Claims',
      lastName: 'Test',
      password: testUserPassword
    });
    await api.users.assignRoles(testUser.id, [role.id]);

    // 2. Act (UI) - Login via TestClient OIDC flow
    const userContext = await browser.newContext();
    const userPage = await userContext.newPage();

    try {
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');
      await userPage.waitForURL(/https:\/\/localhost:7035/, { timeout: 10000 });
      await userPage.waitForSelector('#Input_Login', { timeout: 10000 });

      await userPage.fill('#Input_Login', testUserEmail);
      await userPage.fill('#Input_Password', testUserPassword);
      await userPage.click('button.auth-btn-primary');

      // Handle consent if needed
      const allowBtn = userPage.locator('button[name="submit"][value="allow"]');
      if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
        await allowBtn.click();
      }

      await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });

      // 3. Assert (UI) - Verify claims in profile
      const claimsTable = userPage.locator('table');
      await expect(claimsTable).toBeVisible();
      await expect(claimsTable).toContainText(testUserEmail);
      await expect(claimsTable).toContainText('role');
      await expect(claimsTable).toContainText(roleName);

      for (const permission of permissions) {
        await expect(claimsTable).toContainText(permission);
      }

      // Logout
      await userPage.click('a:has-text("Logout")');
      await userPage.waitForURL('https://localhost:7001/', { timeout: 10000 });
    } finally {
      await userContext.close().catch(() => { });
    }

    // 4. Cleanup (API)
    await api.users.deleteUser(testUser.id);
    await api.roles.deleteRole(role.id);
  });

  test('Should reflect permission changes after role reassignment', async ({ browser, api }) => {
    const timestamp = Date.now();

    // 1. Arrange (API) - Create two roles and user
    const role1Name = `e2e-role1-${timestamp}`;
    const role1 = await api.roles.create(role1Name, 'Role 1', ['users.read']);

    const role2Name = `e2e-role2-${timestamp}`;
    const role2 = await api.roles.create(role2Name, 'Role 2', ['scopes.read']);

    const testUserEmail = `e2e-reassign-${timestamp}@hybridauth.local`;
    const testUserPassword = `Test@${timestamp}`;

    const testUser = await api.users.create({
      email: testUserEmail,
      userName: testUserEmail,
      firstName: 'Reassign',
      lastName: 'Test',
      password: testUserPassword
    });
    await api.users.assignRoles(testUser.id, [role1.id]);

    // 2. Act (UI) - First login, verify role1
    const userContext = await browser.newContext();
    const userPage = await userContext.newPage();

    try {
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');
      await userPage.waitForURL(/https:\/\/localhost:7035/, { timeout: 10000 });
      await userPage.fill('#Input_Login', testUserEmail);
      await userPage.fill('#Input_Password', testUserPassword);
      await userPage.click('button.auth-btn-primary');

      const allowBtn = userPage.locator('button[name="submit"][value="allow"]');
      if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
        await allowBtn.click();
      }

      await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });
      await expect(userPage.locator('table')).toContainText(role1Name);

      await userPage.click('a:has-text("Logout")');
      await userPage.waitForURL('https://localhost:7001/', { timeout: 10000 });
    } finally {
      await userContext.close().catch(() => { });
    }

    // 3. Reassign (API) - Switch to role2
    await api.users.assignRoles(testUser.id, [role2.id]);

    // 4. Act (UI) - Second login, verify role2
    const userContext2 = await browser.newContext();
    const userPage2 = await userContext2.newPage();

    try {
      await userPage2.goto('https://localhost:7001/');
      await userPage2.click('a:has-text("Login")');
      await userPage2.waitForURL(/https:\/\/localhost:7035/, { timeout: 10000 });
      await userPage2.fill('#Input_Login', testUserEmail);
      await userPage2.fill('#Input_Password', testUserPassword);
      await userPage2.click('button.auth-btn-primary');

      const allowBtn2 = userPage2.locator('button[name="submit"][value="allow"]');
      if (await allowBtn2.count() > 0 && await allowBtn2.isVisible()) {
        await allowBtn2.click();
      }

      await userPage2.waitForURL('**/Account/Profile', { timeout: 20000 });

      // Verify role2, not role1
      await expect(userPage2.locator('table')).toContainText(role2Name);
      await expect(userPage2.locator('table')).not.toContainText(role1Name);

      await userPage2.click('a:has-text("Logout")');
    } finally {
      await userContext2.close().catch(() => { });
    }

    // 5. Cleanup (API)
    await api.users.deleteUser(testUser.id);
    await api.roles.deleteRole(role1.id);
    await api.roles.deleteRole(role2.id);
  });
});
