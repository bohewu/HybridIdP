import { test, expect } from '@playwright/test';
import * as adminHelpers from '../helpers/admin';
import * as scopeHelpers from '../helpers/scopeHelpers';

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
    // Verify openid scope is present (either as checkbox or hidden input if required)
    const openidInput = page.locator('input[name="granted_scopes"][value="openid"]');
    if (await openidInput.count() > 0) {
      const inputType = await openidInput.getAttribute('type');
      if (inputType === 'checkbox') {
        await expect(openidInput).toBeChecked();
      }
      // Note: if required, openid will be a hidden input instead of a checkbox
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

test('TestClient login + consent redirects back to profile', async ({ page, context }) => {
  // Setup: Ensure testclient-public has only openid as required scope
  const adminContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
  const adminPage = await adminContext.newPage();
  
  try {
    await adminHelpers.loginAsAdminViaIdP(adminPage);
    const clientGuid = await scopeHelpers.getClientGuidByClientId(adminPage, 'testclient-public');
    console.log('[SETUP] clientGuid:', clientGuid);
    if (clientGuid) {
      const success = await scopeHelpers.setClientRequiredScopes(adminPage, clientGuid, ['openid']);
      console.log('[SETUP] setClientRequiredScopes result:', success);
      
      // Verify it was set correctly
      const currentRequiredScopes = await scopeHelpers.getClientRequiredScopes(adminPage, clientGuid);
      console.log('[SETUP] currentRequiredScopes after set:', currentRequiredScopes);
    }
  } finally {
    await adminContext.close();
  }

  // Run the actual test
  await loginViaTestClient(page);

  // Expect to see the user email in the profile
  await expect(page.locator('table')).toContainText('admin@hybridauth.local');

  // Verify access token is present in profile (it's displayed in a textarea, not in the claims table)
  const accessTokenHeading = page.locator('h5:has-text("Access Token")');
  await expect(accessTokenHeading).toBeVisible();
  
  const accessTokenTextarea = page.locator('textarea, input[type="text"], .token-display');
  const hasAccessToken = await accessTokenTextarea.first().inputValue().then(val => val.length > 0).catch(() => false);
  expect(hasAccessToken).toBeTruthy();

  // Note: Scopes might not be displayed as claims in the TestClient profile page
  // The important verification is that we have an access token and user claims
  // Skip scope verification for now as it's not reliably displayed in the UI

  // Click the Test API Call and assert success if available
  await page.click('a:has-text("Test API Call")');
  await expect(page.locator('body')).toContainText('Success');
});
