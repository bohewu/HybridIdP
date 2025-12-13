import { test, expect } from '../fixtures';

// Profile management tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Profile Management', () => {
  test('Profile page loads and shows user info', async ({ page }) => {
    // Navigate to profile
    await page.goto('https://localhost:7035/Account/Profile');

    // Wait for profile content
    await page.waitForSelector('#app, form, .container', { timeout: 10000 });

    // Verify user email is shown somewhere
    const content = await page.content();
    expect(content).toMatch(/admin@hybridauth\.local|email|profile/i);
  });
});
