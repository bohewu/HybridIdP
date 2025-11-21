import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

test.describe('Admin - Scopes negative tests', () => {
  test.beforeEach(async ({ page }) => {
    page.on('dialog', async (dialog) => await dialog.accept());
    await adminHelpers.loginAsAdminViaIdP(page);
    await page.goto('https://localhost:7035/Admin/Scopes');
    await page.waitForURL(/\/Admin\/Scopes/);
    // Wait for Vue app to load
    await page.waitForSelector('button:has-text("Create New Scope"), ul[role="list"]', { timeout: 15000 });
  });

  test('Validation error - missing required fields', async ({ page }) => {
    // Open the Create Scope form
    await page.click('button:has-text("Create New Scope")');
    await page.waitForSelector('#name');

    // Leave name blank and try to submit
    await page.fill('#name', '');
    await page.fill('#displayName', 'Test Scope');
    await page.click('button[type="submit"]');

    // Check for validation error message
    await expect(page.locator('text=/required|cannot be empty/i')).toBeVisible({ timeout: 5000 });
  });

  test('Validation error - invalid scope name format', async ({ page }) => {
    // Open the Create Scope form
    await page.click('button:has-text("Create New Scope")');
    await page.waitForSelector('#name');

    // Try to create scope with invalid characters (spaces not allowed in scope names)
    await page.fill('#name', 'invalid scope name');
    await page.fill('#displayName', 'Invalid Scope');
    await page.click('button[type="submit"]');

    // Check for validation or error message
    // This may show as a validation error or API error depending on implementation
    const errorExists = await Promise.race([
      page.locator('text=/invalid|error|cannot/i').isVisible({ timeout: 5000 }).then(() => true).catch(() => false),
      page.locator('[role="alert"]').isVisible({ timeout: 5000 }).then(() => true).catch(() => false)
    ]);

    expect(errorExists).toBeTruthy();
  });

  test('Duplicate scope name shows error', async ({ page }) => {
    const scopeName = `e2e-dup-scope-${Date.now()}`;
    
    // Create the first scope via API
    const scopeId = await page.evaluate(async (name) => {
      const payload = {
        name,
        displayName: `E2E Duplicate Test ${name}`,
        description: 'First scope for duplicate test',
        resources: []
      };
      const response = await fetch('/api/admin/scopes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await response.json();
      return data.id;
    }, scopeName);

    // Now try to create another scope with the same name via UI
    await page.click('button:has-text("Create New Scope")');
    await page.waitForSelector('#name');
    await page.fill('#name', scopeName);
    await page.fill('#displayName', 'Duplicate Scope');
    await page.click('button[type="submit"]');

    // Check for duplicate error message
    await expect(page.locator('text=/already exists|duplicate/i')).toBeVisible({ timeout: 5000 });

    // Cleanup: delete the scope via API
    try {
      await page.evaluate(async (id) => {
        await fetch(`/api/admin/scopes/${id}`, { method: 'DELETE' });
      }, scopeId);
    } catch (e) {
      // Ignore cleanup errors
    }
  });

  test('Empty display name is allowed but name is required', async ({ page }) => {
    // Open the Create Scope form
    await page.click('button:has-text("Create New Scope")');
    await page.waitForSelector('#name');

    const scopeName = `e2e-no-display-${Date.now()}`;

    // Fill only name, leave displayName empty
    await page.fill('#name', scopeName);
    await page.fill('#displayName', ''); // explicitly empty
    await page.click('button[type="submit"]');

    // This should succeed (displayName is optional)
    const scopesList = page.locator('ul[role="list"]');
    await expect(scopesList).toContainText(scopeName, { timeout: 20000 });

    // Cleanup
    try {
      const scopeId = await page.evaluate(async (name) => {
        const resp = await fetch(`/api/admin/scopes?search=${name}`);
        const data = await resp.json();
        return data.items && data.items.length > 0 ? data.items[0].id : null;
      }, scopeName);
      
      if (scopeId) {
        await page.evaluate(async (id) => {
          await fetch(`/api/admin/scopes/${id}`, { method: 'DELETE' });
        }, scopeId);
      }
    } catch (e) {
      // Ignore cleanup errors
    }
  });
});
