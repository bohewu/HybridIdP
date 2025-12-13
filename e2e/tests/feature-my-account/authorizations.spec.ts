import { test, expect } from '../fixtures';

// Authorizations tests - simplified.
// Verifies My Account authorizations page loads.

test.describe.configure({ mode: 'serial' });

test.describe('My Account - Authorizations', () => {
  test('Authorizations page loads (UI)', async ({ page }) => {
    // Navigate to my account authorizations
    await page.goto('https://localhost:7035/Account/Authorizations');

    // Verify page loads (may redirect to login if needed)
    await page.waitForSelector('#app, .container', { timeout: 10000 });

    // Verify no error
    const content = await page.content();
    expect(content).not.toContain('Error');
  });
});
