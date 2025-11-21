import { test, expect } from '@playwright/test';
import adminHelpers from './helpers/admin';

/**
 * E2E Integration Tests: Role Permissions in User Claims
 * 
 * These tests verify that role permissions are correctly reflected in user claims
 * after login through the OIDC flow. This ensures end-to-end integration between:
 * - Role assignment API
 * - OIDC authentication flow
 * - Claims transformation
 * - Token generation
 */

test.describe('Role Permissions in User Claims - E2E Integration', () => {
  test('Should include role and permissions in user claims after login', async ({ browser }) => {
    // Create separate contexts for admin and test user to avoid session conflicts
    const adminContext = await browser.newContext();
    const adminPage = await adminContext.newPage();
    
    const userContext = await browser.newContext();
    const userPage = await userContext.newPage();
    
    try {
      // ===== SETUP: Create role with specific permissions =====
      await adminHelpers.loginAsAdminViaIdP(adminPage);
      const timestamp = Date.now();
      
      // Create a role with specific permissions
      const roleName = `e2e-test-role-${timestamp}`;
      const permissions = ['users.read', 'clients.read'];
      const role = await adminHelpers.createRole(adminPage, roleName, permissions);
      
      // Create a test user with this role
      const testUserEmail = `e2e-claims-user-${timestamp}@hybridauth.local`;
      const testUserPassword = `Test@${timestamp}`;
      
      const testUser = await adminPage.evaluate(async (args: any) => {
        const userPayload = {
          username: args.email,
          email: args.email,
          password: args.password,
          emailConfirmed: true
        };
        const r = await fetch('https://localhost:7035/api/admin/users', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(userPayload)
        });
        if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
        return r.json();
      }, { email: testUserEmail, password: testUserPassword });
      
      // Assign the role to the user using the ID-based endpoint
      await adminPage.evaluate(async (args: any) => {
        const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ RoleIds: [args.roleId] })
        });
        if (!r.ok) throw new Error(`Failed to assign role: ${r.status}`);
      }, { userId: testUser.id, roleId: role.id });
      
      // ===== TEST: Login via TestClient and verify claims =====
      
      // Navigate to TestClient
      await userPage.goto('https://localhost:7001/');
      
      // Click Login to trigger OIDC flow
      await userPage.click('a:has-text("Login")');
      
      // Should redirect to IdP login page - wait for it
      await userPage.waitForURL(/https:\/\/localhost:7035/, { timeout: 10000 });
      
      // Wait for login form to be ready
      await userPage.waitForSelector('#Input_Login', { timeout: 10000 });
      
      // Login with the test user credentials
      await userPage.fill('#Input_Login', testUserEmail);
      await userPage.fill('#Input_Password', testUserPassword);
      await userPage.click('button.auth-btn-primary');
      
      // Handle consent page if it appears
      const allowBtn = userPage.locator('button[name="submit"][value="allow"]');
      if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
        await allowBtn.click();
      }
      
      // Wait for redirect back to TestClient profile
      await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });
      
      // ===== VERIFY: Check claims in the profile page =====
      
      // Get the claims table
      const claimsTable = userPage.locator('table');
      await expect(claimsTable).toBeVisible();
      
      // Verify the user's email is in the claims
      await expect(claimsTable).toContainText(testUserEmail);
      
      // Verify the role claim is present
      await expect(claimsTable).toContainText('role');
      await expect(claimsTable).toContainText(roleName);
      
      // Verify permissions are present in claims
      // Permissions should be included as custom claims
      for (const permission of permissions) {
        await expect(claimsTable).toContainText(permission);
      }
      
      // ===== VERIFY: Check access token contains claims =====
      
      // Get the access token from the page
      const accessToken = await userPage.locator('textarea').first().inputValue();
      expect(accessToken).toBeTruthy();
      expect(accessToken.length).toBeGreaterThan(50);
      
      // ===== CLEANUP =====
      
      // Logout from TestClient
      await userPage.click('a:has-text("Logout")');
      await userPage.waitForURL('https://localhost:7001/', { timeout: 10000 });
      
      // Delete the test user and role
      await adminHelpers.deleteUser(adminPage, testUser.id);
      await adminHelpers.deleteRole(adminPage, role.id);
    } finally {
      // Close contexts
      await adminContext.close();
      await userContext.close();
    }
  });

  test('Should reflect permission changes after role reassignment', async ({ browser }) => {
    // Create separate contexts for admin and test user to avoid session conflicts
    const adminContext = await browser.newContext();
    const adminPage = await adminContext.newPage();
    
    const userContext = await browser.newContext();
    const userPage = await userContext.newPage();
    
    try {
      // ===== SETUP: Create two roles with different permissions =====
      await adminHelpers.loginAsAdminViaIdP(adminPage);
      const timestamp = Date.now();
      
      const role1Name = `e2e-test-role-1-${timestamp}`;
      const role1Permissions = ['users.read', 'clients.read'];
      const role1 = await adminHelpers.createRole(adminPage, role1Name, role1Permissions);
      
      const role2Name = `e2e-test-role-2-${timestamp}`;
      const role2Permissions = ['scopes.read', 'roles.read'];
      const role2 = await adminHelpers.createRole(adminPage, role2Name, role2Permissions);
      
      // Create a test user
      const testUserEmail = `e2e-claims-reassign-user-${timestamp}@hybridauth.local`;
      const testUserPassword = `Test@${timestamp}`;
      
      const testUser = await adminPage.evaluate(async (args: any) => {
        const userPayload = {
          username: args.email,
          email: args.email,
          password: args.password,
          emailConfirmed: true
        };
        const r = await fetch('https://localhost:7035/api/admin/users', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(userPayload)
        });
        if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
        return r.json();
      }, { email: testUserEmail, password: testUserPassword });
      
      // Assign role1 to the user
      await adminPage.evaluate(async (args: any) => {
        const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ RoleIds: [args.roleId] })
        });
        if (!r.ok) throw new Error(`Failed to assign role: ${r.status}`);
      }, { userId: testUser.id, roleId: role1.id });
      
      // ===== TEST: First login - should see role1 permissions =====
      
      // Navigate to TestClient
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
      
      // Verify role1 and its permissions
      const claimsTable = userPage.locator('table');
      await expect(claimsTable).toContainText(role1Name);
      for (const permission of role1Permissions) {
        await expect(claimsTable).toContainText(permission);
      }
      
      // Logout
      await userPage.click('a:has-text("Logout")');
      await userPage.waitForURL('https://localhost:7001/', { timeout: 10000 });
      
      // Close first user context and create a new one for fresh session
      await userContext.close();
      
      // ===== REASSIGN: Switch user to role2 =====
      
      await adminPage.evaluate(async (args: any) => {
        const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ RoleIds: [args.roleId] })
        });
        if (!r.ok) throw new Error(`Failed to reassign role: ${r.status}`);
      }, { userId: testUser.id, roleId: role2.id });
      
      // ===== TEST: Second login - should see role2 permissions =====
      
      // Create a fresh context for second login
      const userContext2 = await browser.newContext();
      const userPage2 = await userContext2.newPage();
      
      await userPage2.goto('https://localhost:7001/');
      await userPage2.click('a:has-text("Login")');
      await userPage2.waitForURL(/https:\/\/localhost:7035/, { timeout: 10000 });
      await userPage2.waitForSelector('#Input_Login', { timeout: 10000 });
      await userPage2.fill('#Input_Login', testUserEmail);
      await userPage2.fill('#Input_Password', testUserPassword);
      await userPage2.click('button.auth-btn-primary');
      
      // Consent might be cached, but check anyway
      const allowBtn2 = userPage2.locator('button[name="submit"][value="allow"]');
      if (await allowBtn2.count() > 0 && await allowBtn2.isVisible()) {
        await allowBtn2.click();
      }
      
      await userPage2.waitForURL('**/Account/Profile', { timeout: 20000 });
      
      // Get the claims table from the second page
      const claimsTable2 = userPage2.locator('table');
      
      // Verify role2 and its permissions (role1 permissions should NOT be present)
      await expect(claimsTable2).toContainText(role2Name);
      for (const permission of role2Permissions) {
        await expect(claimsTable2).toContainText(permission);
      }
      
      // role1 permissions should NOT be present
      await expect(claimsTable2).not.toContainText(role1Name);
      
      // ===== CLEANUP =====
      
      await userPage2.click('a:has-text("Logout")');
      await userPage2.waitForURL('https://localhost:7001/', { timeout: 10000 });
      
      // Delete test user and roles
      await adminHelpers.deleteUser(adminPage, testUser.id);
      await adminHelpers.deleteRole(adminPage, role1.id);
      await adminHelpers.deleteRole(adminPage, role2.id);
      
      // Close second user context
      await userContext2.close();
    } finally {
      // Close contexts
      await adminContext.close();
    }
  });
});
