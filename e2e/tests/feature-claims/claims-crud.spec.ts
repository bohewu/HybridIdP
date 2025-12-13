import { test, expect } from '../fixtures';

// Claims CRUD tests - simplified.
// Note: ClaimsApi not yet implemented in api-client.ts.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Claims CRUD', () => {
  test('Placeholder - Claims tests', async ({ api }) => {
    // Note: ClaimsApi not yet implemented
    // Just verify fixture works
    expect(api.users).toBeTruthy();
  });
});
