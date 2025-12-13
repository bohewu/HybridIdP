import { test, expect } from '../fixtures';

// Audit log viewer tests - simple UI flow test.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Audit Log', () => {
  test('Audit log page loads (UI)', async ({ page }) => {
    // Navigate to admin audit page
    await page.goto('https://localhost:7035/Admin/Audit');
    await page.waitForURL(/\/Admin\/Audit/);

    // Wait for Vue app to load
    await page.waitForSelector('#app', { timeout: 10000 });

    // Give time for content to render
    await page.waitForTimeout(1000);

    // Verify page is accessible (not access denied)
    const content = await page.content();
    expect(content).not.toContain('Access Denied');
  });
});
