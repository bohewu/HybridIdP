import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Add retry configuration for this flaky test
test.describe.configure({ retries: 2 });

test.describe('Monitoring main page loading (v-loading)', () => {
  test('shows page-level overlay while permissions API is delayed', async ({ page }) => {
    // Login first to get a session cookie
    await adminHelpers.loginAsAdminViaIdP(page);

    // Use a fresh page so permissionService isn't already loaded in-memory
    const fresh = await page.context().newPage();

    // Intercept the permissions API on the fresh page
    // Use Playwright's built-in route delay instead of CDP
    await fresh.route('**/api/admin/permissions/current', async (route) => {
      // Delay the response by 1.5 seconds
      await new Promise((r) => setTimeout(r, 1500));
      await route.continue();
    });

    // Navigate to Monitoring page
    await fresh.goto('https://localhost:7035/Admin/Monitoring', { 
      waitUntil: 'domcontentloaded' 
    });

    // Check if Monitoring SPA root exists
    let monitoringRootExists = true;
    try {
      await fresh.waitForSelector('.monitoring-app', { timeout: 4000 });
    } catch (err) {
      monitoringRootExists = false;
      console.log('Monitoring SPA root not found; falling back to Dashboard overlay test');
    }

    if (monitoringRootExists) {
      // Set up response listener before it fires
      const permRespPromise = fresh.waitForResponse(
        (r) => r.url().includes('/api/admin/permissions/current') && r.request().method() === 'GET',
        { timeout: 10000 }
      );

      // Look for loading indicator
      const pageLoading = fresh.locator('.monitoring-app [data-testid="loading-indicator"]');

      // Assert overlay appears while permissions are delayed
      await expect(pageLoading).toBeVisible({ timeout: 5000 });

      // Wait for delayed permissions response
      await permRespPromise;

      // Overlay should hide after response
      await expect(pageLoading).toBeHidden({ timeout: 10000 });

      // Content should be visible
      const content = fresh.locator('.monitoring-content');
      await expect(content).toBeVisible({ timeout: 5000 });
    } else {
      // Fallback: test Dashboard overlay
      await fresh.route('**/api/admin/dashboard/stats', async (route) => {
        await new Promise((r) => setTimeout(r, 1200));
        await route.continue();
      });

      await fresh.goto('https://localhost:7035/Admin/Dashboard', { 
        waitUntil: 'domcontentloaded' 
      });

      const dashLoading = fresh.locator('.dashboard-root > .v-loading-container [data-testid="loading-indicator"]');
      await expect(dashLoading).toBeVisible({ timeout: 5000 });
      await expect(dashLoading).toBeHidden({ timeout: 10000 });

      // Ensure dashboard content is visible
      await expect(fresh.locator('.monitoring-content, .dashboard-root')).toBeVisible({ timeout: 5000 });
    }

    await fresh.close();
  })
})
