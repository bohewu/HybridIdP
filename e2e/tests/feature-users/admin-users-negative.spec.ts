import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Negative validation tests for Users admin UI/API.
// Locale-agnostic assertions use regex patterns.
// Falls back to direct API when UI elements differ/missing.

const ERR_DUPLICATE = /duplicate|already exists|already taken|taken/i;
const ERR_INVALID_EMAIL = /invalid|email|format/i;
const ERR_PASSWORD_COMPLEXITY = /password.*(complexity|uppercase|lowercase|digit|special)/i;
const ERR_REQUIRED = /required/i;

async function tryCreateViaApi(page: import('@playwright/test').Page, payload: any) {
  return await page.evaluate(async (p: any) => {
    const r = await fetch('/api/admin/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(p)
    });
    return { status: r.status, body: await r.text() };
  }, payload);
}

function genBase(ts: number) {
  return {
    email: `e2e-neg-${ts}@hybridauth.local`,
    userName: `e2e-neg-${ts}@hybridauth.local`,
    password: `E2E!${ts}a`,
    firstName: 'Neg',
    lastName: 'Test',
    roles: [] as string[]
  };
}

test.describe('Admin - Users negative validation', () => {
  test.beforeEach(async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
  });

  test('Duplicate email shows validation error', async ({ page }) => {
    const ts = Date.now();
    const base = genBase(ts);
    // First create succeeds
    let res1 = await tryCreateViaApi(page, base);
    expect(res1.status).toBeLessThan(300);
    // Second create with same email should fail
    let res2 = await tryCreateViaApi(page, base);
    expect(res2.status).toBeGreaterThanOrEqual(400);
    expect(res2.body).toMatch(ERR_DUPLICATE);
  });

  test('Invalid email format rejected', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    payload.email = 'not-an-email';
    payload.userName = 'not-an-email';
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(ERR_INVALID_EMAIL);
  });

  test('Weak password rejected (complexity)', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    payload.password = 'abc'; // too weak
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(ERR_PASSWORD_COMPLEXITY);
  });

  test('Missing required fields rejected', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    delete (payload as any).email;
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(ERR_REQUIRED);
  });

  test('Whitespace email rejected', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    payload.email = '   ';
    payload.userName = '   ';
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(ERR_REQUIRED);
  });

  test('Empty password rejected', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    payload.password = '';
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    expect(res.body).toMatch(ERR_REQUIRED);
  });

  test('Excessively long email rejected', async ({ page }) => {
    const ts = Date.now();
    const payload = genBase(ts);
    payload.email = 'x'.repeat(300) + '@test.com';
    payload.userName = payload.email;
    const res = await tryCreateViaApi(page, payload);
    expect(res.status).toBeGreaterThanOrEqual(400);
    // Could be length or format based; just ensure failure
    expect(res.body.length).toBeGreaterThan(0);
  });

  test('Duplicate userName with different email rejected', async ({ page }) => {
    const ts = Date.now();
    const payload1 = genBase(ts);
    const payload2 = genBase(ts + 1);
    // payload2 uses same userName as payload1 but different email
    payload2.userName = payload1.userName;
    let r1 = await tryCreateViaApi(page, payload1);
    expect(r1.status).toBeLessThan(300);
    let r2 = await tryCreateViaApi(page, payload2);
    expect(r2.status).toBeGreaterThanOrEqual(400);
    expect(r2.body).toMatch(/user(name)?|duplicate|exists/i);
  });
});
