import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle Status & Updates', () => {
  // Use a localized timeout for this complex flow, giving it enough time to cycle through 5+ states
  test.setTimeout(90000);

  test.beforeEach(async ({ page }) => {
    await admin.loginAsAdminViaIdP(page);
  });

  test('Consolidated Lifecycle Flow: Create -> Update Statuses -> Check Badges & Auth', async ({ page }) => {
    // 1. Create Person with Active status
    const firstName = `Lifecycle_${Date.now()}`;
    const person = await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    // Check backend auth status via API
    let details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Active');
    expect(details.canAuthenticate, 'Active person should authenticate').toBe(true);

    await page.goto('https://localhost:7035/Admin/People');
    // Initial search to isolate the row
    await admin.searchListForItem(page, 'people', firstName);

    // Verify Active Badge
    // Use .first() to avoid strict mode violation if multiple partial matches (though timestamp should be unique)
    let row = page.locator('tr', { hasText: firstName }).first();
    let badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toContainText(/Active|啟用中/);
    await expect(badge).toHaveClass(/bg-green-100|text-green-800/);

    // 2. Update to Suspended
    await row.locator('[data-test-id="person-edit-button"]').click();
    await page.waitForSelector('#status');
    await page.selectOption('#status', 'Suspended');
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    // Verify Suspended Auth Logic
    details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Suspended');
    expect(details.canAuthenticate, 'Suspended person should NOT authenticate').toBe(false);

    // Verify Suspended Badge - Just wait for the row to update
    // We assume the list state (search query) is preserved after edit modal closes.
    // If not, we might need to rely on the fact that it's likely the top item or just visible.
    await admin.searchListForItem(page, 'people', firstName);
    row = page.locator('tr', { hasText: firstName }).first();
    badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toContainText(/Suspended|已停權/);
    await expect(badge).toHaveClass(/bg-orange-100|text-orange-800/);

    // 3. Update to Pending
    await row.locator('[data-test-id="person-edit-button"]').click();
    await page.waitForSelector('#status');
    await page.selectOption('#status', 'Pending');
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Pending');
    expect(details.canAuthenticate, 'Pending person should NOT authenticate').toBe(false);

    await admin.searchListForItem(page, 'people', firstName);
    row = page.locator('tr', { hasText: firstName }).first();
    badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toContainText(/Pending|預約/);
    await expect(badge).toHaveClass(/bg-blue-100|text-blue-800/);

    // 4. Update to Terminated
    await row.locator('[data-test-id="person-edit-button"]').click();
    await page.waitForSelector('#status');
    await page.selectOption('#status', 'Terminated');
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Terminated');
    expect(details.canAuthenticate, 'Terminated person should NOT authenticate').toBe(false);

    await admin.searchListForItem(page, 'people', firstName);
    row = page.locator('tr', { hasText: firstName }).first();
    badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toContainText(/Terminated|已離職/);
    await expect(badge).toHaveClass(/bg-red-100|text-red-800/);

    // 5. Update with Date Restriction
    await row.locator('[data-test-id="person-edit-button"]').click();
    await page.selectOption('#status', 'Active');
    // Set EndDate to Yesterday
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const endDate = yesterday.toISOString().split('T')[0];
    await page.fill('#endDate', endDate);
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Active');
    expect(details.canAuthenticate, 'Active person with past EndDate should NOT authenticate').toBe(false);

    await admin.searchListForItem(page, 'people', firstName);
    row = page.locator('tr', { hasText: firstName }).first();
    badge = row.locator('[data-test-id="person-status-badge"]');

    // Expect yellow warning
    await expect(badge).toHaveClass(/bg-yellow-100/);
    await expect(row.locator('[data-test-id="person-date-restricted"]')).toBeVisible();

    // 6. Fix Date Restriction
    await row.locator('[data-test-id="person-edit-button"]').click();
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const validEndDate = tomorrow.toISOString().split('T')[0];
    await page.fill('#endDate', validEndDate);
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT');

    details = await admin.getPersonDetails(page, person.id);
    expect(details.canAuthenticate, 'Active person with valid dates SHOULD authenticate').toBe(true);

    await admin.searchListForItem(page, 'people', firstName);
    badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toHaveClass(/bg-green-100/);
    await expect(row.locator('[data-test-id="person-date-restricted"]')).toBeHidden();
  });
});
