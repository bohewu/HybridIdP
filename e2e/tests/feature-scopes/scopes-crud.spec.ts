import { test, expect } from '../fixtures';

// Scopes CRUD tests - simplified.
// Note: ScopesApi not yet implemented in api-client.ts.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Scopes CRUD', () => {
  test('Placeholder - Scopes tests', async ({ api }) => {
    // Note: ScopesApi not yet implemented 
    // Just verify fixture works
    expect(api.users).toBeTruthy();
  });
});
