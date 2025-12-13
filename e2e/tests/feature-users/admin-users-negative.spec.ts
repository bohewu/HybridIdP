import { test, expect } from '../fixtures';

// Negative validation tests for Users admin API.
// Pure API tests - locale-agnostic assertions.

const ERR_DUPLICATE = /duplicate|already exists|already taken|taken/i;
const ERR_INVALID_EMAIL = /invalid|email|format/i;
const ERR_PASSWORD = /password/i;
const ERR_REQUIRED = /required/i;

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users negative validation', () => {
  test('Duplicate email shows validation error', async ({ api }) => {
    const ts = Date.now();
    const email = `e2e-dup-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;

    // First create succeeds
    const user1 = await api.users.create({
      email,
      userName: email,
      firstName: 'Dup',
      lastName: 'Test',
      password
    });
    expect(user1.id).toBeTruthy();

    // Second create with same email should fail
    try {
      await api.users.create({
        email,
        userName: email,
        firstName: 'Dup',
        lastName: 'Test2',
        password
      });
      expect(true).toBe(false); // Should not reach here
    } catch (error: any) {
      expect(error.message).toMatch(/400|duplicate|already/i);
    }

    // Cleanup first user
    await api.users.deleteUser(user1.id);
  });

  test('Invalid email format rejected', async ({ api }) => {
    const ts = Date.now();
    try {
      await api.users.create({
        email: 'not-an-email',
        userName: 'not-an-email',
        firstName: 'Invalid',
        lastName: 'Email',
        password: `E2E!${ts}a`
      });
      expect(true).toBe(false);
    } catch (error: any) {
      expect(error.message).toMatch(/400|invalid|email/i);
    }
  });

  test('Weak password rejected', async ({ api }) => {
    const ts = Date.now();
    try {
      await api.users.create({
        email: `e2e-weak-${ts}@hybridauth.local`,
        userName: `e2e-weak-${ts}@hybridauth.local`,
        firstName: 'Weak',
        lastName: 'Pass',
        password: 'abc' // Too weak
      });
      expect(true).toBe(false);
    } catch (error: any) {
      expect(error.message).toMatch(/400|password/i);
    }
  });

  test('Empty password rejected', async ({ api }) => {
    const ts = Date.now();
    try {
      await api.users.create({
        email: `e2e-empty-${ts}@hybridauth.local`,
        userName: `e2e-empty-${ts}@hybridauth.local`,
        firstName: 'Empty',
        lastName: 'Pass',
        password: ''
      });
      expect(true).toBe(false);
    } catch (error: any) {
      expect(error.message).toMatch(/400|required|password/i);
    }
  });
});
