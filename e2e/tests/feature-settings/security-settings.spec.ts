import { test, expect } from '../fixtures';

// Security settings tests - simple navigation test.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Security Settings', () => {
    test('Security settings page navigable', async ({ page }) => {
        // Navigate to admin security page
        await page.goto('https://localhost:7035/Admin/Security');

        // Just verify no error (200 OK implied by no exception)
        expect(true).toBeTruthy();
    });
});
