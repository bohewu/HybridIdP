import { test, expect } from '@playwright/test';

test.describe('System Settings - Monitoring', () => {
    
  test.beforeEach(async ({ page }) => {
    // Login flow
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name'); // Wait for login to complete

    // Go to settings
    await page.goto('/admin/settings');
  });

  test('should display system monitoring settings', async ({ page }) => {
    // Check if the system monitoring section is visible
    // Uses the i18n key "admin.settings.system.title" -> "System Monitoring" (en-US)
    await expect(page.getByText('System Monitoring')).toBeVisible();
    await expect(page.getByText('Enable Monitoring')).toBeVisible();
    
    // Check interval fields
    // "Activity Interval"
    await expect(page.getByText('Activity Interval')).toBeVisible();
    await expect(page.getByText('Security Interval')).toBeVisible();
    await expect(page.getByText('Metrics Interval')).toBeVisible();
  });

  test('should verify intervals values are numbers', async ({ page }) => {
     // Wait for any loading spinner to disappear
     await expect(page.locator('.loading-spinner')).not.toBeVisible();

     // Check input type
     // We have 3 inputs for intervals, they should be type="number"
     const activityInput = page.locator('input[type="number"]').first();
     await expect(activityInput).toBeVisible();
  });
});
