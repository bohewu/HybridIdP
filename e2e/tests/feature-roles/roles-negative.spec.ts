import { test, expect } from '../fixtures';

// Roles negative validation tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Roles validation', () => {
  test('Duplicate role name returns error', async ({ api }) => {
    const timestamp = Date.now();
    const roleName = `e2e-dup-role-${timestamp}`;

    // Create first role
    const role = await api.roles.create(roleName, 'First role', ['users.read']);
    expect(role.id).toBeTruthy();

    // Try to create duplicate - should fail
    try {
      await api.roles.create(roleName, 'Duplicate role', ['users.read']);
      expect(true).toBe(false); // Should not reach here
    } catch (error: any) {
      expect(error.message).toMatch(/400|duplicate|already|exists/i);
    }

    // Cleanup
    await api.roles.deleteRole(role.id);
  });
});
