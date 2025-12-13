import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe('Admin - User Forms', () => {
    test.describe.configure({ mode: 'serial' });

    test('Create new user via API and verify in UI', async ({ page }) => {
        const timestamp = Date.now();
        const username = `user_${timestamp}`;
        const email = `user_${timestamp}@example.com`;

        await adminHelpers.loginAsAdminViaIdP(page);
        await page.goto('https://localhost:7035/Admin/Users');

        // Create via UI
        await page.click('[data-test-id="users-create-btn"]');

        // Wait for modal
        await page.fill('[data-test-id="user-email-input"]', email);
        await page.fill('[data-test-id="user-username-input"]', username);
        await page.fill('[data-test-id="user-firstname-input"]', 'Test');
        await page.fill('[data-test-id="user-lastname-input"]', 'User');

        // Password input (child component)
        await page.fill('[data-test-id="user-password-input"]', 'P@ssword123!');
        await page.fill('[data-test-id="user-confirm-password-input"]', 'P@ssword123!');

        await page.click('[data-test-id="user-save-btn"]');

        // Verify success
        // Search
        await page.fill('[data-test-id="users-search-input"]', username);
        await expect(page.locator('table')).toContainText(username);
    });
});
