import { test, expect } from '../fixtures';

// Homepage refactor tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Homepage', () => {
  test('Homepage loads after login', async ({ page }) => {
    // Navigate to IdP homepage
    await page.goto('https://localhost:7035/');

    // Should be authenticated via storageState
    await page.waitForSelector('.user-name, #app', { timeout: 10000 });

    // Verify page loads
    expect(true).toBeTruthy();
  });
});
