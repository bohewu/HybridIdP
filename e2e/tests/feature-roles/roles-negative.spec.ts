import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Negative test cases for Roles UI and API.

test('Admin - Role create (duplicate name) should show validation error in UI', async ({ page }) => {
  // Ensure we are logged in
  await adminHelpers.loginAsAdminViaIdP(page);

  // Create an initial role via API for duplication
  const ts = Date.now();
  const roleName = `e2e-dup-role-${ts}`;
  const created = await adminHelpers.createRole(page, roleName, ['clients.read']);
  try {
    // Open Roles page
    await page.goto('https://localhost:7035/Admin/Roles');
    await page.waitForURL(/\/Admin\/Roles/);
    await page.waitForSelector('button:has-text("Create Role")', { timeout: 10000 });

    // Click Create Role
    await page.click('button:has-text("Create Role")');
    await page.waitForSelector('#name');

    // Fill the same role name as already created
    await page.fill('#name', roleName);
    await page.fill('#description', `Duplicate role ${ts}`);

    // Wait for permissions to load then submit
    await page.waitForSelector('input[type="checkbox"]');
    const checkboxes = page.locator('input[type="checkbox"]');
    await checkboxes.first().check();

    await page.click('button[type="submit"]');

    // Expect an error alert to be shown inside the CreateRole form
    const errorSelector = 'form div.bg-red-50';
    await page.waitForSelector(errorSelector, { timeout: 5000 });
    const text = await page.textContent(errorSelector);

    expect(text).toContain('already exists');
  } finally {
    // Cleanup: delete created role via API in case test fails midway
    if (created && created.id) await adminHelpers.deleteRole(page, created.id);
  }
});


test('Admin - Role create via API with invalid permission should return 400', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  const roleName = `e2e-invalid-perm-${Date.now()}`;
  const payload = { name: roleName, description: `Invalid permissions test`, permissions: ['not.a.real.permission'] };

  // Evaluate a direct fetch so we can inspect the response status and body
  const result = await page.evaluate(async (p) => {
    const r = await fetch('/api/admin/roles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });

    const text = await r.text().catch(() => null);
    return { ok: r.ok, status: r.status, body: text };
  }, payload);

  expect(result.ok).toBeFalsy();
  // Service returns 400 for invalid permissions
  expect(result.status).toBe(400);
  expect(result.body).toContain('Invalid permissions');
});
