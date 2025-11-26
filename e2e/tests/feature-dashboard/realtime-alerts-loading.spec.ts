import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Monitoring / RealTimeAlerts loading', () => {
  test('shows loading indicator while alerts API is delayed', async ({ page, browserName }) => {
    await adminHelpers.loginAsAdminViaIdP(page);

    await page.route('**/api/admin/monitoring/alerts', async (route) => {
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

    const loading = page.locator('.real-time-alerts [data-testid="loading-indicator"]');
    await expect(loading).toBeVisible({ timeout: 3000 });
    await expect(loading).toBeHidden({ timeout: 7000 });

    const alertsList = page.locator('.alert-item').first();
    // it's okay if there are no alerts, we just want the UI to render appropriately
    await expect(page.locator('.real-time-alerts')).toBeVisible({ timeout: 5000 });

    if (client) {
      await client.send('Network.disable')
      await client.detach()
    }
  })
})
