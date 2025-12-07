import { test, expect } from '@playwright/test';

test.describe('Security Policies', () => {
    test.beforeEach(async ({ page }) => {
        // Login flow
        await page.goto('https://localhost:7035/Account/Login');
        await page.fill('#Input_Login', 'admin@hybridauth.local');
        await page.fill('#Input_Password', 'Admin@123');
        await page.click('button.auth-btn-primary');
        await page.waitForSelector('.user-name');

        await page.goto('/admin/security/policies');
    });

    test('should load security policies page', async ({ page }) => {
        await expect(page.getByRole('heading', { name: 'Security Policies' })).toBeVisible();
    });

    test('should allow saving policy changes', async ({ page }) => {
        // Wait for form to load (inputs populated)
        // Using "Minimum Length" label
        await expect(page.getByLabel('Minimum Length')).not.toHaveValue('');

        // Toggle Uppercase requirement
        const upperCaseCheckbox = page.getByLabel('Require Uppercase');
        const wasChecked = await upperCaseCheckbox.isChecked();
        await upperCaseCheckbox.click();

        // Save
        await page.getByRole('button', { name: 'Save Policy' }).click();

        // Expect success message
        // Fix: Apply .last() to the locator, then expect visibility
        await expect(page.getByText('Security policy saved successfully').last()).toBeVisible();

        // Revert change (optional, for cleanup)
        if (await upperCaseCheckbox.isChecked() !== wasChecked) {
             await upperCaseCheckbox.click();
             await page.getByRole('button', { name: 'Save Policy' }).click();
             await expect(page.getByText('Security policy saved successfully').last()).toBeVisible();
        }
    });
});
