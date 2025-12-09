import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// E2E tests for user session (authorization) management.
// Requires IdP dev server running on https://localhost:7035.
// Uses admin APIs to create users/roles and then exercises session endpoints:
//  GET    /api/admin/users/{id}/sessions
//  POST   /api/admin/users/{id}/sessions/{authorizationId}/revoke
//  POST   /api/admin/users/{id}/sessions/revoke-all

async function fetchUserByEmail(page: import('@playwright/test').Page, email: string) {
  const user = await page.evaluate(async (mail) => {
    const r = await fetch('/api/admin/users?search=' + encodeURIComponent(mail));
    if (!r.ok) return null;
    const j = await r.json();
    return j.items?.find((x: any) => x.email === mail) || null;
  }, email);
  return user;
}

async function listSessions(page: import('@playwright/test').Page, userId: string) {
  return await page.evaluate(async (uid) => {
    const r = await fetch(`/api/admin/users/${uid}/sessions`);
    if (!r.ok) return { status: r.status, items: [] };
    const j = await r.json();
    // Normalize to support either array responses or { items: [] }
    const items = Array.isArray(j) ? j : (j.items || []);
    return { status: r.status, items };
  }, userId);
}

async function revokeSession(page: import('@playwright/test').Page, userId: string, authorizationId: string) {
  return await page.evaluate(async (args) => {
    const r = await fetch(`/api/admin/users/${args.uid}/sessions/${args.auth}/revoke`, { method: 'POST' });
    return r.status;
  }, { uid: userId, auth: authorizationId });
}

async function revokeAll(page: import('@playwright/test').Page, userId: string) {
  return await page.evaluate(async (uid) => {
    const r = await fetch(`/api/admin/users/${uid}/sessions/revoke-all`, { method: 'POST' });
    const body = await r.text();
    return { status: r.status, body };
  }, userId);
}

