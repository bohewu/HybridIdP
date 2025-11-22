import { test, expect, Page } from '@playwright/test';
import adminHelpers from '../helpers/admin';

async function waitForCountIncrease(page: Page, property: string, previous: number, minDelta = 1, timeout = 5000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const stats = await adminHelpers.getDashboardStats(page);
    const current = stats[property];
    if (typeof current === 'number' && current >= previous + minDelta) {
      return { stats, current };
    }
    await page.waitForTimeout(250);
  }
  throw new Error(`Timeout waiting for ${property} to increase by >= ${minDelta}`);
}

// Dashboard metrics scenarios focus on relative changes rather than absolute counts to remain stable
// under parallel test execution.

test.describe('Dashboard Metrics', () => {
  test('totalUsers increases after creating a new user', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);

    const initial = await adminHelpers.getDashboardStats(page);
    expect(initial).toBeDefined();
    expect(typeof initial.totalUsers).toBe('number');

    const ts = Date.now();
    const role = await adminHelpers.createRole(page, `e2e-dash-role-${ts}`, []);
    const email = `e2e-dash-user-${ts}@hybridauth.local`;
    const created = await adminHelpers.createUserWithRole(page, email, `E2E!${ts}a`, [role.id]);
    expect(created.id).toBeTruthy();

    const { stats: afterStats } = await waitForCountIncrease(page, 'totalUsers', initial.totalUsers, 1, 7000);
    expect(afterStats.totalUsers).toBeGreaterThanOrEqual(initial.totalUsers + 1);

    // Cleanup (does not assert decrease to avoid race conditions with other tests)
    await adminHelpers.deleteUser(page, created.id);
    await adminHelpers.deleteRole(page, role.id);
  });

  test('totalScopes increases after creating a new scope', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);

    const initial = await adminHelpers.getDashboardStats(page);
    expect(initial).toBeDefined();
    expect(typeof initial.totalScopes).toBe('number');

    const ts = Date.now();
    const scopeName = `e2e-dash-scope-${ts}`;
    const scope = await adminHelpers.createScope(page, scopeName);
    expect(scope.id || scope.name).toBeTruthy();

    const { stats: afterStats } = await waitForCountIncrease(page, 'totalScopes', initial.totalScopes, 1, 7000);
    expect(afterStats.totalScopes).toBeGreaterThanOrEqual(initial.totalScopes + 1);

    // Cleanup
    await adminHelpers.deleteScope(page, scopeName);
  });
});
