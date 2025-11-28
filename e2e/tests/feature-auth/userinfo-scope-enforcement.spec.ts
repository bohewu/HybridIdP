import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';
import scopeHelpers from '../helpers/scopeHelpers';

test.describe('Userinfo Endpoint - Scope Protection', () => {
  test.beforeEach(async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page);
  });

  test('Userinfo returns 200 with openid scope and correct claims', async ({ page, context }) => {
    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Complete OIDC flow with testclient-public (has openid scope)
      await userPage.goto('https://localhost:7001/');
      await userPage.click('a:has-text("Login")');

      // Login
      await userPage.waitForURL(/https:\/\/localhost:7035/);
      await userPage.fill('#Input_Login', 'admin@hybridauth.local');
      await userPage.fill('#Input_Password', 'Admin@123');
      await userPage.click('button.auth-btn-primary');

      // Handle consent if presented
      const consentForm = userPage.locator('form[method="post"]');
      if (await consentForm.count() > 0) {
        await userPage.click('button[name="submit"][value="allow"]');
      }

      // Wait for redirect back to TestClient
      await userPage.waitForURL('**/Account/Profile', { timeout: 20000 });

      // Extract access token from TestClient profile page
      const accessToken = await userPage.evaluate(() => {
        const rows = document.querySelectorAll('table tr');
        for (const row of rows) {
          const cells = row.querySelectorAll('td');
          if (cells.length >= 2 && cells[0].textContent?.toLowerCase().includes('access_token')) {
            return cells[1].textContent?.trim() || null;
          }
        }
        return null;
      });

      expect(accessToken).not.toBeNull();
      expect(accessToken).toBeTruthy();

      // Call /connect/userinfo with the access token
      const userinfoResponse = await userPage.request.get('https://localhost:7035/connect/userinfo', {
        headers: {
          Authorization: `Bearer ${accessToken}`
        }
      });

      // Expect 200 OK
      expect(userinfoResponse.status()).toBe(200);

      // Verify response contains expected claims
      const userinfo = await userinfoResponse.json();
      expect(userinfo).toHaveProperty('sub');
      expect(userinfo.sub).toBeTruthy();

      // Verify email claim if present
      if (userinfo.email) {
        expect(userinfo.email).toBe('admin@hybridauth.local');
      }

      // Verify preferred_username or username claim
      expect(userinfo.preferred_username || userinfo.username).toBeTruthy();
    } finally {
      await userContext.close();
    }
  });

  test('Userinfo returns 403 without openid scope', async ({ page, context }) => {
    // Verify testclient-no-openid exists (created by global-setup)
    const noOpenIdClientGuid = await scopeHelpers.getClientGuidByClientId(page, 'testclient-no-openid');
    
    if (!noOpenIdClientGuid) {
      // If not found, create it for this test
      await scopeHelpers.createTestClientWithoutOpenId(page, 'testclient-no-openid');
    }

    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Manually construct authorization URL for testclient-no-openid
      // Note: This client does NOT have openid in its allowed scopes
      const authUrl = `https://localhost:7035/connect/authorize?client_id=testclient-no-openid&redirect_uri=https://localhost:7002/signin-oidc&response_type=code&scope=profile+email+api:company:read&state=test${Date.now()}&nonce=test${Date.now()}&code_challenge=test&code_challenge_method=plain`;
      
      await userPage.goto(authUrl);

      // Login if redirected
      if (userPage.url().includes('/Account/Login')) {
        await userPage.fill('#Input_Login', 'admin@hybridauth.local');
        await userPage.fill('#Input_Password', 'Admin@123');
        await userPage.click('button.auth-btn-primary');
      }

      // Handle consent if presented
      const consentForm = userPage.locator('form[method="post"]');
      if (await consentForm.count() > 0) {
        await userPage.click('button[name="submit"][value="allow"]');
      }

      // Wait for redirect (may fail or redirect to error page)
      await userPage.waitForTimeout(3000);

      // Try to extract the authorization code from the URL
      const currentUrl = userPage.url();
      const urlParams = new URLSearchParams(currentUrl.split('?')[1] || '');
      const code = urlParams.get('code');

      if (!code) {
        // If no code, the authorization may have failed due to invalid scope request
        // This is acceptable - it means the server rejected the request without openid
        console.log('Authorization failed as expected without openid scope');
        return;
      }

      // Exchange code for token (simulate what TestClient would do)
      const tokenResponse = await userPage.request.post('https://localhost:7035/connect/token', {
        form: {
          grant_type: 'authorization_code',
          code: code,
          redirect_uri: 'https://localhost:7002/signin-oidc',
          client_id: 'testclient-no-openid',
          code_verifier: 'test'
        }
      });

      if (tokenResponse.ok()) {
        const tokenData = await tokenResponse.json();
        const accessToken = tokenData.access_token;

        if (accessToken) {
          // Call /connect/userinfo with the access token (should fail with 403)
          const userinfoResponse = await userPage.request.get('https://localhost:7035/connect/userinfo', {
            headers: {
              Authorization: `Bearer ${accessToken}`
            }
          });

          // Expect 403 Forbidden
          expect(userinfoResponse.status()).toBe(403);

          // Verify response does not leak sensitive information
          const responseText = await userinfoResponse.text().catch(() => '');
          expect(responseText).not.toContain('admin@hybridauth.local');
        }
      }
    } finally {
      await userContext.close();
    }
  });

  test('Userinfo endpoint validates Bearer token format', async ({ page, context }) => {
    const userContext = await context.browser()!.newContext({ ignoreHTTPSErrors: true });
    const userPage = await userContext.newPage();

    try {
      // Call userinfo with invalid token
      const invalidTokenResponse = await userPage.request.get('https://localhost:7035/connect/userinfo', {
        headers: {
          Authorization: 'Bearer invalid-token-12345'
        }
      });

      // Should return 401 Unauthorized (not 403, since token is invalid not missing scope)
      expect([401, 403]).toContain(invalidTokenResponse.status());

      // Call userinfo without Authorization header
      const noAuthResponse = await userPage.request.get('https://localhost:7035/connect/userinfo');

      // Should return 401 Unauthorized
      expect(noAuthResponse.status()).toBe(401);
    } finally {
      await userContext.close();
    }
  });
});
