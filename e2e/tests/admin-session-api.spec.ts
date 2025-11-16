import { test, expect, Page } from '@playwright/test';

// Direct API validation of session management endpoints (admin perspective)
// This complements existing UI-driven session tests.
// Skips revocation steps gracefully if no sessions are present.

const IDP_BASE = 'https://localhost:7035';
const ADMIN_EMAIL = 'admin@hybridauth.local';
const ADMIN_PASSWORD = 'Admin@123';

async function adminLogin(page: Page) {
  // Navigate to login (Identity area path may vary; try primary route)
  await page.goto(`${IDP_BASE}/Identity/Account/Login`).catch(() => {});
  if (!page.url().includes('/Login')) {
    // Fallback
    await page.goto(`${IDP_BASE}/Account/Login`);
  }
  await page.fill('input[name="Input.Email"]', ADMIN_EMAIL);
  await page.fill('input[name="Input.Password"]', ADMIN_PASSWORD);
  await page.click('button[type="submit"]');
  await page.waitForLoadState('networkidle');
}

test.describe('Admin Session Management API', () => {
  test('List, revoke single, revoke all sessions via API endpoints', async ({ page, request }) => {
    // Ensure authenticated via UI to establish cookies (permissions enforced by cookie auth).
    await adminLogin(page);

    // 1. Fetch users to obtain a target user ID (first page default take=25)
    const usersResponse = await page.request.get(`${IDP_BASE}/api/admin/users?take=1`);
    expect(usersResponse.status()).toBe(200);
    const usersJson = await usersResponse.json();
    // Expect structure { items: [...], total: n } or similar. Be defensive:
    const firstUser = usersJson.items?.[0] || usersJson[0] || usersJson; // fallback
    const userId = firstUser.id || firstUser.userId || firstUser.Id;
    expect(userId).toBeTruthy();

    // 2. List sessions for this user
    const listResp = await page.request.get(`${IDP_BASE}/api/admin/users/${userId}/sessions`);
    expect(listResp.status()).toBe(200);
    const sessions = await listResp.json();
    expect(Array.isArray(sessions)).toBe(true);
    // Each session is expected to include an authorization identifier
    if (sessions.length > 0) {
      expect(sessions[0].authorizationId).toBeTruthy();
      // Client info is best-effort: either clientDisplayName or null is acceptable
      expect(typeof sessions[0]).toBe('object');
      expect(Object.prototype.hasOwnProperty.call(sessions[0], 'clientDisplayName')).toBe(true);
      expect(Object.prototype.hasOwnProperty.call(sessions[0], 'clientId')).toBe(true);
      expect(Object.prototype.hasOwnProperty.call(sessions[0], 'status')).toBe(true);
    }

    // 3. If there is at least one session, revoke the first
    if (sessions.length > 0) {
      const firstAuthId = sessions[0].authorizationId || sessions[0].AuthorizationId || sessions[0].id;
      expect(firstAuthId).toBeTruthy();
      const revokeSingle = await page.request.post(`${IDP_BASE}/api/admin/users/${userId}/sessions/${firstAuthId}/revoke`);
      expect([204,404]).toContain(revokeSingle.status()); // 204 success; 404 if already invalid
    }

    // 4. Revoke all sessions (regardless of prior state)
    const revokeAll = await page.request.post(`${IDP_BASE}/api/admin/users/${userId}/sessions/revoke-all`);
    expect(revokeAll.status()).toBe(200);
    const revokeAllJson = await revokeAll.json().catch(() => ({}));
    // Expect { revoked: number }
    if (typeof revokeAllJson.revoked === 'number') {
      expect(revokeAllJson.revoked).toBeGreaterThanOrEqual(0);
    }

    // 5. Confirm listing now returns zero or fewer sessions than before (best-effort)
    const postList = await page.request.get(`${IDP_BASE}/api/admin/users/${userId}/sessions`);
    expect(postList.status()).toBe(200);
    const postSessions = await postList.json();
    if (Array.isArray(sessions) && Array.isArray(postSessions)) {
      expect(postSessions.length).toBeLessThanOrEqual(sessions.length);
    }
  });
});
