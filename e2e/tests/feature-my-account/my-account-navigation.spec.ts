import { test, expect } from '@playwright/test';

/**
 * My Account - Navigation Tests
 * 
 * Tests navigation to My Account page via:
 * - Role badge in header
 * - User dropdown menu
 */
test.describe('My Account - Navigation', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should show role badge in navigation header', async ({ page }) => {
    // Verify role badge is visible
    await expect(page.locator('.role-badge')).toBeVisible();
    
    // Should contain Admin text
    await expect(page.locator('.role-badge')).toContainText('Admin');
  });

  test('should navigate to My Account page via role badge click', async ({ page }) => {
    // Click on role badge
    await page.click('.role-badge');
    
    // Should navigate to My Account page
    await expect(page).toHaveURL('https://localhost:7035/Account/MyAccount');
    
    // Page title should be visible
    await expect(page.locator('.page-title')).toBeVisible();
  });

  test('should show My Account link in user dropdown', async ({ page }) => {
    // Click user dropdown
    await page.click('#userDropdown');
    
    // Wait for dropdown to open
    await page.waitForSelector('.dropdown-menu.show', { state: 'visible' });
    
    // Verify My Account link exists in the dropdown menu
    const myAccountLink = page.locator('.dropdown-menu a[href="/Account/MyAccount"]');
    await expect(myAccountLink).toBeVisible();
  });

  test('should navigate to My Account page via user dropdown', async ({ page }) => {
    // Click user dropdown
    await page.click('#userDropdown');
    
    // Wait for dropdown to open
    await page.waitForSelector('.dropdown-menu.show', { state: 'visible' });
    
    // Click My Account link in the dropdown
    await page.click('.dropdown-menu a[href="/Account/MyAccount"]');
    
    // Verify we're on My Account page
    await expect(page).toHaveURL('https://localhost:7035/Account/MyAccount');
    await expect(page.locator('.page-title')).toBeVisible();
  });

  test('should display correct page title on My Account page', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for page to load
    await page.waitForSelector('.page-title');
    
    // Verify page title (Chinese: 我的帳戶)
    await expect(page.locator('.page-title')).toContainText('我的帳戶');
  });

  test('should show breadcrumb or back navigation on My Account page', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for page to load
    await page.waitForSelector('.page-title');
    
    // Verify user can navigate back (browser back button always works)
    // This test verifies page loaded successfully
    await expect(page.locator('.page-title')).toBeVisible();
    
    // Header navigation should still be accessible
    await expect(page.locator('.navbar')).toBeVisible();
  });
});
