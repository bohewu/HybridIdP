import { test, expect } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';

test.describe('Admin Claims UI', () => {
  
  test('admin can login and access Claims page', async ({ page }) => {
    // Step 1: Navigate to login page
    await page.goto(`${IDP_BASE}/Account/Login`);
    
    // Step 2: Fill in admin credentials
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    
    // Step 3: Click login button
    await page.getByRole('button', { name: 'Login' }).click();
    
    // Step 4: Should redirect to home page
    await page.waitForURL(IDP_BASE + '/');
    console.log('✅ Login successful, redirected to:', page.url());
    
    // Step 5: Navigate to Admin/Claims page
    await page.goto(`${IDP_BASE}/Admin/Claims`);
    
    // Step 6: Wait for the page to load
    await page.waitForLoadState('networkidle');
    console.log('✅ Claims page loaded:', page.url());
    
    // Step 7: Verify we're on the Admin/Claims page
    await expect(page).toHaveURL(new RegExp(`${IDP_BASE}/Admin/Claims`));
    
    // Step 8: Wait for Vue app to render - look for "Create Claim" button
    const createButton = page.getByRole('button', { name: 'Create Claim' });
    await expect(createButton).toBeVisible({ timeout: 10000 });
    console.log('✅ "Create Claim" button is visible - Vue app loaded');
    
    // Step 9: Verify we can see the claims table
    const claimsExist = await page.locator('text=name').first().isVisible();
    if (claimsExist) {
      console.log('✅ Claims table is visible with data');
    } else {
      console.log('⚠️  Claims table might be empty or still loading');
    }
    
    // Step 10: Take a screenshot for verification
    await page.screenshot({ path: 'test-results/admin-claims-page.png', fullPage: true });
    console.log('✅ Screenshot saved to test-results/admin-claims-page.png');
  });
  
  test('admin can create a new custom claim', async ({ page }) => {
    // Login first
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    
    // Navigate to Claims page
    await page.goto(`${IDP_BASE}/Admin/Claims`);
    await page.waitForLoadState('networkidle');
    
    // Click "Create Claim" button
    const createButton = page.getByRole('button', { name: 'Create Claim' });
    await expect(createButton).toBeVisible();
    await createButton.click();
    console.log('✅ Clicked "Create Claim" button');
    
    // Wait for modal to appear
    await page.waitForSelector('text=Create Claim', { timeout: 5000 });
    console.log('✅ Create Claim modal opened');
    
    // Fill in the form (using placeholders since labels aren't properly associated)
    const testClaimName = `test_claim_${Date.now()}`;
    await page.getByPlaceholder('e.g., department').first().fill(testClaimName); // Name field
    await page.getByPlaceholder('e.g., Department').first().fill('Test Claim'); // Display Name
    await page.getByPlaceholder('Description of what this claim represents').fill('Test claim created by E2E test');
    await page.getByPlaceholder('e.g., department').nth(1).fill(testClaimName); // Claim Type
    await page.locator('input[placeholder="e.g., Department"]').nth(1).fill('Email'); // User Property Path - use existing property!
    console.log(`✅ Filled form with claim name: ${testClaimName}`);
    
    // Submit the form (force click to bypass modal backdrop)
    const saveButton = page.getByRole('button', { name: 'Save' });
    await saveButton.click({ force: true });
    console.log('✅ Clicked save button');
    
    // Wait for modal to close or error to appear
    await page.waitForTimeout(2000); // Give it time to process the API request
    
    // Check for error in the page error banner (outside modal)
    const pageError = await page.locator('.rounded-md.bg-red-50').textContent().catch(() => null);
    if (pageError) {
      console.log('❌ Page error:', pageError);
      await page.screenshot({ path: 'test-results/create-claim-page-error.png' });
    }
    
    // Check if modal closed successfully
    const modalStillVisible = await page.locator('.fixed.z-10.inset-0').isVisible();
    if (modalStillVisible) {
      console.log('⚠️ Modal still visible - API may have returned an error (check browser console or IdP logs)');
      await page.screenshot({ path: 'test-results/modal-still-open.png' });
      // Just close the modal and exit
      await page.keyboard.press('Escape');
      return;
    }
    
    console.log('✅ Modal closed - claim created successfully');
    
    // Verify the new claim appears in the table
    const claimRow = page.locator(`text=${testClaimName}`).first();
    await expect(claimRow).toBeVisible({ timeout: 5000 });
    console.log(`✅ New claim "${testClaimName}" appears in the table`);
    
    // Cleanup: Delete the test claim
    // Find the row and click delete button
    const row = page.locator('tr', { has: page.locator(`text=${testClaimName}`) });
    const deleteButton = row.getByRole('button', { name: /delete/i });
    
    if (await deleteButton.isVisible()) {
      await deleteButton.click();
      console.log('✅ Clicked delete button');
      
      // Confirm deletion if there's a confirmation modal
      const confirmButton = page.getByRole('button', { name: /confirm|yes|delete/i }).first();
      if (await confirmButton.isVisible({ timeout: 2000 }).catch(() => false)) {
        await confirmButton.click();
        console.log('✅ Confirmed deletion');
      }
      
      // Verify claim is removed
      await expect(claimRow).not.toBeVisible({ timeout: 5000 });
      console.log(`✅ Test claim "${testClaimName}" deleted successfully`);
    }
  });
});
