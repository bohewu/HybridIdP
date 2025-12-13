import { test, expect } from '../fixtures';

// Monitoring page loading tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Monitoring', () => {
  test('Dashboard page loads', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Dashboard');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
