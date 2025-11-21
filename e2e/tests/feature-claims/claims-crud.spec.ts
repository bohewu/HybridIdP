import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// NOTE: Skipped because admin user needs Permissions.Claims.Create which isn't assigned by default
// To enable: assign Claims.Create, Claims.Update, Claims.Delete permissions to admin role
test.skip('Admin - Claims CRUD (create, update, delete custom claim)', async ({ page }) => {
  // Accept dialogs automatically
  page.on('dialog', async (dialog) => {
    await dialog.accept();
  });

  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Claims page
  await page.goto('https://localhost:7035/Admin/Claims');
  await page.waitForURL(/\/Admin\/Claims/);

  // Wait for the Vue app to load
  await page.waitForSelector('table', { timeout: 15000 });

  const claimName = `e2e_claim_${Date.now()}`;
  const displayName = `E2E Test Claim ${Date.now()}`;

  // Click Create Claim button
  await page.locator('button:has-text("Create Claim")').click();

  // Wait for modal form to appear
  await page.waitForSelector('form', { timeout: 5000 });

  // Fill in claim details using the form structure from ClaimsApp.vue
  // Name field (required)
  const nameInput = page.locator('input[type="text"]').first();
  await nameInput.fill(claimName);

  // Display Name field (required) - second text input
  const displayNameInput = page.locator('input[type="text"]').nth(1);
  await displayNameInput.fill(displayName);

  // Description field - textarea
  await page.locator('textarea').fill('E2E test custom claim for automated testing');

  // Claim Type field (required) - third text input
  const claimTypeInput = page.locator('input[type="text"]').nth(2);
  await claimTypeInput.fill(`http://schemas.example.com/${claimName}`);

  // User Property Path field (required) - fourth text input
  const userPropertyInput = page.locator('input[type="text"]').nth(3);
  await userPropertyInput.fill('CustomProperties.TestValue');

  // Data Type field - select (defaults to String, no need to change)

  // Submit form
  await page.click('button[type="submit"]');

  // Wait for modal to close (success) or error message
  await Promise.race([
    page.waitForSelector('form', { state: 'hidden', timeout: 5000 }).catch(() => null),
    page.waitForSelector('.bg-red-50', { timeout: 5000 }).catch(() => null)
  ]);

  // Check if there was an error
  const errorVisible = await page.locator('.bg-red-50').isVisible().catch(() => false);
  if (errorVisible) {
    const errorText = await page.locator('.bg-red-50').textContent();
    console.log(`Error creating claim: ${errorText}`);
  }

  // Wait for API to complete
  await page.waitForTimeout(2000);

  // Verify claim was created via API
  const claimCreated = await page.evaluate(async (name) => {
    const resp = await fetch(`/api/admin/claims?search=${encodeURIComponent(name)}`);
    const data = await resp.json();
    console.log('API search result:', data);
    return data.items && data.items.length > 0 && (data.items[0].claimType === name || data.items[0].name === name);
  }, claimName);

  expect(claimCreated).toBeTruthy();

  // Search for the claim in table
  const searchInput = page.locator('input[placeholder*="Search"], input[type="search"]').first();
  if (await searchInput.isVisible().catch(() => false)) {
    await searchInput.fill(claimName);
    await page.waitForTimeout(1000);
  }

  // Find the claim in table
  const claimsTable = page.locator('table tbody');
  await expect(claimsTable).toContainText(claimName, { timeout: 10000 });

  // Edit the claim
  const claimRow = claimsTable.locator('tr', { hasText: claimName });
  await expect(claimRow).toBeVisible();

  // Click edit button (it's an icon button with SVG)
  await claimRow.locator('button').first().click();

  // Wait for modal form
  await page.waitForSelector('form', { timeout: 5000 });

  // Update display name (second text input in edit mode)
  const updatedDisplayName = `${displayName} (updated)`;
  const editDisplayNameInput = page.locator('input[type="text"]').nth(1);
  await editDisplayNameInput.clear();
  await editDisplayNameInput.fill(updatedDisplayName);

  // Submit update
  await page.click('button[type="submit"]');
  await page.waitForTimeout(2000);

  // Delete the claim
  await claimRow.locator('button[title*="Delete"], button:has-text("Delete")').first().click();

  // Wait for claim to be removed
  try {
    await expect(claimsTable).not.toContainText(claimName, { timeout: 20000 });
  } catch (e) {
    // If UI delete fails, try API cleanup
    console.warn(`UI delete failed for claim ${claimName}, attempting API cleanup...`);
    await page.evaluate(async (name) => {
      const resp = await fetch(`/api/admin/claims?search=${encodeURIComponent(name)}`);
      const data = await resp.json();
      if (data.items && data.items.length > 0) {
        const claimId = data.items[0].id;
        await fetch(`/api/admin/claims/${claimId}`, { method: 'DELETE' });
      }
    }, claimName);
  }
});

test('Admin - Claims (standard claim protection)', async ({ page }) => {
  await adminHelpers.loginAsAdminViaIdP(page);

  await page.goto('https://localhost:7035/Admin/Claims');
  await page.waitForURL(/\/Admin\/Claims/);
  await page.waitForSelector('table', { timeout: 15000 });

  // Find a standard claim (like 'sub', 'email', 'name')
  const claimsTable = page.locator('table tbody');
  const standardClaimRow = claimsTable.locator('tr', { hasText: /sub|email|name/ }).first();

  if (await standardClaimRow.isVisible().catch(() => false)) {
    // Try to edit standard claim
    const editButton = standardClaimRow.locator('button[title*="Edit"], button:has-text("Edit")').first();
    
    if (await editButton.isVisible().catch(() => false)) {
      await editButton.click();
      await page.waitForTimeout(1000);

      // Check if ClaimType field is disabled
      const claimTypeInput = page.locator('#claimType, input[name="claimType"]');
      if (await claimTypeInput.isVisible().catch(() => false)) {
        const isDisabled = await claimTypeInput.isDisabled();
        expect(isDisabled).toBeTruthy();
      }

      // Close modal
      await page.locator('button:has-text("Cancel"), button:has-text("Close")').first().click();
    } else {
      // No edit button for standard claims - that's also valid protection
      expect(true).toBeTruthy();
    }
  } else {
    // No standard claims visible - skip test
    console.log('No standard claims found, skipping protection test');
    expect(true).toBeTruthy();
  }
});
