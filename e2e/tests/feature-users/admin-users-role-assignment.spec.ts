import { test, expect } from '../fixtures';

// User Role Assignment API tests using hybrid pattern.
// Pure API tests - no UI needed for these.

test.describe('Admin - User Role Assignment API', () => {
  test('Should assign roles using role IDs endpoint', async ({ api }) => {
    const timestamp = Date.now();

    // Arrange - Create roles and user
    const role1 = await api.roles.create(`e2e-role1-${timestamp}`, 'Role 1', ['users.read']);
    const role2 = await api.roles.create(`e2e-role2-${timestamp}`, 'Role 2', ['clients.read']);

    const user = await api.users.create({
      email: `e2e-user-${timestamp}@hybridauth.local`,
      userName: `e2e-user-${timestamp}@hybridauth.local`,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    });

    // Act - Assign roles using ID-based endpoint
    await api.users.assignRoles(user.id, [role1.id, role2.id]);

    // Assert - Verify via API
    const updatedUser = await api.users.getById(user.id);
    expect(updatedUser.roles).toBeDefined();

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role1.id);
    await api.roles.deleteRole(role2.id);
  });

  test('Should allow switching roles', async ({ api }) => {
    const timestamp = Date.now();

    // Arrange
    const role1 = await api.roles.create(`e2e-switch1-${timestamp}`, 'Role 1', ['users.read']);
    const role2 = await api.roles.create(`e2e-switch2-${timestamp}`, 'Role 2', ['clients.read']);

    const user = await api.users.create({
      email: `e2e-switch-${timestamp}@hybridauth.local`,
      userName: `e2e-switch-${timestamp}@hybridauth.local`,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    });

    // First assign role1
    await api.users.assignRoles(user.id, [role1.id]);

    // Then replace with role2
    await api.users.assignRoles(user.id, [role2.id]);

    // Assert - Should have only role2
    const updatedUser = await api.users.getById(user.id);
    expect(updatedUser.roles).toBeDefined();

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role1.id);
    await api.roles.deleteRole(role2.id);
  });

  test('Should succeed with empty role IDs array (removes all roles)', async ({ api }) => {
    const timestamp = Date.now();

    // Arrange
    const role = await api.roles.create(`e2e-empty-${timestamp}`, 'Role', ['users.read']);

    const user = await api.users.create({
      email: `e2e-empty-${timestamp}@hybridauth.local`,
      userName: `e2e-empty-${timestamp}@hybridauth.local`,
      firstName: 'E2E',
      lastName: 'Empty',
      password: `E2E!${timestamp}a`
    });

    // Assign role first
    await api.users.assignRoles(user.id, [role.id]);

    // Then clear all roles
    await api.users.assignRoles(user.id, []);

    // Assert - Should have no roles
    const updatedUser = await api.users.getById(user.id);
    expect(updatedUser.roles || []).toHaveLength(0);

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role.id);
  });

  test('Should handle duplicate role IDs gracefully', async ({ api }) => {
    const timestamp = Date.now();

    // Arrange
    const role = await api.roles.create(`e2e-dup-${timestamp}`, 'Role', ['users.read']);

    const user = await api.users.create({
      email: `e2e-dup-${timestamp}@hybridauth.local`,
      userName: `e2e-dup-${timestamp}@hybridauth.local`,
      firstName: 'E2E',
      lastName: 'Duplicate',
      password: `E2E!${timestamp}a`
    });

    // Act - Assign same role twice (should be deduplicated)
    await api.users.assignRoles(user.id, [role.id, role.id]);

    // Assert - Should have only one role
    const updatedUser = await api.users.getById(user.id);
    expect(updatedUser.roles).toBeDefined();

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.roles.deleteRole(role.id);
  });
});
