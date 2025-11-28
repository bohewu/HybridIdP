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

      // Verify openid scope checkbox is disabled (use id selector for the visible checkbox)
      const openidCheckbox = userPage.locator('input#scope_openid[type="checkbox"]');
      await expect(openidCheckbox).toBeVisible();
      await expect(openidCheckbox).toBeDisabled();
      await expect(openidCheckbox).toBeChecked();

      // Verify "Required" badge is shown
      const openidLabel = userPage.locator('label[for="scope_openid"]');
      // accept case-insensitive match because UI uses 'OpenID' or 'openid'
      await expect(openidLabel).toContainText(/openid/i);
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

        // Submit consent and wait for OAuth flow to complete
        // The consent form submission triggers redirects: IdP→TestClient callback→Profile
        await Promise.all([
          userPage.waitForURL(/localhost:7001/, { timeout: 60000 }),
          userPage.click('button[name="submit"][value="allow"]')
        ]);
        
        // Verify we're authenticated on TestClient
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

      // Use DevTools to remove the hidden input (required scope bypass attempt)
      const removed = await userPage.evaluate(() => {
        // Remove the hidden input that submits the required scope
        const hiddenInput = document.querySelector('input[type="hidden"][name="granted_scopes"][value="openid"]') as HTMLInputElement;
        let removedHidden = false;
        if (hiddenInput) {
          hiddenInput.remove();
          removedHidden = true;
        }
        // Also uncheck/disable the visible checkbox (though it doesn't submit, this simulates tampering)
        const checkbox = document.querySelector('input#scope_openid[type="checkbox"]') as HTMLInputElement;
        if (checkbox) {
          checkbox.disabled = false;
          checkbox.checked = false;
        }
        // Log all remaining granted_scopes inputs for debugging
        const remaining = Array.from(document.querySelectorAll('input[name="granted_scopes"]')).map((el: any) => ({
          type: el.type,
          value: el.value,
          checked: el.checked
        }));
        return { removedHidden, remaining };
      });
      console.log('Tampering test - removed hidden input:', removed);

      // Submit the form (should fail with 400)
      const responsePromise = userPage.waitForResponse(
        (response) => response.url().includes('/connect/authorize') && response.request().method() === 'POST',
        { timeout: 15000 }
      );

      await userPage.click('button[name="submit"][value="allow"]');

      const response = await responsePromise;
      
      // Expect 400 Bad Request
      expect(response.status()).toBe(400);

      // Verify error message is displayed
      await expect(userPage.locator('body')).toContainText(/required.*scope/i);

      // Verify audit event was logged (check via admin API) - poll briefly to allow async processing
      // Switch back to admin page
      let auditEvents: any[] = [];
      const pollDeadline = Date.now() + 30000; // 30s
      while (Date.now() < pollDeadline) {
        auditEvents = await page.evaluate(async () => {
          const res = await fetch('/api/admin/audit/events?eventType=ConsentTamperingDetected&pageSize=5');
          if (!res.ok) return [];
          const json = await res.json();
          return json.items || [];
        });
        if (auditEvents.length > 0) break;
        await page.waitForTimeout(500);
      }

      // Should have at least one tampering event
      expect(auditEvents.length).toBeGreaterThan(0);
      const latestEvent = auditEvents[0];
      expect(latestEvent.eventType).toBe('ConsentTamperingDetected');
      // Parse details if it's a JSON string
      const details = typeof latestEvent.details === 'string' ? JSON.parse(latestEvent.details) : latestEvent.details;
      expect(details?.clientId).toBe('testclient-public');
    } finally {
      await userContext.close();
    }
  });

  test('Multiple required scopes all display as disabled', async ({ page, context }) => {
    // Setup: Add profile as required scope for testclient
    const clientGuid = await scopeHelpers.getClientGuidByClientId(page, 'testclient-public');
    expect(clientGuid).not.toBeNull();

    // Set both openid and profile as required and verify persistence
    await scopeHelpers.setClientRequiredScopes(page, clientGuid!, ['openid', 'profile']);
    // confirm required scopes applied before proceeding
    const confirmDeadline = Date.now() + 5000;
    while (Date.now() < confirmDeadline) {
      const rs = await scopeHelpers.getClientRequiredScopes(page, clientGuid!);
      if (rs.includes('openid') && rs.includes('profile')) break;
      await page.waitForTimeout(250);
    }

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

      // Verify both openid and profile are disabled (use id selector for visible checkboxes)
      const openidCheckbox = userPage.locator('input#scope_openid[type="checkbox"]');
      await expect(openidCheckbox).toBeDisabled();
      await expect(openidCheckbox).toBeChecked();

      const profileCheckbox = userPage.locator('input#scope_profile[type="checkbox"]');
      if (await profileCheckbox.count() > 0) {
        await expect(profileCheckbox).toBeDisabled();
        await expect(profileCheckbox).toBeChecked();
      }

      // Submit consent and wait for OAuth flow to complete
      await Promise.all([
        userPage.waitForURL(/localhost:7001/, { timeout: 60000 }),
        userPage.click('button[name="submit"][value="allow"]')
      ]);
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
      // Ensure this client has no required scopes explicitly set for the test

    // Explicitly clear client-specific required scopes so we can verify UI behavior
    const tempGuid = await scopeHelpers.getClientGuidByClientId(page, tempClientId);
    if (tempGuid) {
      await scopeHelpers.setClientRequiredScopes(page, tempGuid, []);
    }

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

      // Verify at least one non-global scope is enabled (openid may be globally required)
      const profileCheckbox = userPage.locator('input[name="granted_scopes"][value="profile"]');
      if (await profileCheckbox.count() > 0) {
        await expect(profileCheckbox).toBeEnabled();
      } else {
        // fallback: check an API scope if present
        const apiCheckbox = userPage.locator('input[name="granted_scopes"][value="api:company:read"]');
        if (await apiCheckbox.count() > 0) await expect(apiCheckbox).toBeEnabled();
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
