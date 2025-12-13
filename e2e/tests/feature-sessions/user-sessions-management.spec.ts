import { test, expect } from '../fixtures';

// User Sessions Management tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('User Sessions Management', () => {
  test('Users API accessible', async ({ api }) => {
    // Just verify admin can access users API
    const users = await api.users.list();
    expect(Array.isArray(users.items)).toBeTruthy();
  });
});
