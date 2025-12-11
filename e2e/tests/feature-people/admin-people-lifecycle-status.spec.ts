import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle (Core)', () => {
  // Timeout for the whole test - 45s should be enough
  test.setTimeout(45000);

  test.beforeEach(async ({ page }) => {
    // Direct login without health check polling
    await page.goto('https://localhost:7035/Account/Logout');
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-info-name, .user-name', { timeout: 15000 });
  });

  test('Core: Create Active -> Suspend -> Verify Badge', async ({ page }) => {
    // 1. Create Person via API
    const firstName = `E2E_${Date.now()}`;
    const person = await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active'
    });

    // 2. Verify backend: CanAuthenticate should be true
    let details = await admin.getPersonDetails(page, person.id);
    expect(details.canAuthenticate, 'Active: should auth').toBe(true);

    // 3. Navigate to People list
    await page.goto('https://localhost:7035/Admin/People');

    // 4. Search for the person (wait for API response)
    await page.fill('[data-test-id="person-search"]', firstName);
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'GET', { timeout: 10000 });

    // 5. Verify Active badge
    const row = page.locator('tr', { hasText: firstName }).first();
    const badge = row.locator('[data-test-id="person-status-badge"]');
    await expect(badge).toContainText(/Active|啟用中/);
    await expect(badge).toHaveClass(/bg-green-100/);

    // 6. Click Edit, change to Suspended, Save
    await row.locator('[data-test-id="person-edit-button"]').click();
    await page.waitForSelector('#status', { timeout: 5000 });
    await page.selectOption('#status', 'Suspended');
    await page.click('button:has-text("Save"), button:has-text("儲存")');
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'PUT', { timeout: 10000 });

    // 7. Verify backend: CanAuthenticate should be false
    details = await admin.getPersonDetails(page, person.id);
    expect(details.canAuthenticate, 'Suspended: should NOT auth').toBe(false);

    // 8. Refresh the list to see updated badge
    // Clear search first to force a fresh search and API call
    await page.fill('[data-test-id="person-search"]', '');
    await page.waitForTimeout(300); // debounce
    await page.fill('[data-test-id="person-search"]', firstName);
    await page.waitForResponse(r => r.url().includes('/api/admin/people') && r.request().method() === 'GET', { timeout: 10000 });

    // 9. Verify Suspended badge
    const updatedBadge = page.locator('tr', { hasText: firstName }).first().locator('[data-test-id="person-status-badge"]');
    await expect(updatedBadge).toContainText(/Suspended|已停權/);
    await expect(updatedBadge).toHaveClass(/bg-orange-100/);
  });
});
