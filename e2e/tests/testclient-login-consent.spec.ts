import { test, expect } from '@playwright/test';

async function loginViaTestClient(page) {
  // Start at the TestClient home page
  await page.goto('/');

  // Click the login/profile link to trigger OIDC challenge
  await page.click('a:has-text("Login")');

  // The IdP will serve the login page under https://localhost:7035
  await expect(page).toHaveURL(/https:\/\/localhost:7035/);

  // Fill in credentials (use the development test user)
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');

  // Submit the login form
  await page.click('button.auth-btn-primary');

  // If we see the consent page, click Allow
  const allowBtn = page.locator('button[name="submit"][value="allow"]');
  if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
    await allowBtn.click();
  }

  // Wait for redirect back to TestClient profile
  await page.waitForURL('**/Account/Profile', { timeout: 20000 });
}

test('TestClient login + consent redirects back to profile', async ({ page }) => {
  await loginViaTestClient(page);

  // Expect to see the user email in the profile
  await expect(page.locator('table')).toContainText('admin@hybridauth.local');

  // Click the Test API Call and assert success if available
  await page.click('a:has-text("Test API Call")');
  await expect(page.locator('body')).toContainText('Success');
});
