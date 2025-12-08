import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test('Admin - Settings CRUD (branding settings)', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Settings page
  await page.goto('https://localhost:7035/Admin/Settings');
  await page.waitForURL(/\/Admin\/Settings/);

  // Wait for the Vue app to load
  await page.waitForSelector('button:has-text("Save Changes"), input#appName', { timeout: 15000 });

  // Get original values
  const originalAppName = await page.inputValue('#appName');
  const originalProductName = await page.inputValue('#productName');

  // Update branding settings
  const timestamp = Date.now();
  const newAppName = `E2E Test App ${timestamp}`;
  const newProductName = `E2E Test Product ${timestamp}`;

  await page.fill('#appName', newAppName);
  await page.fill('#productName', newProductName);

  // Save changes
  await page.click('button:has-text("Save Changes")');

  // Wait for success message
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });

  // Refresh page to verify persistence
  await page.reload();
  await page.waitForSelector('input#appName', { timeout: 15000 });

  // Verify values persisted
  await expect(page.locator('#appName')).toHaveValue(newAppName);
  await expect(page.locator('#productName')).toHaveValue(newProductName);

  // Restore original values
  await page.fill('#appName', originalAppName);
  await page.fill('#productName', originalProductName);
  await page.click('button:has-text("Save Changes")');
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });
});

test('Admin - Settings validation (empty fields)', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  await page.goto('https://localhost:7035/Admin/Settings');
  await page.waitForURL(/\/Admin\/Settings/);
  await page.waitForSelector('input#appName', { timeout: 15000 });

  // Try to save with empty app name
  await page.fill('#appName', '');
  await page.click('button:has-text("Save Changes")');

  // Should either show validation error or save button disabled
  const hasError = await Promise.race([
    page.locator('.bg-red-50, .text-red-600, .text-red-700').first().isVisible({ timeout: 3000 }).then(() => true).catch(() => false),
    page.locator('button:has-text("Save Changes"):disabled').isVisible({ timeout: 3000 }).then(() => true).catch(() => false)
  ]);

  expect(hasError).toBeTruthy();
});

test('Admin - System Monitoring Settings', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Settings page
  await page.goto('https://localhost:7035/Admin/Settings');
  
  // Wait for monitoring section
  // Uses the i18n key "admin.settings.system.title" -> "System Monitoring" (en-US)
  // Or "Enable Monitoring" toggle
  await page.waitForSelector('text=System Monitoring', { timeout: 15000 });

  // Verify visibility of key elements
  await expect(page.getByText('System Monitoring')).toBeVisible();
  await expect(page.getByText('Enable Monitoring')).toBeVisible();
  await expect(page.getByText('Activity Interval')).toBeVisible();
  
  // Check input types for intervals
  // They are now standard inputs, type number
  const activityInput = page.locator('input[type="number"]').first();
  await expect(activityInput).toBeVisible();

  // Test Interaction: Toggle monitoring (enable/disable)
  // The toggle is now a standard checkbox input styled with classes, but still Type="checkbox"
  // It is the first checkbox in the monitoring section
  const enableToggle = page.locator('input[type="checkbox"]').first(); 
  
  // Force click because the input itself might be covered by styled label or have opacity 0 with DaisyUI/Tailwind
  // But we can check if it is checked
  const wasEnabled = await enableToggle.isChecked();
  
  // Click the label associated or just force click the input
  await enableToggle.click({ force: true });
  
  // Wait for button to be enabled (hasChanges = true)
  await expect(page.locator('button[data-testid="save-settings-btn"]:not([disabled])')).toBeVisible();

  // Save
  await page.click('button[data-testid="save-settings-btn"]');
  
  // Expect success message
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });

  // Restore state
  await enableToggle.click({ force: true });
  await page.click('button[data-testid="save-settings-btn"]');
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });
});

test('Admin - Logging Settings', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Settings page
  await page.goto('https://localhost:7035/Admin/Settings');
  
  // Wait for page to load
  await page.waitForURL(/\/Admin\/Settings/);
  await page.waitForSelector('text=System Settings', { timeout: 15000 });

  // Locate Logging Section
  const loggingSection = page.locator('div:has-text("Logging Settings")').first();
  await expect(loggingSection).toBeVisible();

  // Find the select element for log level
  const levelSelect = page.getByLabel('Global Log Level');
  await expect(levelSelect).toBeVisible();

  // Get current value
  const originalLevel = await levelSelect.inputValue();
  console.log(`Original Log Level: ${originalLevel}`);

  // Change to 'Debug' (or 'Information' if it was Debug)
  const newLevel = originalLevel === 'Debug' ? 'Information' : 'Debug';
  await levelSelect.selectOption(newLevel);
  
  // Find the specific Save Changes button for logging settings
  // Based on LoggingSettings.vue, button is enabled only when hasChanges is true
  const enabledSaveButton = page.locator('button:has-text("Save Changes"):not([disabled])');
  await expect(enabledSaveButton).toBeVisible();
  
  await enabledSaveButton.click();

  // Wait for success message
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });

  // Verify persistence
  await page.reload();
  await page.waitForSelector('text=Logging Settings', { timeout: 15000 });
  
  const reloadedSelect = page.getByLabel('Global Log Level');
  await expect(reloadedSelect).toHaveValue(newLevel);

  // Cleanup: Restore original level
  await reloadedSelect.selectOption(originalLevel);
  await enabledSaveButton.click();
  await expect(page.locator('.bg-green-50').first()).toBeVisible({ timeout: 10000 });
});
