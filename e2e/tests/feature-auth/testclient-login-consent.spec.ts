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

  // If we see the consent page, verify scope checkboxes and click Allow
  const allowBtn = page.locator('button[name="submit"][value="allow"]');
  if (await allowBtn.count() > 0 && await allowBtn.isVisible()) {
    // Verify openid scope is present and checked (should be required/disabled)
    const openidCheckbox = page.locator('input[name="granted_scopes"][value="openid"]');
    if (await openidCheckbox.count() > 0) {
      await expect(openidCheckbox).toBeChecked();
      // Note: openid should be disabled if marked as required by global-setup
    }

    // Verify profile scope is present and enabled (optional)
    const profileCheckbox = page.locator('input[name="granted_scopes"][value="profile"]');
    if (await profileCheckbox.count() > 0) {
      await expect(profileCheckbox).toBeEnabled();
    }

    await allowBtn.click();
  }

  // Wait for redirect back to TestClient profile
  await page.waitForURL('**/Account/Profile', { timeout: 60000 });
}

test('TestClient login + consent redirects back to profile', async ({ page }) => {
  await loginViaTestClient(page);

  // Expect to see the user email in the profile
  await expect(page.locator('table')).toContainText('admin@hybridauth.local');

  // Verify access token is present in profile
  const hasAccessToken = await page.evaluate(() => {
    const rows = document.querySelectorAll('table tr');
    for (const row of rows) {
      const cells = row.querySelectorAll('td');
      if (cells.length >= 2 && cells[0].textContent?.toLowerCase().includes('access_token')) {
        return cells[1].textContent?.trim() ? true : false;
      }
    }
    return false;
  });
  expect(hasAccessToken).toBeTruthy();

  // Verify scopes are present (should include openid at minimum)
  const scopes = await page.evaluate(() => {
    const rows = document.querySelectorAll('table tr');
    for (const row of rows) {
      const cells = row.querySelectorAll('td');
      if (cells.length >= 2 && cells[0].textContent?.toLowerCase().includes('scope')) {
        return cells[1].textContent?.trim() || '';
      }
    }
    return '';
  });
  expect(scopes).toContain('openid');

  // Click the Test API Call and assert success if available
  await page.click('a:has-text("Test API Call")');
  await expect(page.locator('body')).toContainText('Success');
});
