import { test, expect } from '@playwright/test';

test('login flow: IdP login (admin) and authenticated state', async ({ page }) => {
  // Start directly at the IdP login page
  await page.goto('https://localhost:7035/');

  // If not authenticated we should be redirected to the login page
  await expect(page).toHaveURL(/https:\/\/localhost:7035\/Account\/Login/);

  // Fill in credentials (use the development test user in README)
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');

  // Submit the form
  await page.click('button.auth-btn-primary');

  // After login we should be authenticated on the IdP and see a logout link
  await page.waitForSelector('a[href="/Account/Logout"]', { timeout: 20000 });
  await expect(page.locator('a[href="/Account/Logout"]')).toBeVisible();
});
