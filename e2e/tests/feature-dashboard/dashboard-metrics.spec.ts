import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe.configure({ mode: 'serial' });

test.describe('Dashboard Metrics', () => {
  test('Dashboard loads and displays key metrics', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Dashboard');

    // Wait for the main container
    await page.waitForSelector('.dashboard-container, .grid', { timeout: 10000 });

    // Check for metric cards by text (adapt to your actual UI)
    // Common metrics: Users, Clients, Roles, Failed Logins
    await expect(page.locator('text=Total Users')).toBeVisible();
    await expect(page.locator('text=Total Clients')).toBeVisible();
    await expect(page.locator('text=Total Roles')).toBeVisible();

    // Check for values (should be numbers)
    // Assuming structure like <div>Total Users</div><div class="text-2xl">123</div>
    const userCount = page.locator('text=Total Users').locator('xpath=..').locator('.text-2xl, .text-3xl, strong');
    await expect(userCount).toBeVisible();
    const countText = await userCount.innerText();
    expect(parseInt(countText)).toBeGreaterThanOrEqual(1); // At least admin exists

    // Check for Charts (Canvas or SVG)
    await expect(page.locator('canvas, svg').first()).toBeVisible();

    // Check for Recent Activity table/list
    await expect(page.locator('text=Recent Activity')).toBeVisible();
    await expect(page.locator('table, ul[role="list"]')).toBeVisible();
  });
});
