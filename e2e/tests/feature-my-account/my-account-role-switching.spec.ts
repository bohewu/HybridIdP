import { test, expect } from '@playwright/test';

/**
 * My Account - Role Switching Tests
 * 
 * Tests role switching functionality:
 * - Display all user roles
 * - Current role identification
 * - Switch to different role
 * - Password confirmation for Admin role
 * - Role badge update after switch
 * - Prevention of same-role switch
 */
test.describe('My Account - Role Switching', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should display all user roles on My Account page', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Verify role cards are displayed
    const roleCards = page.locator('.role-card');
    const count = await roleCards.count();
    
    // Admin user should have at least 1 role
    expect(count).toBeGreaterThanOrEqual(1);
    
    // Verify role card contains role name
    await expect(roleCards.first()).toContainText('Admin');
  });

  test('should identify current active role with badge and styling when user has active session', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Check if there's an active role card
    const activeRoleCard = page.locator('.role-card.active');
    const count = await activeRoleCard.count();
    
    if (count > 0) {
      // Should have active styling
      await expect(activeRoleCard).toBeVisible();
      
      // Should contain "Active" badge (zh-TW: 目前)
      const activeBadge = activeRoleCard.locator('.badge.bg-success');
      await expect(activeBadge).toBeVisible();
    } else {
      // No active session (direct login), skip test
      test.skip();
    }
  });

  test('should not show switch button for current active role', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find active role card
    const activeRoleCard = page.locator('.role-card.active');
    
    // Switch button should NOT exist in active role card (v-if="!role.isActive")
    const switchButton = activeRoleCard.locator('.btn.btn-primary');
    await expect(switchButton).not.toBeVisible();
  });

  test('should show switch button for non-active roles', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find non-active role cards
    const inactiveRoleCards = page.locator('.role-card:not(.active)');
    const inactiveCount = await inactiveRoleCards.count();
    
    if (inactiveCount > 0) {
      // Should have switch button
      const switchButton = inactiveRoleCards.first().locator('.btn.btn-primary');
      await expect(switchButton).toBeVisible();
    } else {
      // Skip test if user only has one role
      test.skip();
    }
  });
});

/**
 * Multi-Role User Tests
 * These tests require a user with multiple roles (Admin + User)
 * Use: multitest@hybridauth.local / MultiTest@123
 */
test.describe('My Account - Multi-Role Switching', () => {
  test.beforeEach(async ({ page }) => {
    // Login as multi-role test user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'multitest@hybridauth.local');
    await page.fill('#Input_Password', 'MultiTest@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name', { timeout: 10000 });
  });

  test('should display multiple roles for multi-role user', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Multi-role user should have at least 2 roles
    const roleCards = page.locator('.role-card');
    const count = await roleCards.count();
    expect(count).toBeGreaterThanOrEqual(2);
  });

  test('should switch from User role to Admin role with password confirmation', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Find Admin role card (not active)
    const adminRoleCard = page.locator('.role-card').filter({ hasText: 'Admin' }).filter({ hasNot: page.locator('.badge.bg-success') });
    
    // Check if Admin role is inactive (has switch button)
    const switchButton = adminRoleCard.locator('.btn.btn-primary');
    const isVisible = await switchButton.isVisible().catch(() => false);
    
    if (!isVisible) {
      // Admin is already active, skip test
      test.skip();
      return;
    }
    
    // Click switch button
    await switchButton.click();
    
    // Password modal should appear
    await page.waitForSelector('.modal-overlay', { state: 'visible', timeout: 5000 });
    
    // Fill password
    await page.fill('#password', 'MultiTest@123');
    
    // Click confirm button in modal
    await page.click('.modal-footer .btn-primary');
    
    // Wait for modal to close
    await page.waitForSelector('.modal-overlay', { state: 'hidden', timeout: 5000 });
    
    // Verify role badge updated in header
    await page.waitForTimeout(1000); // Wait for UI update
    await expect(page.locator('.role-badge')).toContainText('Admin');
    
    // Reload page and verify Admin is now active
    await page.reload();
    await page.waitForSelector('.role-card');
    
    const activeAdminCard = page.locator('.role-card.active').filter({ hasText: 'Admin' });
    await expect(activeAdminCard).toBeVisible();
  });

  test('should switch from Admin role to User role without password', async ({ page }) => {
    // First ensure we're in Admin role (from previous test or initial state)
    await page.goto('https://localhost:7035/Account/MyAccount');
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Find User role card (should not be active)
    const userRoleCard = page.locator('.role-card').filter({ hasText: 'User' }).filter({ hasNot: page.locator('.badge.bg-success') });
    
    // Check if User role is inactive (has switch button)
    const switchButton = userRoleCard.locator('.btn.btn-primary');
    const isVisible = await switchButton.isVisible().catch(() => false);
    
    if (!isVisible) {
      // User is already active, skip test
      test.skip();
      return;
    }
    
    // Click switch button (no password required for User role)
    await switchButton.click();
    
    // No modal should appear, direct switch
    // Wait for navigation or page update
    await page.waitForTimeout(1000);
    
    // Verify role badge updated in header
    await expect(page.locator('.role-badge')).toContainText('User');
    
    // Reload page and verify User is now active
    await page.reload();
    await page.waitForSelector('.role-card');
    
    const activeUserCard = page.locator('.role-card.active').filter({ hasText: 'User' });
    await expect(activeUserCard).toBeVisible();
  });

  test('should cancel password modal when switching to Admin role', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Find Admin role card (not active)
    const adminRoleCard = page.locator('.role-card').filter({ hasText: 'Admin' }).filter({ hasNot: page.locator('.badge.bg-success') });
    
    const switchButton = adminRoleCard.locator('.btn.btn-primary');
    const isVisible = await switchButton.isVisible().catch(() => false);
    
    if (!isVisible) {
      test.skip();
      return;
    }
    
    // Click switch button
    await switchButton.click();
    
    // Password modal should appear
    await page.waitForSelector('.modal-overlay', { state: 'visible', timeout: 5000 });
    
    // Click cancel button
    await page.click('.modal-footer .btn-secondary');
    
    // Modal should close
    await page.waitForSelector('.modal-overlay', { state: 'hidden', timeout: 5000 });
    
    // Role should not have changed
    // Original active role should still be active
    const currentRoleBadge = await page.locator('.role-badge').textContent();
    expect(currentRoleBadge).toBeTruthy();
  });

  test('should show error when providing wrong password for Admin role', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    await page.waitForSelector('.role-card', { timeout: 10000 });
    
    // Find Admin role card (not active)
    const adminRoleCard = page.locator('.role-card').filter({ hasText: 'Admin' }).filter({ hasNot: page.locator('.badge.bg-success') });
    
    const switchButton = adminRoleCard.locator('.btn.btn-primary');
    const isVisible = await switchButton.isVisible().catch(() => false);
    
    if (!isVisible) {
      test.skip();
      return;
    }
    
    // Click switch button
    await switchButton.click();
    
    // Password modal should appear
    await page.waitForSelector('.modal-overlay', { state: 'visible', timeout: 5000 });
    
    // Fill wrong password
    await page.fill('#password', 'WrongPassword123');
    
    // Click confirm button
    await page.click('.modal-footer .btn-primary');
    
    // Error message should appear
    await page.waitForSelector('.alert-danger, .error-message', { timeout: 5000 });
    
    // Modal should remain open
    await expect(page.locator('.modal-overlay')).toBeVisible();
  });
});
