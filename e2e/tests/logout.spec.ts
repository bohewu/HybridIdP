import { test, expect } from '@playwright/test';

async function loginViaTestClient(page) {
  await page.goto('/');
  await page.click('a:has-text("Login")');
  await expect(page).toHaveURL(/https:\/\/localhost:7035/);
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');
  await page.click('button.auth-btn-primary');
  const allowBtn = page.locator('button[name="submit"][value="allow"]');
  if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
    await allowBtn.click();
  }
  await page.waitForURL('**/Account/Profile', { timeout: 20000 });
}

test('TestClient logout clears session and OIDC logout works', async ({ page }) => {
  await loginViaTestClient(page);

  // Click the logout link in TestClient layout
  await page.click('a:has-text("Logout")');

  // After logout, trying to access Profile redirects to login
  await page.goto('/Account/Profile');
  // It should redirect to the IdP login page
  await expect(page).toHaveURL(/https:\/\/localhost:7035\/Account\/Login/);

  // Now exercise IdP logout: go to IdP logout and confirm not authenticated for IdP
  await page.goto('https://localhost:7035/Account/Logout');
  // If confirmation page displays, submit it
  const logoutForm = page.locator('form[action*="Logout"] button[type="submit"], button[type="submit"]');
  if (await logoutForm.count() > 0) {
    // Submit - some implementations use a POST form
    await logoutForm.first().click();
  }

  // Visit IdP homepage, should show Login link (not user-menu)
  await page.goto('https://localhost:7035');
  await expect(page.locator('a:has-text("Login")')).toBeVisible();
});
