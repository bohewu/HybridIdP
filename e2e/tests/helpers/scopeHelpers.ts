import { Page } from '@playwright/test';

/**
 * Set required scopes for a client by client GUID
 * @param page - Playwright page instance (must be logged in as admin)
 * @param clientGuid - Client GUID (not clientId string)
 * @param scopeNames - Array of scope names to mark as required (e.g., ['openid', 'profile'])
 * @returns Promise<boolean> - true if successful
 */
export async function setClientRequiredScopes(
  page: Page,
  clientGuid: string,
  scopeNames: string[]
): Promise<boolean> {
  // Retry on transient errors (page evaluation may fail if session temporarily closed)
  const maxAttempts = 3;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      return await page.evaluate(
        async ({ guid, scopes }) => {
          const response = await fetch(`/api/admin/clients/${guid}/required-scopes`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ scopes }),
          });
          return response.ok;
        },
        { guid: clientGuid, scopes: scopeNames }
      );
    } catch (err) {
      if (attempt === maxAttempts) throw err;
      // small backoff
      await new Promise((r) => setTimeout(r, 250 * attempt));
    }
  }
  return false;
}

/**
 * Get client GUID by clientId string
 * @param page - Playwright page instance (must be logged in as admin)
 * @param clientId - Client identifier string (e.g., 'testclient-public')
 * @returns Promise<string | null> - Client GUID or null if not found
 */
export async function getClientGuidByClientId(
  page: Page,
  clientId: string
): Promise<string | null> {
  return await page.evaluate(async (id) => {
    const response = await fetch(`/api/admin/clients?search=${encodeURIComponent(id)}&take=100`);
    if (!response.ok) return null;
    const json = await response.json();
    const items = Array.isArray(json) ? json : json.items || [];
    const client = items.find((c: any) => c.clientId === id);
    return client?.id || null;
  }, clientId);
}

/**
 * Extract access token from TestClient by inspecting cookies/session
 * Note: This is a simplified version - actual implementation may need to inspect
 * session cookies or use TestClient's token endpoint response
 * @param page - Playwright page instance on TestClient domain
 * @returns Promise<string | null> - Access token or null
 */
export async function extractAccessTokenFromTestClient(
  page: Page
): Promise<string | null> {
  // Navigate to TestClient profile page which displays the access token
  await page.goto('https://localhost:7001/Account/Profile', { waitUntil: 'networkidle' });
  
  // Try to extract access token from the profile page
  // The access token is displayed in a textarea/input element, not in the claims table
  const accessToken = await page.evaluate(() => {
    // Look for textarea or input that contains the token (typically under "Access Token" heading)
    const textareas = document.querySelectorAll('textarea, input[type="text"]');
    for (const element of textareas) {
      const value = (element as HTMLInputElement | HTMLTextAreaElement).value;
      // JWT tokens start with 'eyJ'
      if (value && value.startsWith('eyJ')) {
        return value.trim();
      }
    }
    return null;
  });
  
  return accessToken;
}

/**
 * Create a test client without openid scope for testing 403 scenarios
 * @param page - Playwright page instance (must be logged in as admin)
 * @param clientId - Client identifier (default: 'testclient-no-openid')
 * @returns Promise<{id: string, clientId: string}> - Created client info
 */
export async function createTestClientWithoutOpenId(
  page: Page,
  clientId: string = 'testclient-no-openid'
): Promise<{ id: string; clientId: string }> {
  // First, delete if exists
  const existing = await getClientGuidByClientId(page, clientId);
  if (existing) {
    await page.evaluate(async (id) => {
      await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' });
    }, existing);
  }

  // Create new client without openid scope
  const created = await page.evaluate(
    async (id) => {
      const payload = {
        clientId: id,
        clientSecret: null,
        displayName: 'Test Client Without OpenID',
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        redirectUris: ['https://localhost:7002/signin-oidc'],
        postLogoutRedirectUris: ['https://localhost:7002/signout-callback-oidc'],
        // Note: openid is NOT in this list
        permissions: [
          'ept:authorization',
          'ept:token',
          'ept:logout',
          'gt:authorization_code',
          'gt:refresh_token',
          'response_type:code',
          'scp:profile',
          'scp:email',
          'scp:api:company:read',
        ],
      };
      const response = await fetch('/api/admin/clients', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        throw new Error(`Failed to create client: ${response.status}`);
      }
      return response.json();
    },
    clientId
  );

  return created;
}

/**
 * Delete a test client by clientId string
 * @param page - Playwright page instance (must be logged in as admin)
 * @param clientId - Client identifier string
 */
export async function deleteTestClient(page: Page, clientId: string): Promise<void> {
  const guid = await getClientGuidByClientId(page, clientId);
  if (guid) {
    await page.evaluate(async (id) => {
      await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' });
    }, guid);
  }
}

/**
 * Get required scopes for a client
 * @param page - Playwright page instance (must be logged in as admin)
 * @param clientGuid - Client GUID
 * @returns Promise<string[]> - Array of required scope names
 */
export async function getClientRequiredScopes(
  page: Page,
  clientGuid: string
): Promise<string[]> {
  return await page.evaluate(async (guid) => {
    const response = await fetch(`/api/admin/clients/${guid}/required-scopes`);
    if (!response.ok) return [];
    const json = await response.json();
    return json.scopes || [];
  }, clientGuid);
}

export default {
  setClientRequiredScopes,
  getClientGuidByClientId,
  extractAccessTokenFromTestClient,
  createTestClientWithoutOpenId,
  deleteTestClient,
  getClientRequiredScopes,
};
