import { test, expect } from '@playwright/test';

/**
 * Profile Management E2E Tests
 * 
 * Tests the profile page UI including:
 * - Account information display
 * - Edit Profile form visibility
 * - Password change form
 * - Responsive layout (side-by-side forms on wide screens)
 */
test.describe('Profile Management', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test.describe('Profile Page Layout', () => {
    test('should display ProfileInfoCard with account information', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Profile');
      
      // Wait for Vue app to mount
      await page.waitForSelector('#profile-app', { timeout: 15000 });
      
      // Verify ProfileInfoCard is visible
      await expect(page.locator('[data-testid="profile-info-card"]')).toBeVisible();
      
      // Verify email is displayed
      await expect(page.locator('text=admin@hybridauth.local').first()).toBeVisible();
    });

    test('should display EditProfileForm for user with linked Person', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Profile');
      await page.waitForSelector('#profile-app', { timeout: 15000 });
      
      // Admin user has linked Person, so Edit Profile form should be visible
      await expect(page.locator('[data-testid="edit-profile-form"]')).toBeVisible();
      
      // Verify phone number input exists
      const phoneInput = page.locator('input[type="tel"]');
      await expect(phoneInput.first()).toBeVisible();
    });

    test('should display ChangePasswordForm', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Profile');
      await page.waitForSelector('#profile-app', { timeout: 15000 });
      
      // Verify ChangePasswordForm is visible
      await expect(page.locator('[data-testid="change-password-form"]')).toBeVisible();
    });

    test('should have side-by-side layout on wide screens', async ({ page }) => {
      // Set wide viewport
      await page.setViewportSize({ width: 1280, height: 800 });
      
      await page.goto('https://localhost:7035/Account/Profile');
      await page.waitForSelector('#profile-app', { timeout: 15000 });
      
      // Wait for forms to be visible
      const editForm = page.locator('[data-testid="edit-profile-form"]');
      const passwordForm = page.locator('[data-testid="change-password-form"]');
      
      await expect(editForm).toBeVisible();
      await expect(passwordForm).toBeVisible();
      
      // Verify side-by-side layout (forms at roughly same Y position)
      const editFormBox = await editForm.boundingBox();
      const passwordFormBox = await passwordForm.boundingBox();
      
      if (editFormBox && passwordFormBox) {
        const verticalDiff = Math.abs(editFormBox.y - passwordFormBox.y);
        expect(verticalDiff).toBeLessThan(100); // Side-by-side means similar Y position
      }
    });
  });

  test.describe('Profile API', () => {
    test('should return correct data structure', async ({ page }) => {
      // Capture API response when navigating to profile
      const profileResponsePromise = page.waitForResponse(
        response => response.url().includes('/api/profile') && response.request().method() === 'GET',
        { timeout: 15000 }
      );
      
      await page.goto('https://localhost:7035/Account/Profile');
      const profileResponse = await profileResponsePromise;
      
      expect(profileResponse.status()).toBe(200);
      
      const profileData = await profileResponse.json();
      expect(profileData).toHaveProperty('userId');
      expect(profileData).toHaveProperty('userName');
      expect(profileData).toHaveProperty('hasLocalPassword');
      expect(profileData).toHaveProperty('allowPasswordChange');
      expect(profileData.hasLocalPassword).toBe(true);
    });
  });
});
