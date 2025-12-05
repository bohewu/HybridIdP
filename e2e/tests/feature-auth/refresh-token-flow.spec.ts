import { test, expect } from '@playwright/test';
import { loginAsUser } from '../helpers/auth-helper';
import axios from 'axios';

/**
 * Phase 13.1: Refresh Token Flow E2E Test
 * 
 * Tests:
 * - User can obtain initial tokens via authorization code flow
 * - Refresh token can be used to obtain new access token
 * - Rolling refresh tokens: new refresh token is issued
 * - Audit logs capture token_issued events with grant_type
 */

test.describe('Refresh Token Flow', () => {
  test('should obtain and use refresh token to get new access token', async ({ page, context }) => {
    // Step 1: Login and capture authorization code
    await page.goto('https://localhost:7000/connect/authorize?client_id=testclient-public&redirect_uri=https://localhost:7001/signin-oidc&response_type=code&scope=openid%20profile%20email%20roles&code_challenge=test123&code_challenge_method=S256');
    
    // Login as test user
    await loginAsUser(page, 'testuser', 'Test@1234');
    
    // Wait for consent page or redirect
    await page.waitForTimeout(1000);
    
    // Check if on consent page - if so, approve
    if (page.url().includes('/connect/authorize')) {
      const approveButton = page.locator('button:has-text("Approve")');
      if (await approveButton.isVisible()) {
        await approveButton.click();
      }
    }
    
    // Capture the redirect with authorization code
    await page.waitForURL(/code=/);
    const url = page.url();
    const codeMatch = url.match(/code=([^&]+)/);
    expect(codeMatch).toBeTruthy();
    const authCode = codeMatch![1];
    
    // Step 2: Exchange authorization code for tokens (including refresh token)
    const tokenResponse = await axios.post('https://localhost:7000/connect/token', new URLSearchParams({
      grant_type: 'authorization_code',
      code: authCode,
      redirect_uri: 'https://localhost:7001/signin-oidc',
      client_id: 'testclient-public',
      code_verifier: 'test123'
    }), {
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      httpsAgent: new (require('https').Agent)({ rejectUnauthorized: false })
    });
    
    expect(tokenResponse.status).toBe(200);
    expect(tokenResponse.data.access_token).toBeTruthy();
    expect(tokenResponse.data.refresh_token).toBeTruthy();
    expect(tokenResponse.data.token_type).toBe('Bearer');
    
    const initialAccessToken = tokenResponse.data.access_token;
    const initialRefreshToken = tokenResponse.data.refresh_token;
    
    // Step 3: Use refresh token to obtain new access token
    await page.waitForTimeout(2000); // Wait to ensure different token issuance time
    
    const refreshResponse = await axios.post('https://localhost:7000/connect/token', new URLSearchParams({
      grant_type: 'refresh_token',
      refresh_token: initialRefreshToken,
      client_id: 'testclient-public'
    }), {
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      httpsAgent: new (require('https').Agent)({ rejectUnauthorized: false })
    });
    
    expect(refreshResponse.status).toBe(200);
    expect(refreshResponse.data.access_token).toBeTruthy();
    expect(refreshResponse.data.refresh_token).toBeTruthy();
    
    const newAccessToken = refreshResponse.data.access_token;
    const newRefreshToken = refreshResponse.data.refresh_token;
    
    // Verify rolling refresh tokens: new tokens are different from initial ones
    expect(newAccessToken).not.toBe(initialAccessToken);
    expect(newRefreshToken).not.toBe(initialRefreshToken);
    
    // Step 4: Verify new access token works by calling UserInfo endpoint
    const userInfoResponse = await axios.get('https://localhost:7000/connect/userinfo', {
      headers: {
        'Authorization': `Bearer ${newAccessToken}`
      },
      httpsAgent: new (require('https').Agent)({ rejectUnauthorized: false })
    });
    
    expect(userInfoResponse.status).toBe(200);
    expect(userInfoResponse.data.sub).toBeTruthy();
    
    // Step 5: Verify old refresh token is invalidated (should fail)
    try {
      await axios.post('https://localhost:7000/connect/token', new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: initialRefreshToken,
        client_id: 'testclient-public'
      }), {
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded'
        },
        httpsAgent: new (require('https').Agent)({ rejectUnauthorized: false })
      });
      
      // If we reach here, the old token was accepted (unexpected)
      expect(false).toBe(true); // Fail the test
    } catch (error: any) {
      // Expected: old refresh token should be rejected
      expect(error.response?.status).toBe(400);
    }
  });
  
  test('should audit log token issuance with grant_type', async ({ page }) => {
    // Login as admin to check audit logs
    await page.goto('https://localhost:7000');
    await loginAsUser(page, 'admin', 'Admin@1234');
    
    // Navigate to audit page
    await page.goto('https://localhost:7000/admin/audit');
    await page.waitForLoadState('networkidle');
    
    // Search for token_issued events
    const eventTypeFilter = page.locator('input[placeholder*="Event Type"], input[name="eventType"]');
    if (await eventTypeFilter.isVisible()) {
      await eventTypeFilter.fill('token_issued');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(1000);
    }
    
    // Verify token_issued events exist
    const auditRows = page.locator('table tbody tr, .audit-event-row');
    const count = await auditRows.count();
    
    // Should have at least one token_issued event
    expect(count).toBeGreaterThan(0);
    
    // Check if we can see grant_type in details (if details are visible)
    if (count > 0) {
      const firstRow = auditRows.first();
      await firstRow.click();
      await page.waitForTimeout(500);
      
      // Look for grant_type in expanded details or modal
      const detailsText = await page.textContent('body');
      // Should see either "authorization_code" or "refresh_token" grant type
      expect(detailsText).toMatch(/grant_type|authorization_code|refresh_token/i);
    }
  });
});
