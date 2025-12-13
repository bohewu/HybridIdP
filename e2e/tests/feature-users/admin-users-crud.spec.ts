import { test, expect } from '../fixtures';

// Users CRUD tests using hybrid pattern - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users CRUD', () => {
  test('Create user via API', async ({ api }) => {
    const timestamp = Date.now();
    const email = `e2e-user-${timestamp}@hybridauth.local`;

    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    });
    expect(user.id).toBeTruthy();
    expect(user.email).toBe(email);

    // Cleanup
    await api.users.deleteUser(user.id);
  });
});
