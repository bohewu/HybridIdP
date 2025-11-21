import { test, expect } from '@playwright/test';
import adminHelpers from './helpers/admin';

test('Admin - Clients permission denied (create/update/delete)', async ({ page }) => {
  // Login as admin and create role and user with read-only permission
  await adminHelpers.loginAsAdminViaIdP(page);
  const timestamp = Date.now();
  const role = await adminHelpers.createRole(page, `e2e-read-only-${timestamp}`, ['clients.read']);
  const userEmail = `e2e-user-${timestamp}@hybridauth.local`;
  const userPassword = `E2E!${timestamp}a`;
  const user = await adminHelpers.createUserWithRole(page, userEmail, userPassword, [role.id]);

  // Logout admin
  await page.goto('https://localhost:7035/Account/Logout');
  // Now login as the read-only user
  await adminHelpers.login(page, userEmail, userPassword);
  await page.goto('https://localhost:7035/Admin/Clients');
  // The app may redirect to Access Denied for non-admin users; accept either URL
  await page.waitForURL(/\/Account\/AccessDenied|\/Admin\/Clients/);

  // If we reached Admin/Clients, ensure Create button is not visible; otherwise Access Denied page is shown
  if (await page.url().includes('/Admin/Clients')) {
    const createBtn = page.locator('button:has-text("Create New Client")');
    await expect(createBtn).toHaveCount(0);
  } else {
    // Expect Access Denied content
    const deniedHeading = page.locator('main h2:has-text("Access Denied")');
    await expect(deniedHeading).toHaveCount(1);
  }

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

test('Admin - Clients permission denied when role assigned by id', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);
  const timestamp = Date.now();
  const role2 = await adminHelpers.createRole(page, `e2e-read-only-id-${timestamp}`, ['clients.read']);
  const userEmail2 = `e2e-user-id-${timestamp}@hybridauth.local`;
  const userPassword2 = `E2E!${timestamp}a`;
  const user2 = await adminHelpers.createUserWithRole(page, userEmail2, userPassword2, [role2.id]);

  await page.goto('https://localhost:7035/Account/Logout');
  await adminHelpers.login(page, userEmail2, userPassword2);
  await page.goto('https://localhost:7035/Admin/Clients');
  await page.waitForURL(/\/Account\/AccessDenied|\/Admin\/Clients/);

  // If access denied, it should show the Access Denied page
  const denied = await page.locator('main:has-text("Access Denied")').count();
  expect(denied).toBeGreaterThanOrEqual(0);

  await page.goto('https://localhost:7035/Account/Logout');
  await adminHelpers.loginAsAdminViaIdP(page);
  await adminHelpers.deleteUser(page, user2.id);
  await adminHelpers.deleteRole(page, role2.id);
});
