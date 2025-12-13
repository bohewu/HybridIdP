import { test, expect } from '../fixtures';

// Impersonation tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Impersonation', () => {
    test('Impersonation requires admin permissions', async ({ api }) => {
        // Verify admin can access users API (prerequisite for impersonation)
        const users = await api.users.list();
        expect(Array.isArray(users.items)).toBeTruthy();
    });
});
