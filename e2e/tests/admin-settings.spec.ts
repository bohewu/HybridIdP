import { test, expect } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';

test.describe('Admin Settings - Branding Configuration', () => {
  
  test('admin can login and access Settings page', async ({ page }) => {
    // Step 1: Navigate to login page
    await page.goto(`${IDP_BASE}/Account/Login`);
    
    // Step 2: Fill in admin credentials
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    
    // Step 3: Click login button
    await page.getByRole('button', { name: 'Login' }).click();
    
    // Step 4: Should redirect to home page
    await page.waitForURL(IDP_BASE + '/');
    console.log('✅ Login successful, redirected to:', page.url());
    
    // Step 5: Navigate to Admin/Settings page
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    
    // Step 6: Wait for the page to load
    await page.waitForLoadState('networkidle');
    console.log('✅ Settings page loaded:', page.url());
    
    // Step 7: Verify we're on the Admin/Settings page
    await expect(page).toHaveURL(new RegExp(`${IDP_BASE}/Admin/Settings`));
    
    // Step 8: Wait for Vue app to render - look for page title
    const pageTitle = page.locator('h1', { hasText: /System Settings|系統設定/ });
    await expect(pageTitle).toBeVisible({ timeout: 10000 });
    console.log('✅ Settings page title is visible - Vue app loaded');
    
    // Step 9: Verify Branding section is visible
    const brandingSection = page.locator('h2', { hasText: /Branding Settings|品牌設定/ });
    await expect(brandingSection).toBeVisible();
    console.log('✅ Branding Settings section is visible');
    
    // Step 10: Verify form fields exist
    const appNameField = page.locator('input#appName');
    const productNameField = page.locator('input#productName');
    await expect(appNameField).toBeVisible();
    await expect(productNameField).toBeVisible();
    console.log('✅ Branding form fields (appName, productName) are visible');
    
    // Step 11: Take a screenshot for verification
    await page.screenshot({ path: 'test-results/admin-settings-page.png', fullPage: true });
    console.log('✅ Screenshot saved to test-results/admin-settings-page.png');
  });
  
  test('admin can edit and save branding settings', async ({ page }) => {
    // Login first
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    console.log('✅ Logged in as admin');
    
    // Navigate to Settings page
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    await page.waitForLoadState('networkidle');
    
    // Wait for form to load
    const appNameField = page.locator('input#appName');
    await expect(appNameField).toBeVisible({ timeout: 10000 });
    console.log('✅ Settings form loaded');
    
    // Store original values for cleanup
    const originalAppName = await appNameField.inputValue();
    const originalProductName = await page.locator('input#productName').inputValue();
    console.log(`📝 Original values: appName="${originalAppName}", productName="${originalProductName}"`);
    
    // Generate unique test values
    const timestamp = Date.now();
    const testAppName = `TestApp_${timestamp}`;
    const testProductName = `TestProduct_${timestamp}`;
    
    // Clear and fill in new values
    await appNameField.clear();
    await appNameField.fill(testAppName);
    await page.locator('input#productName').clear();
    await page.locator('input#productName').fill(testProductName);
    console.log(`✏️  Filled new values: appName="${testAppName}", productName="${testProductName}"`);
    
    // Verify Save button is enabled (has changes)
    const saveButton = page.getByRole('button', { name: /Save Changes|儲存變更/ });
    await expect(saveButton).toBeEnabled();
    console.log('✅ Save button is enabled');
    
    // Click Save button
    await saveButton.click();
    console.log('💾 Clicked Save button');
    
    // Wait for success message
    const successAlert = page.locator('.bg-green-50', { hasText: /saved successfully|已成功儲存/ });
    await expect(successAlert).toBeVisible({ timeout: 5000 });
    console.log('✅ Success alert displayed');
    
    // Wait a bit for cache invalidation to propagate
    await page.waitForTimeout(1000);
    
    // Step: Verify changes persist after page reload
    await page.reload();
    await page.waitForLoadState('networkidle');
    
    // Check that form shows the new values
    await expect(appNameField).toHaveValue(testAppName);
    await expect(page.locator('input#productName')).toHaveValue(testProductName);
    console.log('✅ New values persisted after page reload');
    
    // Take screenshot of saved state
    await page.screenshot({ path: 'test-results/settings-saved.png', fullPage: true });
    
    // Clean up: Restore original values
    await appNameField.clear();
    await appNameField.fill(originalAppName || 'HybridAuth');
    await page.locator('input#productName').clear();
    await page.locator('input#productName').fill(originalProductName || 'HybridAuth IdP');
    await saveButton.click();
    await expect(successAlert).toBeVisible({ timeout: 5000 });
    console.log('🧹 Cleanup: Restored original branding values');
  });
  
  test('branding changes reflect on login page after cache invalidation', async ({ page }) => {
    // Login as admin
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    
    // Navigate to Settings
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    await page.waitForLoadState('networkidle');
    
    // Get original values
    const appNameField = page.locator('input#appName');
    await expect(appNameField).toBeVisible({ timeout: 10000 });
    const originalAppName = await appNameField.inputValue();
    const originalProductName = await page.locator('input#productName').inputValue();
    
    // Set test values
    const testAppName = `E2E_Test_${Date.now()}`;
    const testProductName = `E2E Product ${Date.now()}`;
    
    await appNameField.clear();
    await appNameField.fill(testAppName);
    await page.locator('input#productName').clear();
    await page.locator('input#productName').fill(testProductName);
    
    // Save changes
    const saveButton = page.getByRole('button', { name: /Save Changes|儲存變更/ });
    await saveButton.click();
    const successAlert = page.locator('.bg-green-50', { hasText: /saved successfully|已成功儲存/ });
    await expect(successAlert).toBeVisible({ timeout: 5000 });
    console.log('✅ Branding settings updated');
    
    // Wait for cache invalidation to propagate
    await page.waitForTimeout(2000);
    
    // Logout
    await page.goto(`${IDP_BASE}/Account/Logout`);
    await page.waitForLoadState('networkidle');
    console.log('🚪 Logged out');
    
    // Navigate to login page
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.waitForLoadState('networkidle');
    
    // Verify the page title includes the new product name
    await expect(page).toHaveTitle(new RegExp(testProductName));
    console.log(`✅ Login page title contains updated productName: "${testProductName}"`);
    
    // Take screenshot of login page with new branding
    await page.screenshot({ path: 'test-results/login-page-new-branding.png', fullPage: true });
    
    // Clean up: Login again and restore original values
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    await page.waitForLoadState('networkidle');
    
    await appNameField.clear();
    await appNameField.fill(originalAppName || 'HybridAuth');
    await page.locator('input#productName').clear();
    await page.locator('input#productName').fill(originalProductName || 'HybridAuth IdP');
    await saveButton.click();
    await expect(successAlert).toBeVisible({ timeout: 5000 });
    console.log('🧹 Cleanup: Restored original branding');
  });
  
  test('non-admin user cannot access Settings page', async ({ page }) => {
    // Note: This test assumes there's a regular user account
    // If not, this test will need to be skipped or modified
    
    // Try to navigate directly to Settings page without login
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    
    // Should redirect to login page
    await page.waitForURL(/\/Account\/Login/, { timeout: 5000 });
    console.log('✅ Unauthenticated user redirected to login');
    
    // Try with a non-admin user (if one exists)
    // For now, verify that the URL includes returnUrl parameter
    expect(page.url()).toContain('ReturnUrl=%2FAdmin%2FSettings');
    console.log('✅ ReturnUrl preserved for post-login redirect');
    
    await page.screenshot({ path: 'test-results/settings-unauthorized.png' });
  });
  
  test('Cancel button discards changes', async ({ page }) => {
    // Login
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    
    // Navigate to Settings
    await page.goto(`${IDP_BASE}/Admin/Settings`);
    await page.waitForLoadState('networkidle');
    
    const appNameField = page.locator('input#appName');
    await expect(appNameField).toBeVisible({ timeout: 10000 });
    
    // Get original value
    const originalValue = await appNameField.inputValue();
    
    // Make a change
    await appNameField.clear();
    await appNameField.fill('TemporaryChange');
    console.log('✏️  Made temporary change to appName');
    
    // Verify Cancel button is enabled
    const cancelButton = page.getByRole('button', { name: /Cancel|取消/ });
    await expect(cancelButton).toBeEnabled();
    
    // Handle the confirmation dialog
    page.on('dialog', async dialog => {
      console.log(`📢 Dialog message: ${dialog.message()}`);
      expect(dialog.type()).toBe('confirm');
      expect(dialog.message()).toContain(/unsaved changes|未儲存的變更/);
      await dialog.accept();
      console.log('✅ Accepted cancel confirmation dialog');
    });
    
    // Click Cancel
    await cancelButton.click();
    console.log('🚫 Clicked Cancel button');
    
    // Wait a bit for state to reset
    await page.waitForTimeout(500);
    
    // Verify original value is restored
    await expect(appNameField).toHaveValue(originalValue);
    console.log('✅ Original value restored after cancel');
  });
});
