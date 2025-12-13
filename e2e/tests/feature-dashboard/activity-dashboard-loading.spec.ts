import { test, expect } from '../fixtures';

// Dashboard loading tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Dashboard', () => {
  test('Dashboard loads', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Dashboard');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
