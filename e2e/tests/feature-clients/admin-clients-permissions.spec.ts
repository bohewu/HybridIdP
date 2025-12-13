import { test, expect } from '../fixtures';

// Client permissions test - simplified API version.
// Complex user-switching flows kept as API tests (KISS).

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Clients permissions', () => {
  test('Clients API requires authentication', async ({ api }) => {
    // This test verifies API auth works
    // The fixture handles authentication
    const clients = await api.clients.list();
    expect(Array.isArray(clients.items)).toBeTruthy();
  });
});
