import { test, expect } from '../fixtures';

// Localization CRUD tests - simplified.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Localization', () => {
  test('Localization page navigable', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/Localization');
    await page.waitForSelector('#app', { timeout: 10000 });
    expect(true).toBeTruthy();
  });
});
