import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Dashboard main page loading (v-loading directive)', () => {
  test('shows page-level loading overlay when dashboard stats are slow', async ({ page, browserName }) => {
    await adminHelpers.loginAsAdminViaIdP(page);

    // Delay the dashboard stats API
    await page.route('**/api/admin/dashboard/stats', async (route) => {
      await new Promise((r) => setTimeout(r, 1200));
      await route.continue();
    });

    let client: any | undefined
    if (browserName === 'chromium') {
      client = await page.context().newCDPSession(page)
      await client.send('Network.enable')
      await client.send('Network.emulateNetworkConditions', {
        offline: false,
        latency: 800,
        downloadThroughput: 50 * 1024,
        uploadThroughput: 50 * 1024
      })
    }

    await page.goto('https://localhost:7035/Admin/Dashboard');

    // target specifically the overlay instance created by the directive
    const pageLoading = page.locator('.dashboard-root .loading-indicator.loading-overlay')
    await expect(pageLoading).toBeVisible({ timeout: 3000 })
    await expect(pageLoading).toBeHidden({ timeout: 7000 })

    // cleanup
    if (client) {
      await client.send('Network.disable')
      await client.detach()
    }
  })
})
