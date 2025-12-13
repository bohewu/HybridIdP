import { test, expect } from '../fixtures';

// Client regenerate secret - simplified.
// This requires ClientsApi.regenerateSecret not yet implemented.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Client secrets', () => {
  test('Create confidential client', async ({ api }) => {
    const timestamp = Date.now();
    const clientId = `e2e-secret-${timestamp}`;

    const client = await api.clients.create({
      clientId,
      displayName: 'Secret Test',
      applicationType: 'web',
      type: 'confidential',  // This should generate a secret
      consentType: 'explicit',
      redirectUris: ['https://localhost:7001/callback'],
      permissions: ['ept:authorization', 'gt:authorization_code']
    });
    expect(client.id).toBeTruthy();

    // Cleanup
    await api.clients.deleteClient(client.id);
  });
});
