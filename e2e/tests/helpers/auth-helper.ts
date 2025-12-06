import { Page, expect } from '@playwright/test';

export async function loginAsUser(page: Page, username: string, password: string = 'Test@1234') {
    const loginInput = page.locator('#Input_Login');
    if (await loginInput.isVisible()) {
        await loginInput.fill(username);
        await page.locator('#Input_Password').fill(password);
        await page.click('button.auth-btn-primary');

        try {
            await page.waitForSelector('.user-name', { timeout: 30000 });
        } catch (e) {
            console.error(`Login failed for ${username}.`);
            throw e;
        }
    }
}
