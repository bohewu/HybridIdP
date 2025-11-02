import { test, expect } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';

test.describe('Legacy + JIT login flow', () => {
  test('success path: login with dev stub then consent', async ({ page }) => {
    // Go to TestClient home
    await page.goto('/');

    // Click Login (nav item triggers OIDC challenge)
    await page.getByRole('link', { name: 'Login' }).click();

    // On IdP login page
    await expect(page).toHaveURL(new RegExp(`${IDP_BASE}/Account/Login`));
    await page.getByLabel('Email').fill('jane@example.com');
    await page.getByLabel('Password').fill('LegacyDev@123');
    await page.getByRole('button', { name: 'Login' }).click();

    // Consent screen
    await expect(page).toHaveTitle(/Authorize Application/i);
    await page.getByRole('button', { name: 'Allow Access' }).click();

    // Back to TestClient profile
    await expect(page).toHaveURL(/https:\/\/localhost:7001\/Account\/Profile/);
    await expect(page.getByRole('heading', { name: 'User Profile' })).toBeVisible();
    // Basic presence: email should be present as a claim value
    await expect(page.getByText('jane@example.com')).toBeVisible();
    // Enriched claims should be present in the table (from JIT + custom principal factory)
    await expect(page.getByText('preferred_username')).toBeVisible();
    await expect(page.getByText('department')).toBeVisible();
    await expect(page.getByText('IT')).toBeVisible();
  });

  test('negative: invalid legacy password shows error and stays on login', async ({ page }) => {
    // Go to TestClient home
    await page.goto('/');

    // Click Login
    await page.getByRole('link', { name: 'Login' }).click();

    // On IdP login page
    await expect(page).toHaveURL(new RegExp(`${IDP_BASE}/Account/Login`));
    await page.getByLabel('Email').fill('jane@example.com');
    await page.getByLabel('Password').fill('WrongPwd123');
    await page.getByRole('button', { name: 'Login' }).click();

    // Expect error displayed and still on login page
    await expect(page).toHaveURL(new RegExp(`${IDP_BASE}/Account/Login`));
    await expect(page.getByText('Invalid login attempt.')).toBeVisible();
  });
});
