import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle Manual Status Update', () => {
  test.setTimeout(30000);

  test.beforeEach(async ({ page }) => {
    await admin.loginAsAdminViaIdP(page);
  });

  test('Update person status from Active to Suspended via UI', async ({ page }) => {
    const firstName = `ToSuspend_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await page.fill('[data-test-id="person-search-input"]', firstName);
    await page.waitForTimeout(500);

    // Click Edit button (only one row after search)
    await page.click('[data-test-id="person-edit-button"]');

    await page.waitForSelector('#status');
    await page.selectOption('#status', 'Suspended');
    
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    await page.waitForTimeout(500);
    await expect(page.locator('[data-test-id="person-status-badge"]').first()).toContainText(/Suspended|已停權/);
  });
  
  test('Update person status from Suspended to Active via UI', async ({ page }) => {
    const firstName = `ToActivate_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Suspended'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await page.fill('[data-test-id="person-search-input"]', firstName);
    await page.waitForTimeout(500);

    await page.click('[data-test-id="person-edit-button"]');

    await page.waitForSelector('#status');
    await page.selectOption('#status', 'Active');
    
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    await page.waitForTimeout(500);
    await expect(page.locator('[data-test-id="person-status-badge"]').first()).toContainText(/Active|啟用中/);
  });

  test('Update person dates shows date restriction indicator', async ({ page }) => {
    const firstName = `DateRestrict_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    await page.goto('https://localhost:7035/Admin/People');
    await page.fill('[data-test-id="person-search-input"]', firstName);
    await page.waitForTimeout(500);

    await page.click('[data-test-id="person-edit-button"]');

    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const endDate = yesterday.toISOString().split('T')[0];
    
    await page.waitForSelector('#endDate');
    await page.fill('#endDate', endDate);
    
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    await page.waitForTimeout(500);
    await expect(page.locator('[data-test-id="person-status-badge"]').first()).toHaveClass(/bg-yellow-100/);
    await expect(page.locator('[data-test-id="person-date-restricted"]')).toBeVisible();
  });
});
