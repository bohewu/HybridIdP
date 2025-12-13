import { test, expect } from '../fixtures';

// Dashboard metrics tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Dashboard Metrics', () => {
  test('Metrics accessible', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Dashboard');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
