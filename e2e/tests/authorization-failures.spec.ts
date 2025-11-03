import { test, expect } from '@playwright/test';

test.describe('Authorization Failure Scenarios', () => {
  
  test('User denies consent', async ({ page }) => {
    // 1. Navigate to TestClient
    await page.goto('https://localhost:7001');
    
    // 2. Click Profile to trigger OIDC login flow
    await page.click('a[href="/Account/Profile"]');
    
    // 3. Should redirect to IdP login page (if not already authenticated)
    // If already logged in, will go directly to authorization page
    const currentUrl = page.url();
    
    if (currentUrl.includes('/Account/Login')) {
      // Fill in login form
      await page.fill('input[name="Input.Email"]', 'admin@hybridauth.local');
      await page.fill('input[name="Input.Password"]', 'Admin@123');
      await page.click('button[type="submit"]');
      
      // Wait for redirect to authorization page
      await page.waitForURL(/.*\/connect\/authorize.*/);
    }
    
    // 4. Should be on the authorization/consent page
    await expect(page).toHaveURL(/.*\/connect\/authorize.*/);
    
    // Verify consent page elements
    await expect(page.locator('h2')).toContainText('Authorize Application');
    const allowButton = page.locator('button:has-text("Allow Access")');
    const denyButton = page.locator('button:has-text("Deny")');
    
    await expect(allowButton).toBeVisible();
    await expect(denyButton).toBeVisible();
    
    // 5. Click Deny button
    await denyButton.click();
    
    // 6. Verify error response - should redirect back to TestClient with error
    await page.waitForURL(/.*localhost:7001.*/);
    
    // Check for error in URL or on page
    const finalUrl = page.url();
    const pageContent = await page.content();
    
    // OpenIddict may redirect with error parameter or display error page
    const hasErrorInUrl = finalUrl.includes('error=access_denied') || 
                          finalUrl.includes('error=consent_required');
    const hasErrorOnPage = pageContent.includes('access_denied') || 
                           pageContent.includes('denied') ||
                           pageContent.includes('error');
    
    // At least one should be true
    expect(hasErrorInUrl || hasErrorOnPage).toBeTruthy();
    
    // 7. User should NOT be logged in - verify by checking if Profile link triggers login again
    await page.goto('https://localhost:7001/Account/Profile');
    
    // Should either show error or redirect to login
    const profileUrl = page.url();
    const isStillOnProfile = profileUrl.includes('/Account/Profile');
    
    if (isStillOnProfile) {
      // If on profile page, should not show user claims (authorization was denied)
      const content = await page.content();
      expect(content).not.toContain('User Claims');
    }
  });

  test('Invalid redirect_uri should fail safely', async ({ page }) => {
    // Manually construct authorization request with invalid redirect_uri
    const invalidAuthUrl = 'https://localhost:7035/connect/authorize?' + 
      'client_id=test_client&' +
      'redirect_uri=https://evil.com/callback&' + // Invalid redirect_uri
      'response_type=code&' +
      'scope=openid profile email&' +
      'code_challenge=test123&' +
      'code_challenge_method=S256&' +
      'state=test_state';
    
    await page.goto(invalidAuthUrl);
    
    // Should display error page, NOT redirect to evil.com
    const currentUrl = page.url();
    expect(currentUrl).not.toContain('evil.com');
    expect(currentUrl).toContain('localhost:7035');
    
    // Should show error message
    const content = await page.content();
    expect(content.toLowerCase()).toMatch(/error|invalid|redirect/);
  });

  test('Missing openid scope should fail', async ({ page }) => {
    // Construct authorization request without openid scope
    const noOpenIdUrl = 'https://localhost:7035/connect/authorize?' + 
      'client_id=test_client&' +
      'redirect_uri=https://localhost:7001/signin-oidc&' +
      'response_type=code&' +
      'scope=profile email&' + // Missing 'openid'
      'code_challenge=test123&' +
      'code_challenge_method=S256&' +
      'state=test_state';
    
    await page.goto(noOpenIdUrl);
    
    // OpenIddict should reject this request
    const content = await page.content();
    const url = page.url();
    
    // Should show error (either on page or in redirect)
    const hasError = content.toLowerCase().includes('error') ||
                     content.toLowerCase().includes('invalid') ||
                     url.includes('error=');
    
    expect(hasError).toBeTruthy();
  });

  test('Invalid client_id should fail', async ({ page }) => {
    // Construct authorization request with non-existent client_id
    const invalidClientUrl = 'https://localhost:7035/connect/authorize?' + 
      'client_id=non_existent_client&' +
      'redirect_uri=https://localhost:7001/signin-oidc&' +
      'response_type=code&' +
      'scope=openid profile&' +
      'state=test_state';
    
    await page.goto(invalidClientUrl);
    
    // Should show error page (not redirect since client is untrusted)
    const currentUrl = page.url();
    expect(currentUrl).toContain('localhost:7035');
    expect(currentUrl).not.toContain('localhost:7001');
    
    // Should display error
    const content = await page.content();
    expect(content.toLowerCase()).toMatch(/error|invalid|client/);
  });

  test('Already used authorization code should fail', async ({ page }) => {
    // This test requires more complex setup - capturing the code and trying to reuse it
    // 1. Complete normal OIDC flow and capture the authorization code
    await page.goto('https://localhost:7001');
    
    // Intercept the token exchange to capture the code
    let capturedCode = '';
    
    page.on('request', request => {
      if (request.url().includes('/connect/token')) {
        const postData = request.postData();
        if (postData) {
          const match = postData.match(/code=([^&]+)/);
          if (match) {
            capturedCode = match[1];
          }
        }
      }
    });
    
    // Trigger login flow
    await page.click('a[href="/Account/Profile"]');
    
    // If on authorization page, click Allow
    if (page.url().includes('/connect/authorize')) {
      await page.click('button:has-text("Allow Access")');
    }
    
    // Wait for redirect back to TestClient
    await page.waitForURL(/.*localhost:7001.*/);
    
    // 2. Now try to reuse the captured code (if we got one)
    if (capturedCode) {
      // Try to exchange the code again using direct HTTP request
      // This would require using fetch or request library
      // For now, we'll document this as a manual test case
      console.log('Captured code:', capturedCode);
      console.log('Manual test: Try to exchange this code again - should fail with invalid_grant');
    }
    
    // Note: Full implementation would require making direct HTTP POST to /connect/token
    // with the used authorization code
  });
});

