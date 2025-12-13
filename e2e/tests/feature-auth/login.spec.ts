import { test, expect } from '@playwright/test';

// Disable default storage state (admin.json) to ensure we start unauthenticated
test.use({ storageState: { cookies: [], origins: [] } });

test.describe('Authentication Flow', () => {
  test('login flow: IdP login (admin) and authenticated state', async ({ page }) => {
    // Start directly at the IdP login page
    await page.goto('https://localhost:7035/');

    // If not authenticated we should be redirected to the login page
    await expect(page).toHaveURL(/https:\/\/localhost:7035\/Account\/Login/);

    // Fill in credentials - using robust name attributes that work regardless of data-test-id
    await page.fill('input[name="Input.Login"]', 'admin@hybridauth.local');
    await page.fill('input[name="Input.Password"]', 'Admin@123');

    // Submit the form
    await page.click('button[type="submit"]');

    // After login we should be authenticated on the IdP and see the user name in the header
    await page.waitForSelector('.user-name', { timeout: 20000 });
    await expect(page.locator('.user-name')).toContainText('admin@hybridauth.local');
  });

  test('login flow: Invalid password shows error', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Login');

    await page.fill('input[name="Input.Login"]', 'admin@hybridauth.local');
    await page.fill('input[name="Input.Password"]', 'Wrong@123');
    await page.click('button[type="submit"]');

    // Expect validation summary
    // Support both Bootstrap alert and legacy validation classes and data-test-id
    const errorSummary = page.locator('.validation-summary-errors, .alert-danger, [data-test-id="login-error-summary"]');
    await expect(errorSummary.first()).toBeVisible();
    await expect(errorSummary.first()).toContainText('Invalid login attempt');

    // Should still be on login page
    await expect(page).toHaveURL(/Account\/Login/);
  });

  test('login flow: Non-existent user', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Login');

    await page.fill('input[name="Input.Login"]', 'nobody@hybridauth.local');
    await page.fill('input[name="Input.Password"]', 'Any@123');
    await page.click('button[type="submit"]');

    const errorSummary = page.locator('.validation-summary-errors, .alert-danger, [data-test-id="login-error-summary"]');
    await expect(errorSummary.first()).toBeVisible();
  });

  test('login flow: Empty fields validation', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Login');

    // Clear fields if auto-filled
    await page.fill('input[name="Input.Login"]', '');
    await page.fill('input[name="Input.Password"]', '');
    await page.click('button[type="submit"]');

    // Field level validation
    // Use [data-valmsg-for="Input.Login"] which is standard ASP.NET Core unobtrusive validation attribute
    // Combined with fallback to ID or data-test-id
    const userError = page.locator('[data-valmsg-for="Input.Login"], #Input_Login-error, [data-test-id="login-username-error"]');
    await expect(userError.first()).toBeVisible();

    const passError = page.locator('[data-valmsg-for="Input.Password"], #Input_Password-error, [data-test-id="login-password-error"]');
    await expect(passError.first()).toBeVisible();
  });

  test('login page: Visual checks', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Login');

    // Check "Remember Me"
    await expect(page.locator('input[name="Input.RememberMe"]')).toBeVisible();
  });
});
