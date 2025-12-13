import { test, expect } from '../fixtures';

// User Role Assignment API tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - User Role Assignment API', () => {
  test('Create role and user', async ({ api }) => {
    const timestamp = Date.now();

    // Create role
    const role = await api.roles.create(`e2e-role-${timestamp}`, 'Test role', ['users.read']);
    expect(role.id).toBeTruthy();

    // Create user
    const user = await api.users.create({
      email: `e2e-role-user-${timestamp}@hybridauth.local`,
      userName: `e2e-role-user-${timestamp}@hybridauth.local`,
      firstName: 'Role',
      lastName: 'Test',
      password: `E2E!${timestamp}a`
    });
    expect(user.id).toBeTruthy();

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role.id);
  });
});
