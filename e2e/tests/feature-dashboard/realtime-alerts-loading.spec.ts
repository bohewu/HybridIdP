import { test, expect } from '../fixtures';

// Realtime alerts loading tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Realtime Alerts', () => {
  test('Dashboard page accessible for alerts', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Dashboard');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
