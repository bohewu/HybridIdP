import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Persons Account Linking E2E tests

test('Admin - Persons Account Linking (link and unlink)', async ({ page }) => {
  page.on('dialog', async d => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);

  const timestamp = Date.now();

  // Create a test user (account) without a person
  const userEmail = `e2e-account-link-${timestamp}@hybridauth.local`;
  const userPassword = `E2E!${timestamp}a`;

  const createdUser = await adminHelpers.createUserWithRole(page, userEmail, userPassword, []);

  // Create a test person
  const personData = {
    firstName: `LinkTest${timestamp}`,
    lastName: `Person${timestamp}`,
    employeeId: `LINK-${timestamp}`,
    department: 'Link Test Dept'
  };

  const createdPerson = await page.evaluate(async (data) => {
    const r = await fetch('/api/admin/persons', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!r.ok) throw new Error(`Failed to create person: ${r.status}`);
    return r.json();
  }, personData);

  // Navigate to Persons page
  await page.goto('https://localhost:7035/Admin/Persons');
  await page.waitForURL(/\/Admin\/Persons/);

  // Search for the person
  const searchInput = page.locator('input[type="text"]').first();
  await searchInput.fill(personData.firstName);
  await page.waitForTimeout(500);

  // Click "Accounts" button
  const personRow = page.locator(`li:has-text("${personData.firstName} ${personData.lastName}")`);
  const accountsBtn = personRow.locator('button:has-text("Accounts")');
  await accountsBtn.click();

  // Wait for linked accounts dialog
  await page.waitForSelector('text=Linked Accounts', { timeout: 10000 });

  // Verify no accounts initially
  await expect(page.locator('text=No accounts linked')).toBeVisible({ timeout: 5000 });

  // Click "Link Account" button
  await page.click('button:has-text("Link Account")');

  // Wait for user selection dialog
  await page.waitForSelector('text=Select User to Link', { timeout: 10000 });

  // Search for the user
  const searchUserInput = page.locator('input[placeholder*="Search users"]');
  await searchUserInput.fill(userEmail);
  await page.click('button:has-text("Search")');

  await page.waitForTimeout(1000);

  // Select the user
  const userOption = page.locator(`text=${userEmail}`).first();
  await userOption.click();

  // Click Link button
  const linkBtn = page.locator('button:has-text("Link")').first();
  await linkBtn.click();

  // Wait for success alert (dialog auto-accepted)
  await page.waitForTimeout(1000);

  // Verify account is now linked
  await expect(page.locator(`text=${userEmail}`)).toBeVisible({ timeout: 10000 });

  // Verify user is excluded from available users
  await page.click('button:has-text("Link Account")');
  await page.waitForSelector('text=Select User to Link', { timeout: 10000 });
  
  await searchUserInput.fill(userEmail);
  const searchBtn = page.locator('button:has-text("Search")').last();
  await searchBtn.click();
  await page.waitForTimeout(1000);

  // User should not appear in available users list
  const availableUsersList = page.locator('.max-h-64').last();
  await expect(availableUsersList.locator(`text=${userEmail}`)).not.toBeVisible({ timeout: 5000 });

  // Close the select user dialog
  const cancelBtn = page.locator('button:has-text("Cancel")').last();
  await cancelBtn.click();

  // Unlink the account
  await page.waitForTimeout(500);
  const unlinkBtn = page.locator('button:has-text("Unlink")');
  await unlinkBtn.click();

  // Confirm unlink (dialog already handled)
  await page.waitForTimeout(1000);

  // Verify no accounts linked again
  await expect(page.locator('text=No accounts linked')).toBeVisible({ timeout: 10000 });

  // Cleanup
  const closeBtn = page.locator('button:has-text("Close")').first();
  await closeBtn.click();
  await page.waitForTimeout(500);
  
  await adminHelpers.deletePerson(page, createdPerson.id);
  await adminHelpers.deleteUser(page, createdUser.id);

  console.log('✓ Persons account linking test passed');
});

test('Admin - Persons Account Linking via API', async ({ page }) => {
  page.on('dialog', async d => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);

  const timestamp = Date.now();

  // Create user and person via API
  const userEmail = `e2e-api-link-${timestamp}@hybridauth.local`;
  const userPassword = `E2E!${timestamp}a`;

  const createdUser = await adminHelpers.createUserWithRole(page, userEmail, userPassword, []);

  const personData = {
    firstName: `ApiLink${timestamp}`,
    lastName: `Test${timestamp}`,
    employeeId: `API-${timestamp}`
  };

  const createdPerson = await page.evaluate(async (data) => {
    const r = await fetch('/api/admin/persons', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!r.ok) throw new Error(`Failed to create person: ${r.status}`);
    return r.json();
  }, personData);

  // Link account via helper
  await adminHelpers.linkAccountToPerson(page, createdPerson.id, createdUser.id);

  // Verify link via API
  const linkedAccounts = await page.evaluate(async (personId) => {
    const r = await fetch(`/api/admin/persons/${personId}/accounts`);
    if (!r.ok) throw new Error(`Failed to fetch accounts: ${r.status}`);
    return r.json();
  }, createdPerson.id);

  expect(linkedAccounts.length).toBe(1);
  expect(linkedAccounts[0].email).toBe(userEmail);

  // Verify user is not in available users
  const availableUsers = await adminHelpers.getAvailableUsers(page);
  const userInAvailable = availableUsers.find((u: any) => u.id === createdUser.id);
  expect(userInAvailable).toBeUndefined();

  // Unlink account via helper
  await adminHelpers.unlinkAccountFromPerson(page, createdUser.id);

  // Verify unlink
  const linkedAccountsAfter = await page.evaluate(async (personId) => {
    const r = await fetch(`/api/admin/persons/${personId}/accounts`);
    if (!r.ok) throw new Error(`Failed to fetch accounts: ${r.status}`);
    return r.json();
  }, createdPerson.id);

  expect(linkedAccountsAfter.length).toBe(0);

  // Cleanup
  await adminHelpers.deletePerson(page, createdPerson.id);
  await adminHelpers.deleteUser(page, createdUser.id);

  console.log('✓ Persons API account linking test passed');
});
