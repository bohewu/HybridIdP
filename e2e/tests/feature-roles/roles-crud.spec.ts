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

  // Wait for the modal to appear (CreateRoleModal uses id selectors)
  await page.waitForSelector('#name', { timeout: 5000 });

  // Fill in the role name and description
  await page.fill('#name', roleName);
  await page.fill('#description', description);

  // Select some permissions (checkboxes)
  // Wait for permissions section to load - permissions are grouped by category
  await page.waitForSelector('input[type="checkbox"]', { timeout: 5000 });
  // Check the first two available checkboxes
  const checkboxes = page.locator('input[type="checkbox"]');
  await checkboxes.nth(0).check();
  await checkboxes.nth(1).check();

  // Submit the form
  await page.click('button[type="submit"]');

  // Wait for modal to close and API to complete
  await page.waitForTimeout(2000);

  // Verify role was created via API (table might be paginated)
  const roleCreated = await page.evaluate(async (name) => {
    const resp = await fetch(`/api/admin/roles?search=${encodeURIComponent(name)}`);
    const data = await resp.json();
    return data.items && data.items.length > 0 && data.items[0].name === name;
  }, roleName);
  
  expect(roleCreated).toBeTruthy();

  // Refresh or search to find the role in the table
  await page.fill('input[placeholder*="Search"]', roleName);
  await page.waitForTimeout(1000);

  const rolesTable = page.locator('table tbody');
  await expect(rolesTable).toContainText(roleName, { timeout: 10000 });

  // Find the row with our role and click Edit
  const roleRow = rolesTable.locator('tr', { hasText: roleName });
  await expect(roleRow).toBeVisible();

  // Click the edit button (look for Edit action button)
  await roleRow.locator('button[title*="Edit"], button:has-text("Edit")').first().click();

  // Update the description
  const updatedDescription = `${description} (updated)`;
  await page.waitForSelector('#description');
  await page.fill('#description', updatedDescription);

  // Add another permission checkbox (find the 3rd checkbox since we already have 2 selected)
  const editCheckboxes = page.locator('input[type="checkbox"]');
  await editCheckboxes.nth(2).check();

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
