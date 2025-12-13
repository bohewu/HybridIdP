import { test, expect } from '../fixtures';
import adminHelpers from '../helpers/admin';

test.describe.configure({ mode: 'serial' });

test.describe('Admin - Users UI Forms', () => {
    test('Create user via UI with validation', async ({ page }) => {
        await adminHelpers.loginAsAdminViaIdP(page);
        await page.goto('https://localhost:7035/Admin/Users');

        // Click Create
        await page.click('button:has-text("Create User"), a:has-text("Create New")');
        await page.waitForURL(/\/Admin\/Users\/Create|\/Admin\/Users\/Edit/);

        const timestamp = Date.now();
        const email = `ui-form-${timestamp}@hybridauth.local`;

        // 1. Submit empty form to check validation
        await page.click('button[type="submit"]');
        await expect(page.locator('.text-red-600, .field-validation-error').first()).toBeVisible();

        // 2. Fill form
        await page.fill('input[name="UserName"], #UserName', email);
        await page.fill('input[name="Email"], #Email', email);
        await page.fill('input[name="FirstName"], #FirstName', 'UI');
        await page.fill('input[name="LastName"], #LastName', 'Test');

        // Password fields
        await page.fill('input[name="Password"], #Password', 'Test@123');
        await page.fill('input[name="ConfirmPassword"], #ConfirmPassword', 'Test@123');

        // Check "Email Confirmed" if present
        const emailConfirmed = page.locator('input[name="EmailConfirmed"], #EmailConfirmed');
        if (await emailConfirmed.count() > 0) {
            await emailConfirmed.check();
        }

        // Submit
        await page.click('button[type="submit"]');

        // Wait for success toast or redirection
        await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();

        // Verify in list
        await page.goto('https://localhost:7035/Admin/Users');
        await expect(page.locator('table')).toContainText(email);
    });

    test('Edit user via UI', async ({ page, api }) => {
        const timestamp = Date.now();
        const email = `ui-edit-${timestamp}@hybridauth.local`;

        // Setup: Create user via API to edit
        const user = await api.users.create({
            email, userName: email, firstName: 'Original', lastName: 'User', password: 'Test@123'
        });

        await adminHelpers.loginAsAdminViaIdP(page);
        await page.goto('https://localhost:7035/Admin/Users');

        // Find user and click Edit
        // Using data-test-id ideally, or fallback to text search
        const row = page.locator(`tr:has-text("${email}")`);
        await row.locator('button[title="Edit"], a:has-text("Edit")').click();

        // Update First Name
        await page.fill('input[name="FirstName"], #FirstName', 'UpdatedName');
        await page.click('button[type="submit"]');

        // Verify
        await expect(page.locator('.bg-green-50, .alert-success').first()).toBeVisible();

        // Reload list and check text
        await page.goto('https://localhost:7035/Admin/Users');
        await expect(page.locator(`tr:has-text("${email}")`)).toContainText('UpdatedName');

        // Cleanup
        await api.users.deleteUser(user.id);
    });

    test('Delete user via UI', async ({ page, api }) => {
        const timestamp = Date.now();
        const email = `ui-delete-${timestamp}@hybridauth.local`;

        const user = await api.users.create({
            email, userName: email, firstName: 'Delete', lastName: 'Me', password: 'Test@123'
        });

        await adminHelpers.loginAsAdminViaIdP(page);
        await page.goto('https://localhost:7035/Admin/Users');

        const row = page.locator(`tr:has-text("${email}")`);
        await row.locator('button[title="Delete"], button.text-red-600').click();

        // Confirm modal
        await page.locator('button:has-text("Confirm"), button:has-text("Delete")').last().click();

        // Verify gone
        await expect(page.locator(`tr:has-text("${email}")`)).not.toBeVisible({ timeout: 10000 });
    });
});