test.describe('User Sessions Management', () => {
  test('List sessions and revoke single session', async ({ page, browser }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-sessions-role-${ts}`, ['users.read','users.update']);
    const email = `e2e-sessions-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;
    const created = await adminHelpers.createUserWithRole(page, email, password, [role.id]);

    // Login as created user in a fresh context to generate a session
    const userContext = await browser.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();
    // Use OIDC login flow via TestClient to ensure authorization is recorded
    await adminHelpers.loginViaTestClient(userPage, email, password);

    // Back to admin page context: list sessions
    const userRecord = await fetchUserByEmail(page, email);
    expect(userRecord?.id).toBeTruthy();
    // Wait for session to appear - sessions may take a short time to be recorded
    let list = await listSessions(page, userRecord.id);
    expect(list.status).toBe(200);
    let attempts = 0;
    while (list.items.length === 0 && attempts < 50) {
      await page.waitForTimeout(200);
      list = await listSessions(page, userRecord.id);
      attempts++;
    }
    expect(list.items.length).toBeGreaterThanOrEqual(1);
    const targetAuth = list.items[0].authorizationId;
    expect(targetAuth).toBeTruthy();

    // Revoke first session
    const revokeStatus = await revokeSession(page, userRecord.id, targetAuth);
    expect([204,200]).toContain(revokeStatus);

    // Wait until revoked
    const revoked = await adminHelpers.waitForSessionRevocation(page, userRecord.id, targetAuth, 10000);
    expect(revoked).toBeTruthy();

    // Cleanup
    await userContext.close().catch(() => {});
    await adminHelpers.deleteUser(page, userRecord.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('Revoke all sessions removes all authorizations', async ({ page, browser }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-sessions-role-all-${ts}`, ['users.read','users.update']);
    const email = `e2e-sessions-all-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;
    const created = await adminHelpers.createUserWithRole(page, email, password, [role.id]);

    // Create multiple sessions (3 contexts)
    const contexts = await adminHelpers.createMultipleSessions(page, email, password, 3);

    const userRecord = await fetchUserByEmail(page, email);
    let beforeList = await listSessions(page, userRecord.id);
    let attempts = 0;
    while (beforeList.items.length === 0 && attempts < 50) {
      await page.waitForTimeout(200);
      beforeList = await listSessions(page, userRecord.id);
      attempts++;
    }
    expect(beforeList.items.length).toBeGreaterThanOrEqual(1);

    const allResult = await revokeAll(page, userRecord.id);
    expect(allResult.status).toBe(200);
    expect(allResult.body).toMatch(/revoked/);

    // Poll until revocation propagates
    let afterList;
    let stillValid;
    let attemptsRevoke = 0;
    do {
      await page.waitForTimeout(500);
      afterList = await listSessions(page, userRecord.id);
      stillValid = afterList.items.find((s: any) => beforeList.items.some((b: any) => b.authorizationId === s.authorizationId && (s.status || '').toLowerCase() === 'valid'));
      attemptsRevoke++;
    } while (stillValid && attemptsRevoke < 20);

    expect(stillValid).toBeFalsy();

    // Cleanup contexts
    for (const ctx of contexts) { await ctx.context.close(); }
    await adminHelpers.deleteUser(page, userRecord.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('Non-admin user cannot revoke sessions (permission denied)', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const readRole = await adminHelpers.createRole(page, `e2e-sessions-read-${ts}`, ['users.read']);
    const email = `e2e-sessions-read-${ts}@hybridauth.local`;
    const password = `E2E!${ts}a`;
    const created = await adminHelpers.createUserWithRole(page, email, password, [readRole.id]);

    // Login as limited user
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.login(page, email, password);

    // Attempt revoke-all (should be 403/401)
    const result = await page.evaluate(async (uid) => {
      const r = await fetch(`/api/admin/users/${uid}/sessions/revoke-all`, { method: 'POST' });
      return r.status;
    }, created.id);
    expect([401,403]).toContain(result);

    // Cleanup
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.loginAsAdminViaIdP(page);
    await adminHelpers.deleteUser(page, created.id);
    await adminHelpers.deleteRole(page, readRole.id);
  });

  test('User cannot revoke another user\'s session', async ({ page, browser }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-sessions-owner-${ts}`, ['users.read','users.update']);
    const email1 = `e2e-sessions-owner1-${ts}@hybridauth.local`;
    const email2 = `e2e-sessions-owner2-${ts}@hybridauth.local`;
    const pwd = `E2E!${ts}a`;
    const user1 = await adminHelpers.createUserWithRole(page, email1, pwd, [role.id]);
    const user2 = await adminHelpers.createUserWithRole(page, email2, pwd, [role.id]);

    // Login as user1 context (not admin)
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.login(page, email1, pwd);

    // Admin lists sessions for user2 (need admin context)
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.loginAsAdminViaIdP(page);
    // Ensure user2 has an active session by logging in via TestClient
    const user2Ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const user2Page = await user2Ctx.newPage();
    await adminHelpers.loginViaTestClient(user2Page, email2, pwd);

    const rec2 = await fetchUserByEmail(page, email2);
    let sessions2 = await listSessions(page, rec2.id);
    let sessionAttempts = 0;
    while (sessions2.items.length === 0 && sessionAttempts < 50) {
      await page.waitForTimeout(200);
      sessions2 = await listSessions(page, rec2.id);
      sessionAttempts++;
    }
    const targetAuth = sessions2.items[0]?.authorizationId;
    expect(targetAuth).toBeTruthy();

    // Switch to user1 (limited) and attempt revoke
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.login(page, email1, pwd);
    const status = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/users/${args.uid}/sessions/${args.auth}/revoke`, { method: 'POST' });
      return r.status;
    }, { uid: rec2.id, auth: targetAuth });
    expect([401,403]).toContain(status);

    // Cleanup with admin
    await page.goto('https://localhost:7035/Account/Logout');
    await adminHelpers.loginAsAdminViaIdP(page);
    await adminHelpers.deleteUser(page, user1.id);
    await adminHelpers.deleteUser(page, user2.id);
    await adminHelpers.deleteRole(page, role.id);
  });
});
