import { test, expect } from '../fixtures';

// Negative tests for Users admin API - validation errors.
// Pure API tests using hybrid pattern.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users error cases', () => {
  test('Assign role fails for non-existent role ID', async ({ api }) => {
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
});
