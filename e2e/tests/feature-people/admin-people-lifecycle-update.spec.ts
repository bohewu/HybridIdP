import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle Manual Status Update', () => {
  test.beforeEach(async ({ page }) => {
    await admin.loginAsAdminViaIdP(page);
  });

  test('Update person status from Active to Suspended via UI', async ({ page }) => {
    // 1. Create Active person
    const firstName = `ToSuspend_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    // 2. Find and Edit
    await page.goto('https://localhost:7035/Admin/Persons');
    await admin.searchAndConfirmAction(page, 'people', firstName, 'Edit', {
      confirmSelector: 'button:has-text("Save")', // Edit dialog uses "Save"
      waitForApi: false // We will handle saving manually below
    });

    // 3. Change Status in Dialog
    await expect(page.locator('#status')).toHaveValue('Active');
    await page.selectOption('#status', 'Suspended');
    
    // 4. Save
    // Intercept PUT request
    const updatePromise = page.waitForResponse(response => 
      response.url().includes('/api/admin/people') && 
      response.request().method() === 'PUT' && 
      response.status() === 200
    );
    await page.click('button:has-text("Save")');
    await updatePromise;

    // 5. Verify UI Badge updated
    // Need to re-search to refresh list or wait for list update
    await expect(page.locator('[data-test-id="person-status-badge"]', { hasText: 'Suspended' }).first()).toBeVisible();
    
    // 6. Verify API Details
    // Get person ID from the search result or by re-fetching
    // We can just create a new context or use a helper to fetch details by name if we had one.
    // For now, checking UI is sufficient proof of update, but let's verify listing API if possible.
    const row = page.locator('tr', { hasText: firstName });
    await expect(row.locator('[data-test-id="person-status-badge"]')).toContainText('Suspended');
    await expect(row.locator('[data-test-id="person-status-badge"]')).toHaveClass(/bg-orange-100/);
  });
  
  test('Update person status from Suspended to Active via UI', async ({ page }) => {
    // 1. Create Suspended person
    const firstName = `ToActivate_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Suspended'
    });

    // 2. Find and Edit
    await page.goto('https://localhost:7035/Admin/Persons');
    // Using searchAndClickAction to open modal, avoiding auto-confirm
    const result = await admin.searchAndClickAction(page, 'people', firstName, 'Edit');
    expect(result.clicked).toBe(true);

    // 3. Change Status
    await expect(page.locator('#status')).toHaveValue('Suspended');
    await page.selectOption('#status', 'Active');
    
    // 4. Save
    const updatePromise = page.waitForResponse(response => 
      response.url().includes('/api/admin/people') && 
      response.request().method() === 'PUT' && 
      response.status() === 200
    );
    await page.click('button:has-text("Save")');
    await updatePromise;

    // 5. Verify UI Badge
    const row = page.locator('tr', { hasText: firstName });
    await expect(row.locator('[data-test-id="person-status-badge"]')).toContainText('Active');
    await expect(row.locator('[data-test-id="person-status-badge"]')).toHaveClass(/bg-green-100/);
  });

  test('Update person dates via UI', async ({ page }) => {
    const firstName = `DateUpdate_${Date.now()}`;
    await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    await page.goto('https://localhost:7035/Admin/Persons');
    await admin.searchAndClickAction(page, 'people', firstName, 'Edit');

    // Set dates
    await page.fill('#startDate', '2025-01-01');
    await page.fill('#endDate', '2025-12-31');
    
    // Save
    const updatePromise = page.waitForResponse(response => 
      response.url().includes('/api/admin/people') && 
      response.request().method() === 'PUT' && 
      response.status() === 200
    );
    await page.click('button:has-text("Save")');
    await updatePromise;

    // Verify Clock icon appears (since dates are set)
    // Actually clock icon logic: !person.canAuthenticate && person.status === 'Active'
    // If today is 2025-12-11 (from prompt metadata), then 2025-01-01 to 2025-12-31 implies Active?
    // Wait, prompt time is 2025-12-11.
    // StartDate 2025-01-01 <= Now <= EndDate 2025-12-31. So canAuthenticate should be TRUE.
    // So NO clock icon.

    // Let's set EndDate to yesterday to force Inactive
    // Today is 2025-12-11. Set EndDate to 2025-12-10.
    await admin.searchAndClickAction(page, 'people', firstName, 'Edit');
    await page.fill('#endDate', '2025-12-10');
    
    const updatePromise2 = page.waitForResponse(response => 
      response.url().includes('/api/admin/people') && 
      response.request().method() === 'PUT' && 
      response.status() === 200
    );
    await page.click('button:has-text("Save")');
    await updatePromise2;

    // Verify Clock icon (Date Restricted)
    const row = page.locator('tr', { hasText: firstName });
    // Badge is still Active
    await expect(row.locator('[data-test-id="person-status-badge"]')).toContainText('Active');
    // But Badge color changes to yellow if !canAuthenticate (logic in PersonsApp.vue: person.status === 'Active' && !person.canAuthenticate ? 'bg-yellow-100'...)
    await expect(row.locator('[data-test-id="person-status-badge"]')).toHaveClass(/bg-yellow-100/);
    
    // And Clock icon visible
    await expect(row.locator('[data-test-id="person-date-restricted"]')).toBeVisible();
  });
});