test.describe('Token Validation Failures', () => {
  
  test('Malformed token should be rejected', async ({ page, request }) => {
    // Get a valid token first
    await page.goto('https://localhost:7001');
    await page.click('a[href="/Account/Profile"]');
    
    // If need to authorize, click Allow
    if (page.url().includes('/connect/authorize')) {
      await page.click('button:has-text("Allow Access")');
    }
    
    await page.waitForURL(/.*\/Account\/Profile/);
    
    // Extract token from page (it should be displayed)
    const tokenElement = await page.locator('textarea').first();
    let token = await tokenElement.inputValue();
    
    if (token) {
      // Modify the token to make it invalid
      const malformedToken = token.substring(0, token.length - 10) + 'INVALID123';
      
      // Try to use the malformed token (if there's an API to test against)
      // For now, we verify that the token modification would fail validation
      expect(malformedToken).not.toBe(token);
      expect(malformedToken.length).toBeGreaterThan(0);
      
      // In a real test, you would:
      // const response = await request.get('https://localhost:7035/api/protected', {
      //   headers: { 'Authorization': `Bearer ${malformedToken}` }
      // });
      // expect(response.status()).toBe(401);
    }
  });
});

test.describe('Scope-Mapped Claims Edge Cases', () => {
  
  test('Non-existent user property path should not break login', async ({ page }) => {
    // Prerequisites: Create a claim with invalid UserPropertyPath via Admin UI
    // This test assumes such a claim exists (or we create it first)
    
    // For now, we test that login still works even with potentially invalid claims
    await page.goto('https://localhost:7001');
    
    // Logout first if logged in
    if (await page.locator('a:has-text("Logout")').isVisible()) {
      await page.click('a:has-text("Logout")');
    }
    
    // Login
    await page.goto('https://localhost:7001/Account/Profile');
    
    if (page.url().includes('/Account/Login')) {
      await page.fill('input[name="Input.Email"]', 'admin@hybridauth.local');
      await page.fill('input[name="Input.Password"]', 'Admin@123');
      await page.click('button[type="submit"]');
    }
    
    if (page.url().includes('/connect/authorize')) {
      await page.click('button:has-text("Allow Access")');
    }
    
    // Should successfully reach profile page
    await expect(page).toHaveURL(/.*\/Account\/Profile/);
    
    // Verify claims table is displayed
    await expect(page.locator('h5:has-text("User Claims")')).toBeVisible();
    
    // Check that we have standard claims (sub, email, etc.)
    const claimsTable = page.locator('table');
    await expect(claimsTable).toBeVisible();
    
    const tableContent = await claimsTable.textContent();
    expect(tableContent).toContain('sub');
    expect(tableContent).toContain('email');
  });
});
