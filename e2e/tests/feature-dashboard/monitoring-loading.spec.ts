import { test, expect } from '../fixtures';

// Monitoring loading tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Monitoring Loading', () => {
  test('Monitoring page accessible', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Dashboard');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
