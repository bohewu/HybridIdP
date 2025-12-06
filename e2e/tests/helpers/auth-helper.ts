import { Page, expect } from '@playwright/test';

export async function loginAsUser(page: Page, username: string, password: string = 'Test@1234') {
    const loginInput = page.locator('#Input_Login');
    // If login input is visible, perform login. 
    // If not visible, we assume we might already be logged in, but the waitForSelector below acts as verification.
    if (await loginInput.isVisible()) {
        await loginInput.fill(username);
        await page.locator('#Input_Password').fill(password);
        await page.click('button.auth-btn-primary');

        try {
            // Wait for user menu to appear. 
            // .user-name is used in standard _Layout (Client)
            // .user-info-name is used in _AdminLayout (Admin)
            await page.waitForSelector('.user-name, .user-info-name', { timeout: 30000 });
        } catch (e) {
            console.error(`Login failed for ${username}.`);
            // Check for error messages
            const errorSummary = await page.textContent('.validation-summary-errors');
            if (errorSummary) console.error('Validation Summary:', errorSummary);
            
            const fieldErrors = await page.locator('.field-validation-error').allTextContents();
            if (fieldErrors.length > 0) console.error('Field Errors:', fieldErrors);

            console.log(`[AuthHelper] Login failed. Current URL: ${page.url()}`);
            console.log('Taking screenshot auth-failure.png');
            try {
                await page.screenshot({ path: 'auth-failure.png', fullPage: true });
            } catch (screenshotErr) {
                 console.error('Screenshot failed:', screenshotErr);
            }

            throw e;
        }
    }
}
