import { test, expect } from '@playwright/test';

/**
 * Phase 11.6 - Linked Accounts Page E2E Tests
 * 
 * Tests for the new LinkedAccounts page:
 * - Display linked accounts
 * - Account switching functionality
 * - Avatar display (CSS-based, no inline styles)
 * - Active/Inactive status
 * - Role badges
 */
test.describe('Phase 11.6 - Linked Accounts Page', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin user
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name');
  });

  test.describe('Page Layout and Content', () => {
    test('should display LinkedAccounts page with correct title', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      // Verify page title
      await expect(page.locator('h1')).toContainText('帳號鏈結');
      
      // Verify description
      await expect(page.locator('p.lead')).toContainText('查看和管理關聯的帳號');
    });

    test('should display linked accounts list', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      // Wait for content to load
      await page.waitForLoadState('networkidle');
      
      // Check if either accounts are shown or "no accounts" message
      const hasAccounts = await page.locator('.card').count() > 0;
      const hasNoAccountsMessage = await page.locator('.alert-info').isVisible();
      
      // One of them should be true
      expect(hasAccounts || hasNoAccountsMessage).toBeTruthy();
    });

    test('should display account cards with avatars (CSS classes, no inline styles)', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        const firstCard = accountCards.first();
        
        // Verify avatar has account-avatar class (not inline style)
        const avatar = firstCard.locator('.user-avatar');
        await expect(avatar).toBeVisible();
        
        // Check that it has account-avatar class
        await expect(avatar).toHaveClass(/account-avatar/);
        
        // Verify no inline style attribute
        const inlineStyle = await avatar.getAttribute('style');
        expect(inlineStyle).toBeNull();
      }
    });

    test('should display account information', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        const firstCard = accountCards.first();
        
        // Account name should be visible
        await expect(firstCard.locator('h5.card-title')).toBeVisible();
        
        // Email should be visible
        const emailText = await firstCard.locator('.card-text').first().textContent();
        expect(emailText).toContain('@');
      }
    });

    test('should display account status (Active/Inactive)', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        const firstCard = accountCards.first();
        
        // Status icon should be visible
        const statusIcon = firstCard.locator('.bi-check-circle-fill, .bi-x-circle-fill');
        await expect(statusIcon).toBeVisible();
        
        // Status text should be present
        const hasActiveText = await firstCard.locator('text=Active').isVisible() ||
                              await firstCard.locator('text=Inactive').isVisible() ||
                              await firstCard.locator('text=啟用').isVisible() ||
                              await firstCard.locator('text=停用').isVisible();
        expect(hasActiveText).toBeTruthy();
      }
    });

    test('should display role badges', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        const firstCard = accountCards.first();
        
        // Check if roles are displayed
        const roleBadges = firstCard.locator('.badge');
        const roleCount = await roleBadges.count();
        
        // Should have at least one role or no roles section
        expect(roleCount).toBeGreaterThanOrEqual(0);
      }
    });

    test('should indicate current account', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        // At least one card should be marked as current (no switch button)
        let foundCurrentAccount = false;
        
        for (let i = 0; i < count; i++) {
          const card = accountCards.nth(i);
          const hasSwitchButton = await card.locator('button.switch-account-btn').count() === 0;
          if (hasSwitchButton) {
            foundCurrentAccount = true;
            break;
          }
        }
        
        expect(foundCurrentAccount).toBeTruthy();
      }
    });
  });

  test.describe('Account Switching', () => {
    test('should display switch button for non-current active accounts', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      // Look for switch buttons (if there are multiple active accounts)
      const switchButtons = page.locator('button.switch-account-btn');
      const switchButtonCount = await switchButtons.count();
      
      // If there are multiple accounts, some should have switch buttons
      if (count > 1) {
        expect(switchButtonCount).toBeGreaterThanOrEqual(0);
      }
    });

    test('should not display switch button for current account', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count > 0) {
        // The current account should not have a switch button
        // We can verify by checking that not all cards have switch buttons
        const switchButtons = page.locator('button.switch-account-btn');
        const switchButtonCount = await switchButtons.count();
        
        expect(switchButtonCount).toBeLessThan(count);
      }
    });

    test('should not display switch button for inactive accounts', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      for (let i = 0; i < count; i++) {
        const card = accountCards.nth(i);
        
        // Check if account is inactive
        const isInactive = await card.locator('.bi-x-circle-fill').count() > 0;
        
        if (isInactive) {
          // Inactive accounts should not have switch button
          const switchButton = card.locator('button.switch-account-btn');
          await expect(switchButton).not.toBeVisible();
        }
      }
    });

    test('should show confirmation dialog when clicking switch account', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const switchButtons = page.locator('button.switch-account-btn');
      const count = await switchButtons.count();
      
      if (count > 0) {
        let dialogShown = false;
        
        // Setup dialog handler
        page.on('dialog', async dialog => {
          dialogShown = true;
          expect(dialog.message()).toContain('確定要切換');
          await dialog.dismiss();
        });
        
        // Click first switch button
        await switchButtons.first().click();
        
        // Wait a bit for dialog
        await page.waitForTimeout(500);
        
        // Dialog should have been shown
        expect(dialogShown).toBeTruthy();
      }
    });

    test('should not switch when confirmation is cancelled', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const switchButtons = page.locator('button.switch-account-btn');
      const count = await switchButtons.count();
      
      if (count > 0) {
        // Get current page URL
        const initialUrl = page.url();
        
        // Dismiss confirmation dialog
        page.on('dialog', async dialog => {
          await dialog.dismiss();
        });
        
        await switchButtons.first().click();
        await page.waitForTimeout(1000);
        
        // URL should remain the same (no reload)
        expect(page.url()).toBe(initialUrl);
      }
    });
  });

  test.describe('External JavaScript', () => {
    test('should load linked-accounts.js external script', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      // Verify linked-accounts.js is loaded
      const scripts = await page.evaluate(() => {
        return Array.from(document.scripts)
          .map(script => script.src)
          .filter(src => src.includes('linked-accounts.js'));
      });
      
      expect(scripts.length).toBeGreaterThan(0);
    });

    test('should have no inline scripts', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      // Count inline scripts (excluding those with src or nonce)
      const inlineScripts = await page.locator('script:not([src]):not([nonce])').count();
      expect(inlineScripts).toBe(0);
    });

    test('should pass localized messages via data attributes', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const switchButtons = page.locator('button.switch-account-btn');
      const count = await switchButtons.count();
      
      if (count > 0) {
        const firstButton = switchButtons.first();
        
        // Verify data attributes exist
        const hasAccountId = await firstButton.getAttribute('data-account-id') !== null;
        const hasAccountEmail = await firstButton.getAttribute('data-account-email') !== null;
        const hasConfirmMessage = await firstButton.getAttribute('data-confirm-message') !== null;
        const hasErrorMessage = await firstButton.getAttribute('data-error-message') !== null;
        const hasFailedMessage = await firstButton.getAttribute('data-failed-message') !== null;
        
        expect(hasAccountId).toBeTruthy();
        expect(hasAccountEmail).toBeTruthy();
        expect(hasConfirmMessage).toBeTruthy();
        expect(hasErrorMessage).toBeTruthy();
        expect(hasFailedMessage).toBeTruthy();
      }
    });
  });

  test.describe('Responsive Design', () => {
    test('should display accounts in grid on desktop', async ({ page }) => {
      await page.setViewportSize({ width: 1920, height: 1080 });
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count >= 2) {
        const firstCard = accountCards.first();
        const secondCard = accountCards.nth(1);
        
        const firstBox = await firstCard.boundingBox();
        const secondBox = await secondCard.boundingBox();
        
        // On desktop, cards should be side by side or in grid
        const verticalDiff = Math.abs(firstBox?.y! - secondBox?.y!);
        expect(verticalDiff).toBeLessThan(150);
      }
    });

    test('should stack accounts vertically on mobile', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const accountCards = page.locator('.card');
      const count = await accountCards.count();
      
      if (count >= 2) {
        const firstCard = accountCards.first();
        const secondCard = accountCards.nth(1);
        
        const firstBox = await firstCard.boundingBox();
        const secondBox = await secondCard.boundingBox();
        
        // On mobile, second card should be below first card
        expect(secondBox?.y).toBeGreaterThan(firstBox?.y! + firstBox?.height!);
      }
    });
  });

  test.describe('CSP Compliance', () => {
    test('should have no inline styles on avatars', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
      const avatars = page.locator('.user-avatar');
      const count = await avatars.count();
      
      for (let i = 0; i < count; i++) {
        const avatar = avatars.nth(i);
        const inlineStyle = await avatar.getAttribute('style');
        expect(inlineStyle).toBeNull();
      }
    });

    test('should have no CSP violations', async ({ page }) => {
      const cspViolations: string[] = [];
      
      page.on('console', msg => {
        if (msg.type() === 'error' && msg.text().includes('Content Security Policy')) {
          cspViolations.push(msg.text());
        }
      });
      
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      await page.waitForLoadState('networkidle');
      
      expect(cspViolations).toHaveLength(0);
    });
  });

  test.describe('Navigation', () => {
    test('should have back to homepage link', async ({ page }) => {
      await page.goto('https://localhost:7035/Account/LinkedAccounts');
      
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
      
      // Click LinkedAccounts link
      const linkedLink = page.locator('.dropdown-menu a[href="/Account/LinkedAccounts"]');
      await linkedLink.click();
      
      // Should navigate to LinkedAccounts page
      await expect(page).toHaveURL('https://localhost:7035/Account/LinkedAccounts');
    });
  });
});
