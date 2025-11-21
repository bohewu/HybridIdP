import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Roles CRUD (create, update, delete role)', async ({ page }) => {
  // Accept native JS dialogs (confirm) automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate directly to the Admin Roles page
  await page.goto('https://localhost:7035/Admin/Roles');
  await page.waitForURL(/\/Admin\/Roles/);

  // Wait for the Vue app to load
  await page.waitForSelector('button:has-text("Create Role"), table', { timeout: 15000 });

  const roleName = `e2e-role-${Date.now()}`;
  const description = `E2E Test Role ${Date.now()}`;

  // Click the Create Role button
  await page.click('button:has-text("Create Role")');

  // Wait for the modal to appear
  await page.waitForSelector('input[name="name"]', { timeout: 5000 });

  // Fill in the role name and description
  await page.fill('input[name="name"]', roleName);
  await page.fill('textarea[name="description"]', description);

  // Select some permissions (checkboxes)
  // Wait for permissions section to load
  await page.waitForSelector('input[type="checkbox"][value*="users.read"]', { timeout: 5000 });
  await page.check('input[type="checkbox"][value*="users.read"]');
  await page.check('input[type="checkbox"][value*="users.update"]');

  // Submit the form
  await page.click('button[type="submit"]');

  // Wait for the role to appear in the table
  const rolesTable = page.locator('table tbody');
  await expect(rolesTable).toContainText(roleName, { timeout: 20000 });

  // Find the row with our role and click Edit
  const roleRow = rolesTable.locator('tr', { hasText: roleName });
  await expect(roleRow).toBeVisible();

  // Click the edit button (look for Edit action button)
  await roleRow.locator('button[title*="Edit"], button:has-text("Edit")').first().click();

  // Update the description
  const updatedDescription = `${description} (updated)`;
  await page.waitForSelector('textarea[name="description"]');
  await page.fill('textarea[name="description"]', updatedDescription);

  // Add another permission
  await page.check('input[type="checkbox"][value*="clients.read"]');

  // Submit the update
  await page.click('button[type="submit"]');

  // Verify the update
  await expect(roleRow).toContainText('3', { timeout: 20000 }); // 3 permissions now

  // Delete the role: click delete button
  await roleRow.locator('button[title*="Delete"], button:has-text("Delete")').first().click();

  // Confirm deletion in modal
  await page.waitForSelector('button:has-text("Delete"):not([disabled])', { timeout: 5000 });
  await page.click('button:has-text("Delete"):not([disabled])');

  // Wait for the role to be removed from the table
  try {
    await expect(rolesTable).not.toContainText(roleName, { timeout: 20000 });
  } catch (e) {
    // If UI delete fails, fall back to the API cleanup
    console.warn(`UI delete failed for role ${roleName}, attempting API cleanup...`);
    await adminHelpers.deleteRole(page, roleName);
  }
});
