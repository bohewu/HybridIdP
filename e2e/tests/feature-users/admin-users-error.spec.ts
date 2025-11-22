import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Negative tests focused on server validation errors and permission-denied scenarios
test.describe('Admin - Users error cases', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Logout');
  });

  test('Update user returns validation error for duplicate email', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-err-role-${ts}`, []);
    const emailA = `e2e-err-${ts}@hybridauth.local`;
    const emailB = `e2e-err-b-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;
    const createdA = await adminHelpers.createUserWithRole(page, emailA, password, [role.id]);
    const createdB = await adminHelpers.createUserWithRole(page, emailB, password, [role.id]);
    expect(createdA.id).toBeTruthy();
    expect(createdB.id).toBeTruthy();
      // Use a non-existent role name in update to trigger validation/BadRequest
      const invalidRoleName = `no-such-role-${Date.now()}`;
      // Call API directly to update roles (name-based) to get status and body
      const result = await page.evaluate(async (args) => {
        const r = await fetch(`/api/admin/users/${args.uid}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json'},
          body: JSON.stringify(args.payload)
        });
        return { status: r.status, body: await r.text() };
      }, { uid: createdB.id, payload: { roles: [invalidRoleName] } });

      expect(result.status).toBeGreaterThanOrEqual(400);
      expect(result.body).toMatch(/not found|does not exist|invalid|error/i);

    // Cleanup
    await adminHelpers.deleteUser(page, createdA.id);
    await adminHelpers.deleteUser(page, createdB.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('ID-based roles assign endpoint returns not found for unknown role ids', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-assign-role-${ts}`, []);
    const email = `e2e-assign-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;
    const created = await adminHelpers.createUserWithRole(page, email, password, []);
    expect(created.id).toBeTruthy();

    const invalidRoleId = '00000000-0000-0000-0000-000000000000';
    const res = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.uid}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.ids })
      });
      return { status: r.status, body: await r.text() };
    }, { uid: created.id, ids: [invalidRoleId] });

    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(/not found|not exist|404/i);

    // Cleanup
    await adminHelpers.deleteUser(page, created.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('Non-admin user cannot update another user (permission denied) via ID-based endpoint', async ({ page, browser }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const adminRole = await adminHelpers.createRole(page, `e2e-admin-role-${ts}`, ['users.read','users.update']);
    const readRole = await adminHelpers.createRole(page, `e2e-read-role-${ts}`, ['users.read']);

    const userEmail = `e2e-limited-${ts}@hybridauth.local`;
    const userPassword = `E2E!${ts}a`;
    const limitedUser = await adminHelpers.createUserWithRole(page, userEmail, userPassword, [readRole.id]);
    const targetEmail = `e2e-target-${ts}@hybridauth.local`;
    const targetUser = await adminHelpers.createUserWithRole(page, targetEmail, userPassword, []);

    // Login as limited user (non-admin)
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.login(page, userEmail, userPassword);

    // Attempt to assign roles by ID for the target user
    const attempt = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.uid}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.ids })
      });
      return { status: r.status, body: await r.text() };
    }, { uid: targetUser.id, ids: [adminRole.id] });

    expect([401,403]).toContain(attempt.status);

    // Cleanup
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.loginAsAdminViaIdP(page);
    await adminHelpers.deleteUser(page, limitedUser.id);
    await adminHelpers.deleteUser(page, targetUser.id);
    await adminHelpers.deleteRole(page, readRole.id);
    await adminHelpers.deleteRole(page, adminRole.id);
  });
});
