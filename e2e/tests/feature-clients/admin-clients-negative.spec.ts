import { test, expect } from '../fixtures';

// Clients negative validation tests using hybrid pattern.
// Pure API tests for validation.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Clients negative validation', () => {
  test('Duplicate clientId returns error', async ({ api }) => {
    const timestamp = Date.now();
    const clientId = `e2e-dup-${timestamp}`;

    // Create first client
    const client = await api.clients.create({
      clientId,
      displayName: 'Duplicate Test',
      applicationType: 'web',
      type: 'public',
      consentType: 'explicit',
      redirectUris: ['https://localhost:7001/callback'],
      permissions: ['ept:authorization', 'gt:authorization_code']
    });
    expect(client.id).toBeTruthy();

    // Try to create duplicate - should fail
    try {
      await api.clients.create({
        clientId,
        displayName: 'Duplicate Test 2',
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        redirectUris: ['https://localhost:7001/callback'],
        permissions: ['ept:authorization', 'gt:authorization_code']
      });
      expect(true).toBe(false); // Should not reach here
    } catch (error: any) {
      expect(error.message).toMatch(/400|duplicate|already|exists/i);
    }

    // Cleanup
    await api.clients.deleteClient(client.id);
  });

  test('Empty clientId returns validation error', async ({ api }) => {
    try {
      await api.clients.create({
        clientId: '',
        displayName: 'Should Fail',
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        redirectUris: ['https://localhost:7001/callback'],
        permissions: ['ept:authorization']
      });
      expect(true).toBe(false);
    } catch (error: any) {
      expect(error.message).toMatch(/400|required|clientId/i);
    }
  });
});
