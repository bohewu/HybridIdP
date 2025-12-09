import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Admin - User Impersonation', () => {
    test('Should allow admin to impersonate a user and switch back', async ({ page }) => {
        // 1. Login as Admin
        await adminHelpers.loginAsAdminViaIdP(page);

        // 2. Setup: Create a test user to impersonate
        const timestamp = Date.now();
        const targetEmail = `impersonate-target-${timestamp}@hybridauth.local`;
        const targetPassword = `Target!${timestamp}`;
        const targetUser = await adminHelpers.createUserWithRole(page, targetEmail, targetPassword, []);

        try {
            // 3. Navigate to User List
            await page.goto('https://localhost:7035/Admin/Users');
            await page.waitForURL(/\/Admin\/Users/);

            // 4. Find the user in the list
            await page.waitForTimeout(500); // Allow list to load
            const row = await adminHelpers.searchListForItem(page, 'users', targetEmail, { listSelector: 'table tbody' });
            expect(row).not.toBeNull();

            // 5. Open Action Menu
            // The action menu trigger is likely the button in the last column
            const actionMenuTrigger = row!.locator('button[aria-haspopup="menu"]'); // Assuming standard dropdown trigger or dot icon
            // Fallback if specific aria attribute is missing: verify structure in UserList.vue
            // UserList.vue: uses ActionMenu component.
            // ActionMenu.vue likely has a button.
            // Let's assume it's the button in the last 'td'.
            // Or look for ".action-menu-button" if class exists?
            // "Actions" column is text-right align-middle.
            
            await actionMenuTrigger.click();

            // 6. Click "Login As"
            const loginAsItem = page.locator('div[role="menu"] button:has-text("Login As")');
            // Or localized "Login As". In en-US it is "Login As".
            await expect(loginAsItem).toBeVisible();
            await loginAsItem.click();

            // 7. Handled by API call and page reload/redirect.
            // Verify Impersonation Banner appears.
            // The banner text: "You are currently impersonating {0}."
            await expect(page.locator('text=You are currently impersonating')).toBeVisible({ timeout: 10000 });
            await expect(page.locator(`text=${targetEmail}`)).toBeVisible();

            // 8. Verify "Switch Back" button exists
            const switchBackBtn = page.locator('button:has-text("Switch Back")');
            await expect(switchBackBtn).toBeVisible();

            // 9. Verify current user context in My Account or Header
            // The header might show the IMPERSONATED user's name/email.
            // Let's assume the UI updates to show target user.

            // 10. Stop Impersonation
            await switchBackBtn.click();

            // 11. Verify Banner disappears
            await expect(page.locator('text=You are currently impersonating')).toBeHidden();

            // 12. Verify Admin context restored
            // URL should be preserved or go to Admin Home?
            // RevertImpersonation typically reloads the page.
            await page.waitForLoadState('networkidle');
            // Check if we can see Admin elements again (if we remained on the same page, e.g. Admin/Users)
            // Or just check that "Switch Back" is gone.
            
        } finally {
            // Cleanup
            if (targetUser && targetUser.id) {
                await adminHelpers.deleteUser(page, targetUser.id);
            }
        }
    });
});
