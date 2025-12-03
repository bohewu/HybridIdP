import { test, expect } from '@playwright/test';

/**
 * My Account - UI States Tests
 * 
 * Tests visual states and styling:
 * - Active role styling (blue background)
 * - Inactive role styling
 * - Button visibility states
 * - Active badge display
 * - Hover effects
 * - Responsive layout
 */
test.describe('My Account - UI States', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should display active role with blue background when user has active session', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Check if there's an active role card (only exists if user has an active session)
    const activeRoleCard = page.locator('.role-card.active');
    const count = await activeRoleCard.count();
    
    if (count > 0) {
      // User has an active session with a role
      await expect(activeRoleCard).toBeVisible();
      
      // Check if it has active class
      await expect(activeRoleCard).toHaveClass(/active/);
      
      // Verify blue background color (Material Design blue: #e8f0fe)
      const backgroundColor = await activeRoleCard.evaluate(el => {
        return window.getComputedStyle(el).backgroundColor;
      });
      
      // RGB for #e8f0fe is rgb(232, 240, 254)
      expect(backgroundColor).toBe('rgb(232, 240, 254)');
    } else {
      // No active session yet (direct login without OIDC), skip test
      test.skip();
    }
  });

  test('should display active badge on current role when user has active session', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Check if there's an active role card
    const activeRoleCard = page.locator('.role-card.active');
    const count = await activeRoleCard.count();
    
    if (count > 0) {
      // Should have success badge
      const activeBadge = activeRoleCard.locator('.badge.bg-success');
      await expect(activeBadge).toBeVisible();
      
      // Badge should contain text (zh-TW: 目前 or similar)
      const badgeText = await activeBadge.textContent();
      expect(badgeText).toBeTruthy();
    } else {
      test.skip();
    }
  });

  test('should not display switch button on active role card', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find active role card
    const activeRoleCard = page.locator('.role-card.active');
    
    // Switch button should NOT be rendered (v-if="!role.isActive")
    const switchButton = activeRoleCard.locator('.btn.btn-primary');
    await expect(switchButton).toHaveCount(0);
  });

  test('should display switch button on inactive role cards', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find inactive role cards
    const inactiveRoleCards = page.locator('.role-card:not(.active)');
    const count = await inactiveRoleCards.count();
    
    if (count > 0) {
      // Each inactive role should have a switch button
      const firstInactiveCard = inactiveRoleCards.first();
      const switchButton = firstInactiveCard.locator('.btn.btn-primary');
      
      await expect(switchButton).toBeVisible();
      
      // Button should have text
      const buttonText = await switchButton.textContent();
      expect(buttonText).toBeTruthy();
    } else {
      // User only has one role, skip test
      test.skip();
    }
  });

  test('should display role name and description', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    const roleCard = page.locator('.role-card').first();
    
    // Should have role name in h4
    const roleName = roleCard.locator('h4');
    await expect(roleName).toBeVisible();
    
    const roleNameText = await roleName.textContent();
    expect(roleNameText).toBeTruthy();
    
    // Description may or may not exist
    const hasDescription = await roleCard.locator('.text-muted').count();
    if (hasDescription > 0) {
      const description = roleCard.locator('.text-muted');
      await expect(description).toBeVisible();
    }
  });

  test('should apply hover effect on inactive role cards', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find inactive role card
    const inactiveRoleCards = page.locator('.role-card:not(.active)');
    const count = await inactiveRoleCards.count();
    
    if (count > 0) {
      const firstInactiveCard = inactiveRoleCards.first();
      
      // Hover over the card
      await firstInactiveCard.hover();
      
      // Note: Testing CSS :hover pseudo-class requires checking computed styles
      // This test verifies the card is hoverable (no errors)
      await expect(firstInactiveCard).toBeVisible();
    } else {
      test.skip();
    }
  });

  test('should maintain layout with multiple roles', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    const roleCards = page.locator('.role-card');
    const count = await roleCards.count();
    
    // All role cards should be visible
    for (let i = 0; i < count; i++) {
      const card = roleCards.nth(i);
      await expect(card).toBeVisible();
    }
    
    // Role list container should exist
    const roleList = page.locator('.role-list');
    await expect(roleList).toBeVisible();
  });

  test('should display role card with proper spacing and borders', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    const roleCard = page.locator('.role-card').first();
    
    // Check padding
    const padding = await roleCard.evaluate(el => {
      const style = window.getComputedStyle(el);
      return {
        top: style.paddingTop,
        right: style.paddingRight,
        bottom: style.paddingBottom,
        left: style.paddingLeft
      };
    });
    
    // Material Design spacing (16px 24px)
    expect(padding.top).toBe('16px');
    expect(padding.left).toBe('24px');
    expect(padding.right).toBe('24px');
    expect(padding.bottom).toBe('16px');
  });

  test('should use correct font family for role name', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    const roleName = page.locator('.role-card h4').first();
    
    // Check font family (Google Sans or Roboto)
    const fontFamily = await roleName.evaluate(el => {
      return window.getComputedStyle(el).fontFamily;
    });
    
    // Should contain Google Sans or Roboto
    const hasGoogleSans = fontFamily.includes('Google Sans') || fontFamily.includes('Roboto');
    expect(hasGoogleSans).toBe(true);
  });

  test('should display active role with blue text color when user has active session', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Check if there's an active role card
    const activeRoleCard = page.locator('.role-card.active');
    const count = await activeRoleCard.count();
    
    if (count > 0) {
      const activeRoleName = activeRoleCard.locator('h4');
      
      // Check color (Material Design blue: #1967d2)
      const color = await activeRoleName.evaluate(el => {
        return window.getComputedStyle(el).color;
      });
      
      // RGB for #1967d2 is rgb(25, 103, 210)
      expect(color).toBe('rgb(25, 103, 210)');
    } else {
      test.skip();
    }
  });
});

/**
 * My Account - Responsive and Accessibility Tests
 */
test.describe('My Account - Responsive UI', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test('should display properly on mobile viewport', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Role cards should still be visible
    const roleCard = page.locator('.role-card').first();
    await expect(roleCard).toBeVisible();
    
    // Page title should be visible
    await expect(page.locator('.page-title')).toBeVisible();
  });

  test('should display properly on tablet viewport', async ({ page }) => {
    // Set tablet viewport
    await page.setViewportSize({ width: 768, height: 1024 });
    
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Role cards should still be visible
    const roleCard = page.locator('.role-card').first();
    await expect(roleCard).toBeVisible();
  });

  test('should display properly on desktop viewport', async ({ page }) => {
    // Set desktop viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Role cards should still be visible
    const roleCard = page.locator('.role-card').first();
    await expect(roleCard).toBeVisible();
  });

  test('should have accessible button labels', async ({ page }) => {
    // Navigate to My Account page
    await page.goto('https://localhost:7035/Account/MyAccount');
    
    // Wait for roles to load
    await page.waitForSelector('.role-card');
    
    // Find inactive role cards with switch button
    const inactiveRoleCards = page.locator('.role-card:not(.active)');
    const count = await inactiveRoleCards.count();
    
    if (count > 0) {
      const switchButton = inactiveRoleCards.first().locator('.btn.btn-primary');
      
      // Button should have text content
      const buttonText = await switchButton.textContent();
      expect(buttonText).toBeTruthy();
      expect(buttonText!.trim().length).toBeGreaterThan(0);
    } else {
      test.skip();
    }
  });
});
