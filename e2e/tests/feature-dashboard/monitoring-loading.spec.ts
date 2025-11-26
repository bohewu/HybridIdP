import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Monitoring / SecurityMetrics loading', () => {
  test('shows loading indicator while system-metrics API is delayed', async ({ page, browserName }) => {
    // Ensure admin session ready
    await adminHelpers.loginAsAdminViaIdP(page);

    // Intercept the system-metrics API and add a delay so we can assert the loading indicator.
    // Additionally, for Chromium we enable CDP-level network throttling for a more realistic slowdown.
    await page.route('**/api/admin/monitoring/system-metrics', async (route) => {
      // artificial delay to give the UI time to show the loading state
      await new Promise((r) => setTimeout(r, 1500));
      await route.continue();
    });

    let client: any | undefined
    if (browserName === 'chromium') {
      // Set up Chromium CDP network emulation to add latency / low throughput.
      // This helps catch UI loading indicators that depend on slow networks.
      client = await page.context().newCDPSession(page)
      await client.send('Network.enable')
      await client.send('Network.emulateNetworkConditions', {
        offline: false,
        latency: 1500, // ms
        downloadThroughput: 50 * 1024, // ~50kb/s
        uploadThroughput: 50 * 1024
      })
    }

    // Navigate to the Dashboard which contains the SecurityMetrics component
    await page.goto('https://localhost:7035/Admin/Dashboard');

    const loading = page.locator('[data-testid="loading-indicator"]');

    // Expect loading indicator to appear while the API is delayed
    await expect(loading).toBeVisible({ timeout: 3000 });

    // After the API completes the loading indicator should disappear and metrics should render
    await expect(loading).toBeHidden({ timeout: 7000 });

    // Check that at least one metric section is visible (gauges/counters/histograms)
    const metricChart = page.locator('.metric-chart').first();
    await expect(metricChart).toBeVisible({ timeout: 5000 });

    if (client) {
      // Reset CDP network emulation
      await client.send('Network.disable')
      await client.detach()
    }
  });
});
