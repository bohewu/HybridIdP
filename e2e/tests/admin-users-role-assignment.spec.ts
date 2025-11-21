import { test, expect } from '@playwright/test';
import adminHelpers from './helpers/admin';

test.describe('Admin - User Role Assignment API', () => {
  test('Should assign roles using role IDs endpoint', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const timestamp = Date.now();
    
    // Create two roles
    const role1 = await adminHelpers.createRole(page, `e2e-role1-${timestamp}`, ['users.read']);
    const role2 = await adminHelpers.createRole(page, `e2e-role2-${timestamp}`, ['clients.read']);
    
    // Create a user
    const userEmail = `e2e-user-${timestamp}@hybridauth.local`;
    const userPayload = {
      email: userEmail,
      userName: userEmail,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    };

    const user = await page.evaluate(async (p) => {
      const r = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(p)
      });
      if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
      return r.json();
    }, userPayload);

    // Assign roles using the ID-based endpoint
    const assignResult = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.roleIds })
      });
      if (!r.ok) {
        const body = await r.text();
        throw new Error(`Failed to assign roles: ${r.status} ${body}`);
      }
      return r.json();
    }, { userId: user.id, roleIds: [role1.id, role2.id] });

    // Verify the user has the assigned roles
    expect(assignResult.roles).toHaveLength(2);
    expect(assignResult.roles.map((r: any) => r.name)).toContain(role1.name);
    expect(assignResult.roles.map((r: any) => r.name)).toContain(role2.name);

    // Cleanup
    await adminHelpers.deleteUser(page, user.id);
    await adminHelpers.deleteRole(page, role1.id);
    await adminHelpers.deleteRole(page, role2.id);
  });

  test('Should return error for invalid role ID', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const timestamp = Date.now();
    
    // Create a user
    const userEmail = `e2e-user-invalid-${timestamp}@hybridauth.local`;
    const userPayload = {
      email: userEmail,
      userName: userEmail,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    };

    const user = await page.evaluate(async (p) => {
      const r = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(p)
      });
      if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
      return r.json();
    }, userPayload);

    // Try to assign an invalid role ID
    const invalidRoleId = '00000000-0000-0000-0000-000000000000';
    const assignResult = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: args.roleIds })
      });
      return { status: r.status, body: await r.json() };
    }, { userId: user.id, roleIds: [invalidRoleId] });

    // Verify we get a 404 Not Found error
    expect(assignResult.status).toBe(404);
    expect(assignResult.body.errors).toBeDefined();
    expect(assignResult.body.errors.some((e: string) => e.includes(invalidRoleId))).toBeTruthy();
    expect(assignResult.body.errors.some((e: string) => e.includes('not found'))).toBeTruthy();

    // Cleanup
    await adminHelpers.deleteUser(page, user.id);
  });

  test('Should maintain backward compatibility with name-based endpoint', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const timestamp = Date.now();
    
    // Create a role
    const role = await adminHelpers.createRole(page, `e2e-role-name-${timestamp}`, ['users.read']);
    
    // Create a user
    const userEmail = `e2e-user-name-${timestamp}@hybridauth.local`;
    const userPayload = {
      email: userEmail,
      userName: userEmail,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    };

    const user = await page.evaluate(async (p) => {
      const r = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(p)
      });
      if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
      return r.json();
    }, userPayload);

    // Assign roles using the name-based endpoint (original endpoint)
    const assignResult = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}/roles`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Roles: args.roleNames })
      });
      if (!r.ok) {
        const body = await r.text();
        throw new Error(`Failed to assign roles: ${r.status} ${body}`);
      }
      return r.json();
    }, { userId: user.id, roleNames: [role.name] });

    // Verify the user has the assigned role
    expect(assignResult.roles).toHaveLength(1);
    expect(assignResult.roles[0].name).toBe(role.name);

    // Cleanup
    await adminHelpers.deleteUser(page, user.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('Should allow switching between ID and name-based endpoints', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const timestamp = Date.now();
    
    // Create two roles
    const role1 = await adminHelpers.createRole(page, `e2e-role-switch1-${timestamp}`, ['users.read']);
    const role2 = await adminHelpers.createRole(page, `e2e-role-switch2-${timestamp}`, ['clients.read']);
    
    // Create a user
    const userEmail = `e2e-user-switch-${timestamp}@hybridauth.local`;
    const userPayload = {
      email: userEmail,
      userName: userEmail,
      firstName: 'E2E',
      lastName: 'User',
      password: `E2E!${timestamp}a`
    };

    const user = await page.evaluate(async (p) => {
      const r = await fetch('/api/admin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(p)
      });
      if (!r.ok) throw new Error(`Failed to create user: ${r.status}`);
      return r.json();
    }, userPayload);

    // First assign using ID-based endpoint
    await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}/roles/ids`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ RoleIds: [args.roleId1] })
      });
      if (!r.ok) throw new Error(`Failed to assign roles via ID: ${r.status}`);
    }, { userId: user.id, roleId1: role1.id });

    // Then reassign using name-based endpoint
    const finalResult = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.userId}/roles`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Roles: [args.roleName2] })
      });
      if (!r.ok) throw new Error(`Failed to assign roles via name: ${r.status}`);
      return r.json();
    }, { userId: user.id, roleName2: role2.name });

    // Verify the user now has only role2 (replacement behavior)
    expect(finalResult.roles).toHaveLength(1);
    expect(finalResult.roles[0].name).toBe(role2.name);

    // Cleanup
    await adminHelpers.deleteUser(page, user.id);
    await adminHelpers.deleteRole(page, role1.id);
    await adminHelpers.deleteRole(page, role2.id);
  });
});
