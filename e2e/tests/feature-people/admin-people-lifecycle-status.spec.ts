import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle Status Display', () => {
  test.beforeEach(async ({ page }) => {
    await admin.loginAsAdminViaIdP(page);
  });

  test('Smoke Test - Admin Login', async ({ page }) => {
    // Just verify we are on the admin page
    await expect(page).toHaveURL(/Admin/);
  });

  test('Status column header is visible in Person list', async ({ page }) => {
    await page.goto('https://localhost:7035/Admin/People');
    await page.waitForLoadState('networkidle');
    // Check for both English "Status" and Chinese "狀態"
    const statusHeader = page.locator('th:has-text("Status"), th:has-text("狀態")');
    await expect(statusHeader.first()).toBeVisible({ timeout: 10000 });
  });

  test('Create person with Active status - shows green badge', async ({ page }) => {
    const firstName = `Active_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await admin.searchListForItem(page, 'people', firstName);

    const row = page.locator('tr', { hasText: firstName });
    const badge = row.locator('[data-test-id="person-status-badge"]');
    
    await expect(badge).toBeVisible();
    await expect(badge).toContainText('Active');
    // Green badge class check (bg-green-100 or text-green-800)
    await expect(badge).toHaveClass(/bg-green-100/);
    await expect(badge).toHaveClass(/text-green-800/);
  });

  test('Create person with Suspended status - shows orange badge', async ({ page }) => {
    const firstName = `Suspended_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Suspended'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await admin.searchListForItem(page, 'people', firstName);

    const row = page.locator('tr', { hasText: firstName });
    const badge = row.locator('[data-test-id="person-status-badge"]');
    
    await expect(badge).toBeVisible();
    await expect(badge).toContainText('Suspended');
    // Orange badge
    await expect(badge).toHaveClass(/bg-orange-100/);
    await expect(badge).toHaveClass(/text-orange-800/);
  });

  test('Create person with Pending status - shows blue badge', async ({ page }) => {
    const firstName = `Pending_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Pending'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await admin.searchListForItem(page, 'people', firstName);

    const row = page.locator('tr', { hasText: firstName });
    const badge = row.locator('[data-test-id="person-status-badge"]');
    
    await expect(badge).toBeVisible();
    await expect(badge).toContainText('Pending');
    // Blue badge
    await expect(badge).toHaveClass(/bg-blue-100/);
    await expect(badge).toHaveClass(/text-blue-800/);
  });

  test('Create person with Terminated status - shows red badge', async ({ page }) => {
    const firstName = `Terminated_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Terminated'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await admin.searchListForItem(page, 'people', firstName);

    const row = page.locator('tr', { hasText: firstName });
    const badge = row.locator('[data-test-id="person-status-badge"]');
    
    await expect(badge).toBeVisible();
    await expect(badge).toContainText('Terminated');
    // Red badge
    await expect(badge).toHaveClass(/bg-red-100/);
    await expect(badge).toHaveClass(/text-red-800/);
  });
});
