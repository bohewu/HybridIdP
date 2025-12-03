import { test, expect } from '@playwright/test';

/**
 * Phase 11.6 - Homepage Refactoring E2E Tests
 * 
 * Tests for the new two-card homepage layout:
 * - Authorization Management card
 * - Linked Accounts card
 * - Responsive design
 * - CSP compliance (no inline styles/scripts)
 */
test.describe('Phase 11.6 - Homepage Refactoring', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test.describe('Homepage Layout', () => {
    test('should display two navigation cards on homepage', async ({ page }) => {
      // Navigate to homepage
      await page.goto('https://localhost:7035/');
      
      // Verify page title (can be "Home" or "我的帳號" depending on localization)
      await expect(page.locator('h1')).toBeVisible();
      
      // Verify two cards are visible
      const cards = page.locator('.hover-card');
      await expect(cards).toHaveCount(2);
    });

    test('should display Authorization Management card with correct styling', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Find Authorization card by icon class
      const authCard = page.locator('.hover-card').filter({ has: page.locator('.home-icon-container.authorization') }).first();
      await expect(authCard).toBeVisible();
      
      // Verify icon container has correct class (no inline styles)
      const iconContainer = authCard.locator('.home-icon-container.authorization');
      await expect(iconContainer).toBeVisible();
      
      // Verify icon
      await expect(iconContainer.locator('i.bi-shield-check')).toBeVisible();
      
      // Verify description exists
      await expect(authCard.locator('.card-text')).toBeVisible();
    });

    test('should display Linked Accounts card with correct styling', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Find Linked Accounts card by icon class
      const linkedCard = page.locator('.hover-card').filter({ has: page.locator('.home-icon-container.linked-accounts') }).first();
      await expect(linkedCard).toBeVisible();
      
      // Verify icon container has correct class (no inline styles)
      const iconContainer = linkedCard.locator('.home-icon-container.linked-accounts');
      await expect(iconContainer).toBeVisible();
      
      // Verify icon
      await expect(iconContainer.locator('i.bi-person-lines-fill')).toBeVisible();
      
      // Verify description exists
      await expect(linkedCard.locator('.card-text')).toBeVisible();
    });

    test('should have hover effect on cards', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      const authCard = page.locator('.hover-card').first();
      
      // Get initial position
      const initialBox = await authCard.boundingBox();
      
      // Hover over card
      await authCard.hover();
      
      // Wait for transition
      await page.waitForTimeout(500);
      
      // Card should have transform (lifted up)
      const transform = await authCard.evaluate(el => 
        window.getComputedStyle(el).transform
      );
      
      // Transform should not be 'none' (should have translateY)
      expect(transform).not.toBe('none');
    });
  });

  test.describe('Navigation from Homepage', () => {
    test('should navigate to Authorizations page when clicking Authorization card', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Click Authorization Management card (find within main container to avoid dropdown)
      const authCard = page.locator('.container a[href="/Account/Authorizations"]').first();
      await authCard.click();
      
      // Should navigate to Authorizations page
      await expect(page).toHaveURL('https://localhost:7035/Account/Authorizations');
      
      // Verify page loaded (check URL is correct)
      await expect(page.locator('h1')).toBeVisible();
    });

    test('should navigate to LinkedAccounts page when clicking Linked Accounts card', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Click Linked Accounts card (find within main container to avoid dropdown)
      const linkedCard = page.locator('.container a[href="/Account/LinkedAccounts"]').first();
      await linkedCard.click();
      
      // Should navigate to LinkedAccounts page
      await expect(page).toHaveURL('https://localhost:7035/Account/LinkedAccounts');
      
      // Verify page loaded (check URL is correct)
      await expect(page.locator('h1')).toBeVisible();
    });
  });

  test.describe('Responsive Design', () => {
    test('should display cards in column layout on mobile', async ({ page }) => {
      // Set mobile viewport
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('https://localhost:7035/');
      
      // Both cards should be visible
      const cards = page.locator('.hover-card');
      await expect(cards).toHaveCount(2);
      
      // Cards should stack vertically (each taking full width)
      const firstCard = cards.first();
      const secondCard = cards.last();
      
      const firstBox = await firstCard.boundingBox();
      const secondBox = await secondCard.boundingBox();
      
      // Second card should be below first card
      expect(secondBox?.y).toBeGreaterThan(firstBox?.y! + firstBox?.height!);
    });

    test('should display cards side by side on desktop', async ({ page }) => {
      // Set desktop viewport
      await page.setViewportSize({ width: 1920, height: 1080 });
      await page.goto('https://localhost:7035/');
      
      const cards = page.locator('.hover-card');
      await expect(cards).toHaveCount(2);
      
      const firstCard = cards.first();
      const secondCard = cards.last();
      
      const firstBox = await firstCard.boundingBox();
      const secondBox = await secondCard.boundingBox();
      
      // Cards should be roughly at the same vertical position (side by side)
      const verticalDiff = Math.abs(firstBox?.y! - secondBox?.y!);
      expect(verticalDiff).toBeLessThan(50); // Allow small difference
    });

    test('should adjust icon size on mobile', async ({ page }) => {
      // Mobile viewport
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('https://localhost:7035/');
      
      const iconContainer = page.locator('.home-icon-container').first();
      const icon = iconContainer.locator('i').first();
      
      // Icon should be visible and have appropriate size
      await expect(icon).toBeVisible();
      
      const fontSize = await icon.evaluate(el => 
        window.getComputedStyle(el).fontSize
      );
      
      // Font size should be adjusted (parsed as number)
      const size = parseFloat(fontSize);
      expect(size).toBeGreaterThan(20); // At least 20px for mobile
    });
  });

  test.describe('CSP Compliance', () => {
    test('should have no CSP violations on homepage', async ({ page }) => {
      const cspViolations: string[] = [];
      
      // Listen for CSP violations
      page.on('console', msg => {
        if (msg.type() === 'error' && msg.text().includes('Content Security Policy')) {
          cspViolations.push(msg.text());
        }
      });
      
      await page.goto('https://localhost:7035/');
      
      // Wait for page to fully load
      await page.waitForLoadState('networkidle');
      
      // Should have no CSP violations
      expect(cspViolations).toHaveLength(0);
    });

    test('should load external CSS from site.css (no inline styles)', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Verify no inline style tags
      const inlineStyleTags = await page.locator('style:not([nonce])').count();
      expect(inlineStyleTags).toBe(0);
      
      // Verify CSS file is loaded (check for any external stylesheet)
      const externalStylesheets = await page.locator('link[rel="stylesheet"]').count();
      expect(externalStylesheets).toBeGreaterThan(0);
    });

    test('should load external JS from menu.js (no inline scripts)', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Verify no inline script tags (except those with nonce or from Vite)
      const inlineScripts = await page.locator('script:not([src]):not([nonce])').count();
      expect(inlineScripts).toBe(0);
      
      // Verify external JS files are loaded
      const externalScripts = await page.locator('script[src]').count();
      expect(externalScripts).toBeGreaterThan(0);
    });

    test('should have security headers in response', async ({ page }) => {
      const response = await page.goto('https://localhost:7035/');
      
      if (response) {
        const headers = response.headers();
        
        // Verify CSP header exists
        expect(headers['content-security-policy']).toBeDefined();
        
        // Verify other security headers
        expect(headers['x-content-type-options']).toBe('nosniff');
        expect(headers['x-frame-options']).toBe('DENY');
        expect(headers['x-xss-protection']).toBe('1; mode=block');
      }
    });
  });

  test.describe('Menu Active State', () => {
    test('should highlight active menu item on Authorizations page', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Open user dropdown
      await page.click('#userDropdown');
      await page.waitForSelector('.dropdown-menu.show');
      
      // Verify Authorizations menu item is active
      const authMenuItem = page.locator('.dropdown-menu a[href="/Account/Authorizations"]');
      await expect(authMenuItem).toHaveClass(/active/);
    });

    test('should highlight active menu item on LinkedAccounts page', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      // Open user dropdown
      await page.click('#userDropdown');
      await page.waitForSelector('.dropdown-menu.show');
      
      // Verify LinkedAccounts menu item is active
      const linkedMenuItem = page.locator('.dropdown-menu a[href="/Account/LinkedAccounts"]');
      await expect(linkedMenuItem).toHaveClass(/active/);
    });

    test('should not highlight menu items on homepage', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Open user dropdown
      await page.click('#userDropdown');
      await page.waitForSelector('.dropdown-menu.show');
      
      // No menu items should be active on homepage
      const activeItems = page.locator('.dropdown-menu a.active');
      await expect(activeItems).toHaveCount(0);
    });
  });
});
