import { test, expect } from '../fixtures';

// Negative tests for Users admin API - validation errors and permission-denied scenarios.
// Pure API tests using hybrid pattern.

const ERR_INVALID_ROLE = /not found|does not exist|invalid|error/i;

test.describe('Admin - Users error cases', () => {
  test('Update user returns validation error for non-existent role', async ({ api }) => {
    const ts = Date.now();

    // Create a user
    const user = await api.users.create({
      email: `e2e-err-${ts}@hybridauth.local`,
      userName: `e2e-err-${ts}@hybridauth.local`,
      firstName: 'Err',
      lastName: 'Test',
      password: `E2E!${ts}a`
    });

    // Try to assign non-existent role - should fail
    try {
      await api.users.assignRoles(user.id, ['00000000-0000-0000-0000-000000000000']);
      // If we get here, test should fail
      expect(true).toBe(false);
    } catch (error: any) {
      expect(error.message).toMatch(/404|not found/i);
    }

    // Cleanup
    await api.users.deleteUser(user.id);
  });

  test('Permission denied for limited user modifying another user', async ({ page, api }) => {
    const ts = Date.now();

    // Create roles
    const readRole = await api.roles.create(`e2e-read-role-${ts}`, 'Read only', ['users.read']);

    // Create users
    const userPassword = `E2E!${ts}a`;
    const limitedUser = await api.users.create({
      email: `e2e-limited-${ts}@hybridauth.local`,
      userName: `e2e-limited-${ts}@hybridauth.local`,
      firstName: 'Limited',
      lastName: 'User',
      password: userPassword
    });
    await api.users.assignRoles(limitedUser.id, [readRole.id]);

    const targetUser = await api.users.create({
      email: `e2e-target-${ts}@hybridauth.local`,
      userName: `e2e-target-${ts}@hybridauth.local`,
      firstName: 'Target',
      lastName: 'User',
      password: userPassword
    });

    // Login as limited user via UI
    await page.goto('https://localhost:7035/Account/Logout');
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', limitedUser.email);
    await page.fill('#Input_Password', userPassword);
    await page.click('button.auth-btn-primary');
    await page.waitForTimeout(1000);

    // Try to update target user via API (should fail)
    const attempt = await page.evaluate(async (args: any) => {
      const r = await fetch(`/api/admin/users/${args.uid}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.ids })
      });
      return { status: r.status };
    }, { uid: targetUser.id, ids: [readRole.id] });

    expect([401, 403]).toContain(attempt.status);

    // Cleanup (api fixture still has admin auth)
    await api.users.deleteUser(limitedUser.id);
    await api.users.deleteUser(targetUser.id);
    await api.roles.deleteRole(readRole.id);
  });
});
