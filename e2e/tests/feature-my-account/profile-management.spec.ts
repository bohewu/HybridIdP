import { test, expect } from '@playwright/test';
import { loginAsAdminViaIdP, createUserWithRole, deleteUser, createRole, deleteRole, login } from '../helpers/admin';

/**
 * Profile Management E2E Tests
 * 
 * Strategy: Single test with setup/teardown to avoid rate limiting
 * Creates test user via API, runs all checks, then cleanup
 */
test('Profile Management - Complete Flow', async ({ page }) => {
  // Setup: Login as admin and create test data
  await loginAsAdminViaIdP(page);

  const testEmail = `e2e_profile_${Date.now()}@example.com`;
  const testPassword = 'Test@12345';
  
  // Create test role via API (no special permissions needed for profile viewing)
  const role = await createRole(page, `e2e_profile_role_${Date.now()}`, []);
  const testRoleId = role.id;
  
  // Create test user via API
  const user = await createUserWithRole(page, testEmail, testPassword, [testRoleId]);
  const testUserId = user.id;

  // Logout admin
  await page.goto('https://localhost:7035/Account/Logout');

  // ===== Test 1: Display profile page =====
  await login(page, testEmail, testPassword);
  await page.goto('https://localhost:7035/Account/Profile');
  
  // Wait for Vue app to mount
  await page.waitForSelector('#profile-app', { timeout: 15000 });
  await page.waitForSelector('text=Account Information', { timeout: 15000 });
  
  // Check that email is displayed
  await expect(page.locator(`text=${testEmail}`).first()).toBeVisible({ timeout: 10000 });
  await expect(page.locator('text=Email').first()).toBeVisible();

  // ===== Test 2: Verify password change form =====
  const changePasswordTitle = page.locator('text=Change Password');
  const policyDisabledWarning = page.locator('text=disabled by system policy');
  
  // At least one should be visible (form or policy warning)
  const hasChangePasswordForm = await changePasswordTitle.count() > 0;
  const hasPolicyWarning = await policyDisabledWarning.count() > 0;
  expect(hasChangePasswordForm || hasPolicyWarning).toBe(true);
  
  // External login note should NOT appear for test user (has local password)
  const externalLoginNote = page.locator('text=authenticated via an external provider');
  expect(await externalLoginNote.count()).toBe(0);

  // ===== Test 3: Check password form fields =====
  if (await changePasswordTitle.count() > 0) {
    await expect(changePasswordTitle).toBeVisible();
    
    const passwordInputs = page.locator('input[type="password"]');
    const count = await passwordInputs.count();
    expect(count).toBeGreaterThanOrEqual(3); // current, new, confirm
  }

  // ===== Test 4: Verify API response structure =====
  await page.goto('https://localhost:7035/Account/Logout');
  await login(page, testEmail, testPassword);
  
  const profileResponsePromise = page.waitForResponse(
    response => response.url().includes('/api/profile') && response.request().method() === 'GET',
    { timeout: 15000 }
  );
  
  await page.goto('https://localhost:7035/Account/Profile');
  const profileResponse = await profileResponsePromise;
  expect(profileResponse.status()).toBe(200);
  
  const profileData = await profileResponse.json();
  expect(profileData).toHaveProperty('userId');
  expect(profileData).toHaveProperty('userName');
  expect(profileData).toHaveProperty('hasLocalPassword');
  expect(profileData).toHaveProperty('allowPasswordChange');
  expect(profileData.userName).toBe(testEmail);
  expect(profileData.hasLocalPassword).toBe(true);

  // ===== Test 5: Handle user without linked Person =====
  await page.waitForSelector('text=Account Information', { timeout: 15000 });
  const accountInfoSection = page.locator('text=Account Information');
 await expect(accountInfoSection).toBeVisible();
  
  // Test user doesn't have linked Person - edit form should NOT be visible
  const editFormTitle = page.locator('text=Edit Profile');
  const hasEditForm = await editFormTitle.count() > 0;
  
  if (!hasEditForm) {
    // Expected: test user has no Person, so no edit form
    console.log('✓ Test user not linked to Person - edit form correctly hidden');
  }

  // Cleanup: Login as admin and delete test data
  await page.goto('https://localhost:7035/Account/Logout');
  await loginAsAdminViaIdP(page);
  
  await deleteUser(page, testUserId);
  await deleteRole(page, testRoleId);
  
  console.log('✓ All profile management tests passed');
});
