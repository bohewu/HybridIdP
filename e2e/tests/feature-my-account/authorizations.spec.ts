import { test, expect } from '@playwright/test';

/**
 * Phase 11.6 - Authorizations Page E2E Tests
 * 
 * Tests for the new Authorizations page:
 * - Display authorized applications
 * - Revoke authorization functionality
 * - App icon gradients (CSS-based, no inline styles)
 * - Scope display
 */
test.describe('Phase 11.6 - Authorizations Page', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test.describe('Page Layout and Content', () => {
    test('should display Authorizations page with correct title', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Verify page title
      await expect(page.locator('h1')).toContainText('授權管理');
      
      // Verify description
      await expect(page.locator('p.lead')).toContainText('管理您已授權的應用程式和服務');
    });

    test('should display authorized applications list', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Wait for content to load
      await page.waitForLoadState('networkidle');
      
      // Check if either apps are shown or "no apps" message
      const hasApps = await page.locator('.app-card').count() > 0;
      const hasNoAppsMessage = await page.locator('.alert-info').isVisible();
      
      // One of them should be true
      expect(hasApps || hasNoAppsMessage).toBeTruthy();
    });

    test('should display app cards with gradient icons (CSS classes, no inline styles)', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Wait for apps to load
      await page.waitForSelector('.app-card', { timeout: 5000 }).catch(() => {
        // No apps authorized yet, that's okay
      });
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        // Check first app card
        const firstCard = appCards.first();
        
        // Verify app icon has gradient class (not inline style)
        const appIcon = firstCard.locator('.app-icon');
        await expect(appIcon).toBeVisible();
        
        // Check that it has a gradient-X class
        const classes = await appIcon.getAttribute('class');
        expect(classes).toMatch(/gradient-[0-7]/);
        
        // Verify no inline style attribute
        const inlineStyle = await appIcon.getAttribute('style');
        expect(inlineStyle).toBeNull();
      }
    });

    test('should display app name and client ID', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        const firstCard = appCards.first();
        
        // App name should be visible
        await expect(firstCard.locator('h5.card-title')).toBeVisible();
        
        // Client ID should be visible
        const clientIdText = await firstCard.locator('.text-muted small').textContent();
        expect(clientIdText).toContain('Client ID:');
      }
    });

    test('should display scopes with icons', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        const firstCard = appCards.first();
        
        // Scopes section should exist
        const scopesList = firstCard.locator('.list-group-item');
        const scopeCount = await scopesList.count();
        
        if (scopeCount > 0) {
          // Each scope should have an icon
          const firstScope = scopesList.first();
          const icon = firstScope.locator('i, img.scope-icon');
          await expect(icon).toBeVisible();
          
          // If it's an img, verify it has scope-icon class (no inline style)
          const isImg = await icon.evaluate(el => el.tagName === 'IMG');
          if (isImg) {
            await expect(icon).toHaveClass(/scope-icon/);
            const inlineStyle = await icon.getAttribute('style');
            expect(inlineStyle).toBeNull();
          }
        }
      }
    });

    test('should display authorization date', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        const firstCard = appCards.first();
        
        // Authorization date should be visible
        const dateText = await firstCard.locator('.text-muted small').last().textContent();
        expect(dateText).toContain('授權日期:');
      }
    });
  });

  test.describe('Revoke Authorization', () => {
    test('should display revoke button for each app', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        const firstCard = appCards.first();
        const revokeButton = firstCard.locator('button.btn-outline-danger');
        
        // Revoke button should be visible
        await expect(revokeButton).toBeVisible();
        await expect(revokeButton).toContainText('撤銷授權');
      }
    });

    test('should show confirmation dialog when clicking revoke', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count > 0) {
        const firstCard = appCards.first();
        const revokeButton = firstCard.locator('button.btn-outline-danger');
        
        // Setup dialog handler
        let dialogShown = false;
        page.on('dialog', async dialog => {
          dialogShown = true;
          expect(dialog.message()).toContain('確定要撤銷');
          await dialog.dismiss();
        });
        
        // Click revoke button
        await revokeButton.click();
        
        // Wait a bit for dialog
        await page.waitForTimeout(500);
        
        // Dialog should have been shown
        expect(dialogShown).toBeTruthy();
      }
    });

    test('should not revoke when confirmation is cancelled', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const initialCount = await appCards.count();
      
      if (initialCount > 0) {
        const firstCard = appCards.first();
        const revokeButton = firstCard.locator('button.btn-outline-danger');
        
        // Dismiss confirmation dialog
        page.on('dialog', async dialog => {
          await dialog.dismiss();
        });
        
        await revokeButton.click();
        await page.waitForTimeout(500);
        
        // App count should remain the same
        const newCount = await page.locator('.app-card').count();
        expect(newCount).toBe(initialCount);
      }
    });
  });

  test.describe('Responsive Design', () => {
    test('should display apps in grid on desktop', async ({ page }) => {
      await page.setViewportSize({ width: 1920, height: 1080 });
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count >= 2) {
        const firstCard = appCards.first();
        const secondCard = appCards.nth(1);
        
        const firstBox = await firstCard.boundingBox();
        const secondBox = await secondCard.boundingBox();
        
        // On desktop, cards should be side by side
        const verticalDiff = Math.abs(firstBox?.y! - secondBox?.y!);
        expect(verticalDiff).toBeLessThan(100);
      }
    });

    test('should stack apps vertically on mobile', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appCards = page.locator('.app-card');
      const count = await appCards.count();
      
      if (count >= 2) {
        const firstCard = appCards.first();
        const secondCard = appCards.nth(1);
        
        const firstBox = await firstCard.boundingBox();
        const secondBox = await secondCard.boundingBox();
        
        // On mobile, second card should be below first card
        expect(secondBox?.y).toBeGreaterThan(firstBox?.y! + firstBox?.height!);
      }
    });
  });

  test.describe('CSP Compliance', () => {
    test('should have no inline styles on app icons', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      const appIcons = page.locator('.app-icon');
      const count = await appIcons.count();
      
      for (let i = 0; i < count; i++) {
        const icon = appIcons.nth(i);
        const inlineStyle = await icon.getAttribute('style');
        expect(inlineStyle).toBeNull();
      }
    });

    test('should load gradient styles from CSS file', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Verify site.css is loaded and contains gradient classes
      const hasGradientClasses = await page.evaluate(() => {
        const styleSheets = Array.from(document.styleSheets);
        for (const sheet of styleSheets) {
          try {
            const rules = Array.from(sheet.cssRules || []);
            const hasGradient = rules.some(rule => 
              rule.cssText.includes('.app-icon.gradient-')
            );
            if (hasGradient) return true;
          } catch (e) {
            // Cross-origin stylesheet, skip
          }
        }
        return false;
      });
      
      expect(hasGradientClasses).toBeTruthy();
    });

    test('should have no CSP violations', async ({ page }) => {
      const cspViolations: string[] = [];
      
      page.on('console', msg => {
        if (msg.type() === 'error' && msg.text().includes('Content Security Policy')) {
          cspViolations.push(msg.text());
        }
      });
      
      await page.goto('https://localhost:7035/Account/Authorizations');
      await page.waitForLoadState('networkidle');
      
      expect(cspViolations).toHaveLength(0);
    });
  });

  test.describe('Navigation', () => {
    test('should have back to homepage link', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/Authorizations');
      
      // Look for back link or breadcrumb
      const backLink = page.locator('a[href="/"]');
      const hasBackLink = await backLink.count() > 0;
      
      expect(hasBackLink).toBeTruthy();
    });

    test('should be accessible from user dropdown menu', async ({ page }) => {
      await page.goto('https://localhost:7035/');
      
      // Open user dropdown
      await page.click('#userDropdown');
      await page.waitForSelector('.dropdown-menu.show');
      
      // Click Authorizations link
      const authLink = page.locator('.dropdown-menu a[href="/Account/Authorizations"]');
      await authLink.click();
      
      // Should navigate to Authorizations page
      await expect(page).toHaveURL('https://localhost:7035/Account/Authorizations');
    });
  });
});
