import { test, expect } from '../fixtures';

/**
 * Role Permissions in Claims - basic test.
 */

test.describe.configure({ mode: 'serial' });

test.describe('Role Permissions in User Claims', () => {
  test('Create role with permissions', async ({ api }) => {
    const timestamp = Date.now();

    // Create role with permissions
    const role = await api.roles.create(`e2e-claims-${timestamp}`, 'Claims test', ['users.read']);
    expect(role.id).toBeTruthy();

    // Cleanup
    await api.roles.deleteRole(role.id);
  });
});
