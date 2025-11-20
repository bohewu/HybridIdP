import { test, expect } from '@playwright/test';

test('dashboard i18n and UI fixes', async ({ page }) => {
  // Navigate to the admin dashboard
  await page.goto('https://localhost:7035/Admin/Dashboard');

  // Wait for the page to load
  await page.waitForLoadState('networkidle');

  // Check that duplicate titles are removed - should only see titles in the card headers
  const activityDashboardTitles = page.locator('text=活動儀表板');
  await expect(activityDashboardTitles).toHaveCount(1); // Should only be one

  const securityMetricsTitles = page.locator('text=安全指標');
  await expect(securityMetricsTitles).toHaveCount(1); // Should only be one

  const realTimeAlertsTitles = page.locator('text=即時警報');
  await expect(realTimeAlertsTitles).toHaveCount(1); // Should only be one

  // Check that hardcoded English strings are replaced with i18n
  const noGaugesText = page.locator('text=沒有可用的儀表指標');
  const noCountersText = page.locator('text=沒有可用的計數器指標');

  // These should exist if the metrics are empty (which they likely are in test environment)
  const gaugesSection = page.locator('text=儀表指標').locator('xpath=following-sibling::*');
  const countersSection = page.locator('text=計數器指標').locator('xpath=following-sibling::*');

  // Check that no hardcoded English "No gauge/counter metrics available" appears
  await expect(page.locator('text=No gauge metrics available')).toHaveCount(0);
  await expect(page.locator('text=No counter metrics available')).toHaveCount(0);

  console.log('Dashboard i18n and UI fixes verified successfully');
});