import { test, expect } from '@playwright/test';

const IDP_BASE = 'https://localhost:7035';
const TESTCLIENT_BASE = 'https://localhost:7001';

test.describe('TestClient Scope-Mapped Claims', () => {
  
  test('scope-mapped claim appears in TestClient Profile after login', async ({ page }) => {
    // Step 1: Navigate to TestClient
    await page.goto(TESTCLIENT_BASE);
    console.log('✅ Opened TestClient home page');
    
    // Step 2: Click Profile link (triggers login redirect)
    await page.getByRole('link', { name: 'Profile' }).click();
    console.log('✅ Clicked Profile link - should redirect to IdP login');
    
    // Step 3: Wait for IdP login page
    await page.waitForURL(/localhost:7035.*Login/);
    console.log('✅ Redirected to IdP login page');
    
    // Step 4: Fill in credentials
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    console.log('✅ Submitted login credentials');
    
    // Step 5: Handle consent page if it appears
    const consentButton = page.getByRole('button', { name: 'Allow Access' });
    if (await consentButton.isVisible({ timeout: 3000 }).catch(() => false)) {
      await consentButton.click();
      console.log('✅ Consented to access - waiting for redirect');
    } else {
      console.log('ℹ️  No consent page shown (already authorized)');
    }
    
    // Step 6: Wait for redirect back to TestClient Profile
    await page.waitForURL(new RegExp(`${TESTCLIENT_BASE}/Account/Profile`), { timeout: 10000 });
    console.log('✅ Redirected back to TestClient Profile page');
    
    // Step 7: Wait for page to fully load
    await page.waitForLoadState('networkidle');
    
    // Step 8: Verify the custom scope-mapped claim is present
    const customClaimRow = page.locator('tr', { 
      has: page.locator('text=e2e_scope_claim') 
    });
    
    await expect(customClaimRow).toBeVisible({ timeout: 5000 });
    console.log('✅ Custom claim "e2e_scope_claim" found in claims table');
    
    // Step 9: Verify the claim has a value (should be user's email: admin@hybridauth.local)
    const claimValue = customClaimRow.locator('td').nth(1);
    const valueText = await claimValue.textContent();
    console.log(`✅ Claim value: "${valueText}"`);
    
    expect(valueText).toContain('admin@hybridauth.local');
    console.log('✅ Claim value matches expected user email');
    
    // Step 10: Take a screenshot for verification
    await page.screenshot({ path: 'test-results/testclient-profile-with-custom-claim.png', fullPage: true });
    console.log('✅ Screenshot saved to test-results/testclient-profile-with-custom-claim.png');
  });
  
  test('full E2E: create claim, map to scope, verify in token', async ({ page }) => {
    const timestamp = Date.now();
    const claimName = `e2e_test_${timestamp}`;
    const claimType = `e2e_test_${timestamp}`;
    
    console.log(`🔧 Creating test claim: ${claimName}`);
    
    // === PART 1: Login to IdP Admin ===
    await page.goto(`${IDP_BASE}/Account/Login`);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    await page.waitForURL(IDP_BASE + '/');
    console.log('✅ Logged into IdP Admin');
    
    // === PART 2: Create a new custom claim ===
    await page.goto(`${IDP_BASE}/Admin/Claims`);
    await page.waitForLoadState('networkidle');
    
    const createButton = page.getByRole('button', { name: 'Create Claim' });
    await expect(createButton).toBeVisible();
    await createButton.click();
    console.log('✅ Opened Create Claim modal');
    
    // Wait for modal to appear
    await page.waitForSelector('text=Create Claim', { timeout: 5000 });
    
    // Fill in the form
    await page.getByPlaceholder('e.g., department').first().fill(claimName); // Name
    await page.getByPlaceholder('e.g., Department').first().fill(`Test ${claimName}`); // Display Name
    await page.getByPlaceholder('Description of what this claim represents').fill(`E2E test claim ${timestamp}`);
    await page.getByPlaceholder('e.g., department').nth(1).fill(claimType); // Claim Type
    await page.locator('input[placeholder="e.g., Department"]').nth(1).fill('Email'); // User Property Path
    console.log(`✅ Filled claim form with name: ${claimName}`);
    
    // Save the claim
    const saveButton = page.getByRole('button', { name: 'Save' });
    await saveButton.click({ force: true });
    console.log('✅ Submitted claim creation');
    
    // Wait for modal to close (claim created successfully)
    await page.waitForTimeout(2000);
    
    // Verify claim appears in the table
    const claimRow = page.locator(`text=${claimName}`).first();
    await expect(claimRow).toBeVisible({ timeout: 5000 });
    console.log(`✅ Claim "${claimName}" created and visible in table`);
    
    // === PART 3: Map the claim to 'profile' scope ===
    await page.goto(`${IDP_BASE}/Admin/Scopes`);
    await page.waitForLoadState('networkidle');
    console.log('✅ Navigated to Scopes page');
    
    // Find and click Edit on the 'profile' scope row
    const profileRow = page.locator('tr', { has: page.locator('text=profile') });
    const editButton = profileRow.getByRole('button', { name: /edit/i });
    await editButton.click();
    console.log('✅ Clicked Edit on profile scope');
    
    // Wait for the form to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000); // Give Vue time to load claims
    
    // Find and check the checkbox for our new claim
    const claimCheckbox = page.locator(`input[type="checkbox"][value="${claimName}"]`);
    await expect(claimCheckbox).toBeVisible({ timeout: 5000 });
    await claimCheckbox.check();
    console.log(`✅ Checked checkbox for claim "${claimName}"`);
    
    // Save the scope mapping
    const saveScopeButton = page.getByRole('button', { name: 'Save Scope' });
    await saveScopeButton.click();
    console.log('✅ Saved scope mapping');
    
    // Wait for navigation back to scopes list
    await page.waitForURL(new RegExp(`${IDP_BASE}/Admin/Scopes`), { timeout: 5000 });
    console.log('✅ Returned to Scopes list - mapping saved');
    
    // === PART 4: Logout from IdP to clear session ===
    await page.goto(`${IDP_BASE}/Account/Logout`);
    await page.waitForTimeout(1000);
    console.log('✅ Logged out from IdP');
    
    // === PART 5: Login to TestClient and verify claim in token ===
    await page.goto(TESTCLIENT_BASE);
    await page.getByRole('link', { name: 'Profile' }).click();
    
    // Login flow
    await page.waitForURL(/localhost:7035.*Login/);
    await page.getByLabel('Email').fill('admin@hybridauth.local');
    await page.getByLabel('Password').fill('Admin@123');
    await page.getByRole('button', { name: 'Login' }).click();
    
    // Handle consent
    const consentButton = page.getByRole('button', { name: 'Allow Access' });
    if (await consentButton.isVisible({ timeout: 3000 }).catch(() => false)) {
      await consentButton.click();
      console.log('✅ Consented to access');
    }
    
    // Wait for TestClient Profile
    await page.waitForURL(new RegExp(`${TESTCLIENT_BASE}/Account/Profile`), { timeout: 10000 });
    await page.waitForLoadState('networkidle');
    console.log('✅ Reached TestClient Profile page');
    
    // Verify the new claim appears
    const newClaimRow = page.locator('tr', { 
      has: page.locator(`text=${claimType}`) 
    });
    
    await expect(newClaimRow).toBeVisible({ timeout: 5000 });
    console.log(`✅ New claim "${claimType}" found in TestClient claims table`);
    
    // Verify the claim has the expected value (Email property)
    const claimValue = newClaimRow.locator('td').nth(1);
    const valueText = await claimValue.textContent();
    console.log(`✅ New claim value: "${valueText}"`);
    
    expect(valueText).toContain('admin@hybridauth.local');
    console.log('✅ New claim value is correct');
    
    // Take screenshot
    await page.screenshot({ path: `test-results/e2e-claim-${claimName}.png`, fullPage: true });
    
    // === PART 6: Cleanup - Delete the test claim ===
    console.log('🧹 Cleaning up test claim...');
    
    // Go back to IdP Admin Claims
    await page.goto(`${IDP_BASE}/Admin/Claims`);
    await page.waitForLoadState('networkidle');
    
    // Find and delete the test claim
    const testClaimRow = page.locator('tr', { has: page.locator(`text=${claimName}`) });
    const deleteButton = testClaimRow.getByRole('button', { name: /delete/i });
    
    if (await deleteButton.isVisible({ timeout: 5000 }).catch(() => false)) {
      await deleteButton.click();
      console.log('✅ Clicked delete button');
      
      // Confirm deletion if there's a modal
      const confirmButton = page.getByRole('button', { name: /confirm|yes|delete/i }).first();
      if (await confirmButton.isVisible({ timeout: 2000 }).catch(() => false)) {
        await confirmButton.click();
      }
      
      // Verify claim is removed
      await expect(testClaimRow).not.toBeVisible({ timeout: 5000 });
      console.log(`✅ Test claim "${claimName}" deleted successfully`);
    }
    
    console.log('✅ E2E test completed successfully');
  });
});
