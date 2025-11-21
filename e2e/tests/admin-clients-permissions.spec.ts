import { test, expect } from '@playwright/test';
import adminHelpers from './helpers/admin';

test('Admin - Clients permission denied (create/update/delete)', async ({ page }) => {
  // Login as admin and create role and user with read-only permission
  await adminHelpers.loginAsAdminViaIdP(page);
  const timestamp = Date.now();
  const role = await adminHelpers.createRole(page, `e2e-read-only-${timestamp}`, ['clients.read']);
  const userEmail = `e2e-user-${timestamp}@hybridauth.local`;
  const userPassword = `E2E!${timestamp}a`;
  const user = await adminHelpers.createUserWithRole(page, userEmail, userPassword, [role.name]);

  // Logout admin
  await page.goto('https://localhost:7035/Account/Logout');
  // Now login as the read-only user
  await adminHelpers.login(page, userEmail, userPassword);
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Admin\/Clients/);

  // Create button should NOT be visible
  const createBtn = page.locator('button:has-text("Create New Client")');
  await expect(createBtn).toHaveCount(0);

  // Check the first list item doesn't have Edit/Delete for this user
  const listItem = page.locator('ul[role="list"] li').first();
  await expect(listItem.locator('button[title*="Edit"]').first()).toHaveCount(0);
  await expect(listItem.locator('button[title*="Delete"]').first()).toHaveCount(0);

  // Cleanup: logout user and delete the created test user/role using admin
  await page.goto('https://localhost:7035/Account/Logout');
  await adminHelpers.loginAsAdminViaIdP(page);
  await adminHelpers.deleteUser(page, user.id);
  await adminHelpers.deleteRole(page, role.id);
});
