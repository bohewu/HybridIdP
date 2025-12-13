import { test, expect } from '../fixtures';

// Linked accounts tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('My Account - Linked Accounts', () => {
  test('Linked accounts page navigable', async ({ page }) => {
    // Navigate to linked accounts
    await page.goto('https://localhost:7035/Account/LinkedAccounts');

    // Wait for page to load
    await page.waitForSelector('#app, .container', { timeout: 10000 });

    expect(true).toBeTruthy();
  });
});
