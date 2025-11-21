import { test, expect } from '@playwright/test';

test('login flow: login admin and redirect to profile', async ({ page }) => {
  // Start at the TestClient home page
  await page.goto('/');

  // Click the login/profile link to trigger OIDC challenge
  await page.click('a:has-text("Login")');

  // The IdP will serve the login page under https://localhost:7035
  await expect(page).toHaveURL(/https:\/\/localhost:7035/);

  // Fill in credentials (use the development test user in README)
  await page.fill('#Input_Login', 'admin@hybridauth.local');
  await page.fill('#Input_Password', 'Admin@123');

  // Submit the form
  await page.click('button.auth-btn-primary');

  // Wait for redirect back to TestClient profile and expect to see the email
  await page.waitForURL('**/Account/Profile', { timeout: 20000 });

  // Assert that the profile contains the admin email somewhere in the claims table
  await expect(page.locator('table')).toContainText('admin@hybridauth.local');
});
