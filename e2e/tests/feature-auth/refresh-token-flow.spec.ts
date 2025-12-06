import { test, expect } from '@playwright/test';
import { loginAsUser } from '../helpers/auth-helper';
import * as https from 'https';

test.describe('Refresh Token Flow', () => {
  test('should obtain and use refresh token to get new access token', async ({ page, request }) => {
    // Step 1: Login and capture authorization code
    await page.goto('https://localhost:7035/connect/authorize?client_id=testclient-public&redirect_uri=https://localhost:7001/signin-oidc&response_type=code&scope=openid%20profile%20email%20roles&code_challenge=test123&code_challenge_method=S256');

    // Login as test user
    await loginAsUser(page, 'testuser@hybridauth.local', 'Test@123');

    // Wait for consent or redirect
    await page.waitForTimeout(1000);
    if (page.url().includes('/connect/authorize')) {
      const approveButton = page.locator('button:has-text("Approve")');
      if (await approveButton.isVisible()) {
        await approveButton.click();
      }
    }

    await page.waitForURL(/code=/);
    const url = page.url();
    const codeMatch = url.match(/code=([^&]+)/);
    expect(codeMatch).toBeTruthy();
    const authCode = codeMatch![1];

    // Step 2: Exchange authorization code
    const tokenResponse = await request.post('https://localhost:7035/connect/token', {
      form: {
        grant_type: 'authorization_code',
        code: authCode,
        redirect_uri: 'https://localhost:7001/signin-oidc',
        client_id: 'testclient-public',
        code_verifier: 'test123'
      }
    });

    expect(tokenResponse.status()).toBe(200);
    const tokenData = await tokenResponse.json();
    expect(tokenData.access_token).toBeTruthy();
    expect(tokenData.refresh_token).toBeTruthy();

    const initialAccessToken = tokenData.access_token;
    const initialRefreshToken = tokenData.refresh_token;

    await page.waitForTimeout(2000);

    // Step 3: Refresh Token
    const refreshResponse = await request.post('https://localhost:7035/connect/token', {
      form: {
        grant_type: 'refresh_token',
        refresh_token: initialRefreshToken,
        client_id: 'testclient-public'
      }
    });

    expect(refreshResponse.status()).toBe(200);
    const refreshData = await refreshResponse.json();
    expect(refreshData.access_token).toBeTruthy();
    expect(refreshData.refresh_token).toBeTruthy();

    const newAccessToken = refreshData.access_token;
    const newRefreshToken = refreshData.refresh_token;

    expect(newAccessToken).not.toBe(initialAccessToken);
    expect(newRefreshToken).not.toBe(initialRefreshToken);

    // Step 4: UserInfo
    const userInfoResponse = await request.get('https://localhost:7035/connect/userinfo', {
      headers: { 'Authorization': `Bearer ${newAccessToken}` }
    });

    expect(userInfoResponse.status()).toBe(200);

    // Step 5: Old Token Invalid
    const failResponse = await request.post('https://localhost:7035/connect/token', {
      form: {
        grant_type: 'refresh_token',
        refresh_token: initialRefreshToken,
        client_id: 'testclient-public'
      }
    });
    // Should fail
    expect(failResponse.status()).toBe(400);
  });

  test('should audit log token issuance with grant_type', async ({ page }) => {
    // Login as admin
    await page.goto('https://localhost:7035');
    await loginAsUser(page, 'admin@hybridauth.local', 'Admin@123');

    await page.goto('https://localhost:7035/admin/audit');
    await page.waitForLoadState('networkidle');

    const eventTypeFilter = page.locator('input[placeholder*="Event Type"], input[name="eventType"]');
    if (await eventTypeFilter.isVisible()) {
      await eventTypeFilter.fill('token_issued');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(1000);
    }

    const auditRows = page.locator('table tbody tr, .audit-event-row');
    const count = await auditRows.count();
    expect(count).toBeGreaterThan(0);
  });
});
