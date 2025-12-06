import { test, expect } from '@playwright/test';
import { loginAsUser } from './helpers/auth-helper';

test('Debug Login', async ({ page }) => {
    // Navigate to a protected page to trigger redirect
    await page.goto('https://localhost:7035/admin/clients');
    console.log('Navigated to /admin/clients (should redirect to login)');
    
    // Attempt Login
    await loginAsUser(page, 'admin@hybridauth.local', 'Admin@123');
    
    // Check if we are logged in
    const userName = page.locator('.user-name, .user-info-name');
    try {
        // Poll URL for debugging
        const start = Date.now();
        while (Date.now() - start < 30000) {
            if (await userName.isVisible()) break;
            console.log(`[${Date.now() - start}ms] Current URL: ${page.url()}`);
            await page.waitForTimeout(2000);
        }
        
        await expect(userName).toBeVisible({ timeout: 1000 }); // Should be immediate now
        console.log('Login successful');
    } catch (e) {
        console.log(`Login failed. Current URL: ${page.url()}`);
        console.log('Taking screenshot login-failure-redirect.png');
        await page.screenshot({ path: 'login-failure-redirect.png', fullPage: true });
        
        // check for body text
        const bodyText = await page.textContent('body');
        console.log('Body start:', bodyText?.substring(0, 500));
        
        throw e;
    }
});
