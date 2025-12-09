import { test, expect } from '@playwright/test';
import * as adminHelpers from '../helpers/admin';

test.describe('Admin - Security Settings', () => {
    test.beforeEach(async ({ page }) => {
        // Ensure admin API and login
        await adminHelpers.ensureAdminAvailable(page);
        await adminHelpers.loginAsAdminViaIdP(page);
    });

    test('Should load and update security policy', async ({ page }) => {
        // 1. Navigate to Settings Page
        await page.goto('https://localhost:7035/Admin/Settings');
        await page.waitForLoadState('networkidle');
        
        // Verify we are on the settings page by checking for the page header or specific content
        await expect(page.getByRole('heading', { name: /System Settings|系統設定/ })).toBeVisible();

        // 2. Locate Security Policy Section using data-testid
        const securitySectionHost = page.getByTestId('security-policy-section');
        await expect(securitySectionHost).toBeVisible();

        // 3. Modify "Minimum Password Length" input using data-testid
        const minLengthInput = page.getByTestId('min-password-length-input');
        await expect(minLengthInput).toBeVisible();
        
        const originalValue = await minLengthInput.inputValue();
        const newValue = (parseInt(originalValue) || 6) + 1;

        await minLengthInput.fill(newValue.toString());

        // 4. Save using data-testid
        const saveButton = page.getByTestId('save-policy-button');
        await saveButton.click();

        // 5. Verify Success Message using data-testid
        await expect(page.getByTestId('success-message')).toBeVisible();
        await expect(page.getByTestId('success-message')).toContainText(/saved|儲存/);
    });
});
