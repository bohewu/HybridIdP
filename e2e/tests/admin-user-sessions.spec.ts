import { test, expect } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';

test.beforeEach(async ({ page }) => {
  // Login as admin
  await page.goto(`${IDP_BASE}/Identity/Account/Login`);
  await page.fill('input[name="Input.Email"]', 'admin@hybridauth.local');
  await page.fill('input[name="Input.Password"]', 'Admin@123');
  await page.click('button[type="submit"]');
  await page.waitForLoadState('networkidle');
});

test.describe('Admin User Sessions', () => {
  test('should open sessions dialog and display sessions list', async ({ page }) => {
    // Navigate to Users admin page
    await page.goto(`${IDP_BASE}/Admin/Users`);
    await page.waitForLoadState('networkidle');

    // Wait for Vue app to initialize
    await page.waitForSelector('h1:has-text("User Management")', { timeout: 10000 });

    // Click manage-sessions button on first user
    const manageSessionsButton = page.locator('button[title="Manage Sessions"]').first();
    await expect(manageSessionsButton).toBeVisible();
    await manageSessionsButton.click();

    // Wait for sessions dialog to open
    await page.waitForSelector('text=Manage User Sessions', { timeout: 5000 });

    // Verify dialog header is visible
    await expect(page.locator('h2:has-text("Manage User Sessions")')).toBeVisible();

    // Verify user label is displayed
    await expect(page.locator('text=User:')).toBeVisible();

    // Wait for sessions to load (either table or empty message)
    await page.waitForTimeout(2000);

    // Check if there are sessions or empty state
    const hasTable = await page.locator('table').isVisible().catch(() => false);
    const hasEmptyState = await page.locator('text=No active sessions found').isVisible().catch(() => false);

    expect(hasTable || hasEmptyState).toBe(true);

    // Take screenshot
    await page.screenshot({ path: 'test-results/admin-user-sessions-list.png' });
  });

  test('should revoke single session if sessions exist', async ({ page }) => {
    // Navigate to Users admin page
    await page.goto(`${IDP_BASE}/Admin/Users`);
    await page.waitForLoadState('networkidle');

    // Wait for Vue app
    await page.waitForSelector('h1:has-text("User Management")', { timeout: 10000 });

    // Open sessions dialog for first user
    await page.locator('button[title="Manage Sessions"]').first().click();
    await page.waitForSelector('text=Manage User Sessions', { timeout: 5000 });

    // Wait for sessions to load
    await page.waitForTimeout(2000);

    // Check if revoke button exists (means there are sessions)
    const revokeButton = page.locator('button:has-text("Revoke")').first();
    const revokeButtonVisible = await revokeButton.isVisible().catch(() => false);

    if (revokeButtonVisible) {
      // Count sessions before revoke
      const sessionCountBefore = await page.locator('tbody tr').count();

      // Click revoke button
      await revokeButton.click();

      // Confirm the action
      page.on('dialog', dialog => dialog.accept());
      await page.waitForTimeout(1000);

      // Wait for session to be removed
      await page.waitForTimeout(2000);

      // Count sessions after revoke
      const sessionCountAfter = await page.locator('tbody tr').count().catch(() => 0);

      // Verify session was removed
      expect(sessionCountAfter).toBeLessThan(sessionCountBefore);

      // Take screenshot
      await page.screenshot({ path: 'test-results/admin-user-sessions-revoked-one.png' });
    } else {
      console.log('No sessions to revoke - test skipped');
      await page.screenshot({ path: 'test-results/admin-user-sessions-no-sessions.png' });
    }
  });

  test('should revoke all sessions if sessions exist', async ({ page }) => {
    // Navigate to Users admin page
    await page.goto(`${IDP_BASE}/Admin/Users`);
    await page.waitForLoadState('networkidle');

    // Wait for Vue app
    await page.waitForSelector('h1:has-text("User Management")', { timeout: 10000 });

    // Open sessions dialog for first user
    await page.locator('button[title="Manage Sessions"]').first().click();
    await page.waitForSelector('text=Manage User Sessions', { timeout: 5000 });

    // Wait for sessions to load
    await page.waitForTimeout(2000);

    // Check if revoke all button exists
    const revokeAllButton = page.locator('button:has-text("Revoke All Sessions")');
    const revokeAllVisible = await revokeAllButton.isVisible().catch(() => false);

    if (revokeAllVisible) {
      // Click revoke all button
      await revokeAllButton.click();

      // Confirm the action
      page.on('dialog', dialog => dialog.accept());
      await page.waitForTimeout(1000);

      // Wait for sessions to be removed
      await page.waitForTimeout(2000);

      // Verify empty state appears
      const hasEmptyState = await page.locator('text=No active sessions found').isVisible();
      expect(hasEmptyState).toBe(true);

      // Take screenshot
      await page.screenshot({ path: 'test-results/admin-user-sessions-revoked-all.png' });
    } else {
      console.log('No sessions to revoke - test skipped');
      await page.screenshot({ path: 'test-results/admin-user-sessions-no-sessions-for-revoke-all.png' });
    }
  });

  test('should close dialog when close button clicked', async ({ page }) => {
    // Navigate to Users admin page
    await page.goto(`${IDP_BASE}/Admin/Users`);
    await page.waitForLoadState('networkidle');

    // Wait for Vue app
    await page.waitForSelector('h1:has-text("User Management")', { timeout: 10000 });

    // Open sessions dialog
    await page.locator('button[title="Manage Sessions"]').first().click();
    await page.waitForSelector('text=Manage User Sessions', { timeout: 5000 });

    // Verify dialog is visible
    await expect(page.locator('h2:has-text("Manage User Sessions")')).toBeVisible();

    // Click close button
    await page.locator('button:has-text("Close")').click();

    // Verify dialog is closed
    await page.waitForTimeout(500);
    const dialogVisible = await page.locator('h2:has-text("Manage User Sessions")').isVisible().catch(() => false);
    expect(dialogVisible).toBe(false);

    // Take screenshot
    await page.screenshot({ path: 'test-results/admin-user-sessions-closed.png' });
  });
});
