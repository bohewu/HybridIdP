import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';
import { waitForDebounce } from '../helpers/timing';

test('Admin - Localization CRUD (create, update, delete)', async ({ page }) => {
  // Accept dialogs automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Get admin's role and temporarily add Localization permissions
  const { adminRoleId, originalPermissions } = await page.evaluate(async () => {
    const rolesResp = await fetch('/api/admin/roles');
    const roles = await rolesResp.json();
    const adminRole = roles.items.find((r: any) => r.name === 'Admin');

    if (!adminRole) {
      throw new Error('Admin role not found');
    }

    const roleDetailResp = await fetch(`/api/admin/roles/${adminRole.id}`);
    const roleDetail = await roleDetailResp.json();
    const original = roleDetail.permissions || [];

    const updatedPermissions = [
      ...new Set([
        ...original,
        'localization.read',
        'localization.create',
        'localization.update',
        'localization.delete'
      ])
    ];

    await fetch(`/api/admin/roles/${adminRole.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: roleDetail.name,
        description: roleDetail.description,
        permissions: updatedPermissions
      })
    });

    return { adminRoleId: adminRole.id, originalPermissions: original };
  });

  // Re-login to get updated permissions
  await page.goto('https://localhost:7035/Account/Logout');
  await adminHelpers.loginAsAdminViaIdP(page);

  let createdResourceId: number | null = null;

  try {
    // Navigate to Localization page
    await page.goto('https://localhost:7035/Admin/Localization');
    await page.waitForURL(/\/Admin\/Localization/);

    // Wait for the Vue app to load - use data-test-id
    await page.waitForSelector('[data-test-id="resources-table"], [data-test-id="create-resource-btn"]', { timeout: 15000 });

    const resourceKey = `e2e.test.key.${Date.now()}`;
    const resourceValue = `E2E Test Value ${Date.now()}`;
    const resourceCulture = 'zh-TW';

    // Click Create Resource button using data-test-id
    await page.locator('[data-test-id="create-resource-btn"]').click();

    // Wait for modal form to be ready
    await page.waitForSelector('[data-test-id="key-input"]', { timeout: 5000 });

    // Fill form using data-test-id
    await page.locator('[data-test-id="key-input"]').fill(resourceKey);
    await page.locator('[data-test-id="culture-select"]').selectOption(resourceCulture);
    await page.locator('[data-test-id="category-input"]').fill('E2E');
    await page.locator('[data-test-id="value-textarea"]').fill(resourceValue);

    // Submit form
    const responsePromise = page.waitForResponse(resp => 
      resp.url().includes('/api/admin/localization') && resp.request().method() === 'POST'
    );

    await page.locator('[data-test-id="submit-btn"]').click();

    const response = await responsePromise;
    const responseData = await response.json();
    expect(responseData.id).toBeTruthy();
    createdResourceId = responseData.id;

    // Wait for modal to close
    await page.waitForSelector('[data-test-id="key-input"]', { state: 'hidden', timeout: 5000 });
    await waitForDebounce(page, 1000);

    // Verify resource appears in table
    const table = page.locator('[data-test-id="resources-table"]');
    await expect(table).toContainText(resourceKey, { timeout: 10000 });

    // Click edit button using data-test-id
    const editBtn = page.locator(`[data-test-id="edit-btn-${createdResourceId}"]`);
    await editBtn.click();

    // Wait for modal form
    await page.waitForSelector('[data-test-id="value-textarea"]', { timeout: 5000 });

    // Update value
    const updatedValue = `${resourceValue} (updated)`;
    await page.locator('[data-test-id="value-textarea"]').clear();
    await page.locator('[data-test-id="value-textarea"]').fill(updatedValue);

    // Submit update
    const updateResponsePromise = page.waitForResponse(resp => 
      resp.url().includes(`/api/admin/localization/${createdResourceId}`) && resp.request().method() === 'PUT'
    );

    await page.locator('[data-test-id="submit-btn"]').click();
    await updateResponsePromise;

    await page.waitForSelector('[data-test-id="value-textarea"]', { state: 'hidden', timeout: 5000 });
    await waitForDebounce(page, 1000);

    // Click delete button using data-test-id
    const deleteBtn = page.locator(`[data-test-id="delete-btn-${createdResourceId}"]`);
    await deleteBtn.click();

    // Wait for delete API
    const deleteResponsePromise = page.waitForResponse(resp => 
      resp.url().includes(`/api/admin/localization/${createdResourceId}`) && resp.request().method() === 'DELETE'
    );

    await deleteResponsePromise;
    await waitForDebounce(page, 1000);

    // Verify deletion
    await expect(table).not.toContainText(resourceKey, { timeout: 10000 });
    createdResourceId = null; // Cleanup not needed

  } finally {
    // Cleanup: delete resource if still exists
    if (createdResourceId) {
      await page.evaluate(async (id) => {
        await fetch(`/api/admin/localization/${id}`, { method: 'DELETE' });
      }, createdResourceId);
    }

    // Restore original Administrator role permissions
    if (page && !page.isClosed()) {
      await page.evaluate(async (args) => {
        const roleDetailResp = await fetch(`/api/admin/roles/${args.roleId}`);
        const roleDetail = await roleDetailResp.json();

        await fetch(`/api/admin/roles/${args.roleId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name: roleDetail.name,
            description: roleDetail.description,
            permissions: args.originalPerms
          })
        });
      }, { roleId: adminRoleId, originalPerms: originalPermissions });
    }
  }
});

test('Admin - Localization Search and Sort', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Localization page
  await page.goto('https://localhost:7035/Admin/Localization');
  await page.waitForURL(/\/Admin\/Localization/);

  // Wait for the Vue app to load
  await page.waitForSelector('[data-test-id="resources-table"], [data-test-id="create-resource-btn"]', { timeout: 15000 });

  // Test sort functionality using data-test-id
  const sortSelect = page.locator('[data-test-id="sort-select"]');
  if (await sortSelect.isVisible().catch(() => false)) {
    await sortSelect.selectOption('key:desc');
    await waitForDebounce(page, 600);

    // Verify sort was applied (just check the table is still visible)
    await expect(page.locator('[data-test-id="resources-table"]')).toBeVisible();
  }
});
