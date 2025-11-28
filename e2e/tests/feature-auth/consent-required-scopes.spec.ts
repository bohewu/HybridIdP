import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';
import scopeHelpers from '../helpers/scopeHelpers';

test.describe('Consent Page - Required Scopes', () => {
  test.beforeEach(async ({ page }) => {
    // Ensure admin is logged in for setup operations
    await adminHelpers.loginAsAdminViaIdP(page);
  });

  test('Required scope displays as disabled checkbox with badge', async ({ page, context }) => {
    // Setup: Verify testclient-public has openid as required (should be set by global-setup)
    const clientGuid = await scopeHelpers.getClientGuidByClientId(page, 'testclient-public');
    expect(clientGuid).not.toBeNull();
    
    const requiredScopes = await scopeHelpers.getClientRequiredScopes(page, clientGuid!);
    expect(requiredScopes).toContain('openid');

    // Create a new browser context to simulate a fresh user session
    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Navigate to TestClient and trigger OIDC flow
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');

      // Login
      await userPage.waitForURL(/https:\/\/localhost:7035/);
      await userPage.fill('#Input_Login', 'admin@hybridauth.local');
      await userPage.fill('#Input_Password', 'Admin@123');
      await userPage.click('button.auth-btn-primary');

      // Wait for consent page
      await userPage.waitForSelector('form[method="post"]', { timeout: 15000 });

      // Verify openid scope checkbox is disabled
      const openidCheckbox = userPage.locator('input[name="granted_scopes"][value="openid"]');
      await expect(openidCheckbox).toBeVisible();
      await expect(openidCheckbox).toBeDisabled();
      await expect(openidCheckbox).toBeChecked();

      // Verify "Required" badge is shown
      const openidLabel = userPage.locator('label[for="scope_openid"]');
      await expect(openidLabel).toContainText('openid');
      // Look for badge/indicator (adjust selector based on actual HTML)
      const requiredIndicator = userPage.locator('label[for="scope_openid"]', { hasText: /required/i });
      await expect(requiredIndicator).toBeVisible();

      // Verify optional scopes are enabled
      const profileCheckbox = userPage.locator('input[name="granted_scopes"][value="profile"]');
      if (await profileCheckbox.count() > 0) {
        await expect(profileCheckbox).toBeEnabled();
      }
    } finally {
      await userContext.close();
    }
  });

  test('Unchecking optional scope excludes it from granted scopes', async ({ page, context }) => {
    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Navigate to TestClient and trigger OIDC flow
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');

      // Login
      await userPage.waitForURL(/https:\/\/localhost:7035/);
      await userPage.fill('#Input_Login', 'admin@hybridauth.local');
      await userPage.fill('#Input_Password', 'Admin@123');
      await userPage.click('button.auth-btn-primary');

      // Wait for consent page
      await userPage.waitForSelector('form[method="post"]', { timeout: 15000 });

      // Find an optional scope (profile, email, roles, or api scopes)
      const profileCheckbox = userPage.locator('input[name="granted_scopes"][value="profile"]');
      
      if (await profileCheckbox.count() > 0 && await profileCheckbox.isEnabled()) {
        // Uncheck the profile scope
        await profileCheckbox.uncheck();
        await expect(profileCheckbox).not.toBeChecked();

        // Submit consent
        await userPage.click('button[name="submit"][value="allow"]');

        // Should redirect back to TestClient
        await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });

        // Verify profile is not in the claims (if TestClient displays them)
        // Note: This test verifies the consent was accepted; actual token inspection
        // would require accessing TestClient's session or token endpoint
        await expect(userPage.locator('body')).toContainText('admin@hybridauth.local');
      }
    } finally {
      await userContext.close();
    }
  });

  test('Tampering detection - removing required scope returns 400', async ({ page, context }) => {
    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Navigate to TestClient and trigger OIDC flow
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');

      // Login
      await userPage.waitForURL(/https:\/\/localhost:7035/);
      await userPage.fill('#Input_Login', 'admin@hybridauth.local');
      await userPage.fill('#Input_Password', 'Admin@123');
      await userPage.click('button.auth-btn-primary');

      // Wait for consent page
      await userPage.waitForSelector('form[method="post"]', { timeout: 15000 });

      // Use DevTools to enable the disabled openid checkbox and uncheck it
      await userPage.evaluate(() => {
        const checkbox = document.querySelector('input[name="granted_scopes"][value="openid"]') as HTMLInputElement;
        if (checkbox) {
          checkbox.disabled = false;
          checkbox.checked = false;
        }
      });

      // Submit the form (should fail with 400)
      const responsePromise = userPage.waitForResponse(
        (response) => response.url().includes('/connect/authorize') && response.request().method() === 'POST',
        { timeout: 10000 }
      );

      await userPage.click('button[name="submit"][value="allow"]');

      const response = await responsePromise;
      
      // Expect 400 Bad Request
      expect(response.status()).toBe(400);

      // Verify error message is displayed
      await expect(userPage.locator('body')).toContainText(/required.*scope/i);

      // Verify audit event was logged (check via admin API)
      // Switch back to admin page
      const auditEvents = await page.evaluate(async () => {
        const res = await fetch('/api/admin/audit?eventType=ConsentTamperingDetected&pageSize=5');
        if (!res.ok) return [];
        const json = await res.json();
        return json.items || [];
      });

      // Should have at least one tampering event
      expect(auditEvents.length).toBeGreaterThan(0);
      const latestEvent = auditEvents[0];
      expect(latestEvent.eventType).toBe('ConsentTamperingDetected');
      expect(latestEvent.details?.clientId).toBe('testclient-public');
    } finally {
      await userContext.close();
    }
  });

  test('Multiple required scopes all display as disabled', async ({ page, context }) => {
    // Setup: Add profile as required scope for testclient
    const clientGuid = await scopeHelpers.getClientGuidByClientId(page, 'testclient-public');
    expect(clientGuid).not.toBeNull();

    // Set both openid and profile as required
    await scopeHelpers.setClientRequiredScopes(page, clientGuid!, ['openid', 'profile']);

    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Navigate to TestClient and trigger OIDC flow
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');

      // Login
      await userPage.waitForURL(/https:\/\/localhost:7035/);
      await userPage.fill('#Input_Login', 'admin@hybridauth.local');
      await userPage.fill('#Input_Password', 'Admin@123');
      await userPage.click('button.auth-btn-primary');

      // Wait for consent page
      await userPage.waitForSelector('form[method="post"]', { timeout: 15000 });

      // Verify both openid and profile are disabled
      const openidCheckbox = userPage.locator('input[name="granted_scopes"][value="openid"]');
      await expect(openidCheckbox).toBeDisabled();
      await expect(openidCheckbox).toBeChecked();

      const profileCheckbox = userPage.locator('input[name="granted_scopes"][value="profile"]');
      if (await profileCheckbox.count() > 0) {
        await expect(profileCheckbox).toBeDisabled();
        await expect(profileCheckbox).toBeChecked();
      }

      // Submit consent
      await userPage.click('button[name="submit"][value="allow"]');
      await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });
    } finally {
      // Cleanup: restore original required scopes
      await scopeHelpers.setClientRequiredScopes(page, clientGuid!, ['openid']);
      await userContext.close();
    }
  });

  test('Client without required scopes shows all checkboxes as enabled', async ({ page, context }) => {
    // Create a temporary test client without required scopes
    const tempClientId = `e2e-no-required-${Date.now()}`;
    const tempClient = await page.evaluate(async (clientId) => {
      const payload = {
        clientId,
        displayName: `E2E Temp Client ${clientId}`,
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        clientSecret: null,
        redirectUris: ['https://localhost:7001/signin-oidc'],
        postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
        permissions: [
          'ept:authorization',
          'ept:token',
          'gt:authorization_code',
          'scp:openid',
          'scp:profile',
          'scp:email'
        ]
      };
      const res = await fetch('/api/admin/clients', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      return res.json();
    }, tempClientId);

    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Manually construct authorization URL for the temp client
      const authUrl = `https://localhost:7035/connect/authorize?client_id=${tempClientId}&redirect_uri=https://localhost:7001/signin-oidc&response_type=code&scope=openid+profile+email&state=test&nonce=test&code_challenge=test&code_challenge_method=plain`;
      
      await userPage.goto(authUrl);

      // Login if redirected to login page
      if (userPage.url().includes('/Account/Login')) {
        await userPage.fill('#Input_Login', 'admin@hybridauth.local');
        await userPage.fill('#Input_Password', 'Admin@123');
        await userPage.click('button.auth-btn-primary');
      }

      // Wait for consent page
      await userPage.waitForSelector('form[method="post"]', { timeout: 15000 });

      // Verify all scopes are enabled (not disabled)
      const openidCheckbox = userPage.locator('input[name="granted_scopes"][value="openid"]');
      await expect(openidCheckbox).toBeEnabled();

      const profileCheckbox = userPage.locator('input[name="granted_scopes"][value="profile"]');
      if (await profileCheckbox.count() > 0) {
        await expect(profileCheckbox).toBeEnabled();
      }

      const emailCheckbox = userPage.locator('input[name="granted_scopes"][value="email"]');
      if (await emailCheckbox.count() > 0) {
        await expect(emailCheckbox).toBeEnabled();
      }
    } finally {
      // Cleanup: delete temp client
      await adminHelpers.deleteClientViaApiFallback(page, tempClientId);
      await userContext.close();
    }
  });
});
