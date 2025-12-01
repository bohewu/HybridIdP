import { test, expect } from '@playwright/test';
import adminHelpers from '../helpers/admin';

// Persons CRUD UI tests. Requires IdP dev server running.

test('Admin - Persons CRUD (create, update, delete)', async ({ page }) => {
  page.on('dialog', async d => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);

  // Navigate to Persons admin page
  await page.goto('https://localhost:7035/Admin/People');
  await page.waitForURL(/\/Admin\/People/);

  const timestamp = Date.now();
  const firstName = `E2EFirst${timestamp}`;
  const lastName = `E2ELast${timestamp}`;
  const employeeId = `EMP-${timestamp}`;

  // Create New Person
  const createBtn = page.locator('button:has-text("Create Person")');
  await createBtn.click();

  // Wait for form
  await page.waitForSelector('#firstName');
  await page.fill('#firstName', firstName);
  await page.fill('#lastName', lastName);
  await page.fill('#employeeId', employeeId);
  await page.fill('#department', 'E2E Department');
  await page.fill('#jobTitle', 'E2E Tester');

  // Submit form
  await page.click('button[type="submit"]');

  // Wait for form to close and list to refresh
  await page.waitForTimeout(1000);

  // Search for person in list
  const searchInput = page.locator('input[type="text"]').first();
  await searchInput.fill(firstName);
  await page.waitForTimeout(500);

  // Verify person appears in list
  const personItem = page.locator(`text=${firstName} ${lastName}`);
  await expect(personItem).toBeVisible({ timeout: 10000 });

  // Edit Person
  const personRow = page.locator(`tr:has-text("${firstName} ${lastName}")`);
  const editBtn = personRow.locator('button[title*="Edit"]');
  await editBtn.click();

  await page.waitForSelector('#firstName');
  await page.fill('#department', 'Updated Department');
  await page.click('button[type="submit"]');

  await page.waitForTimeout(1000);

  // Verify update
  await expect(page.locator('text=Updated Department')).toBeVisible({ timeout: 10000 });

  // Delete Person - need to get the row again after update
  const updatedPersonRow = page.locator(`tr:has-text("${firstName} ${lastName}")`);
  const deleteBtn = updatedPersonRow.locator('button[title*="Delete"]');
  await deleteBtn.click();

  // Confirm deletion (dialog already handled by listener)
  await page.waitForTimeout(1000);

  // Verify person is removed
  await expect(personItem).not.toBeVisible({ timeout: 10000 });

  console.log('✓ Persons CRUD test passed');
});

test('Admin - Persons Search', async ({ page }) => {
  page.on('dialog', async d => await d.accept());
  await adminHelpers.loginAsAdminViaIdP(page);

  await page.goto('https://localhost:7035/Admin/Persons');
  await page.waitForURL(/\/Admin\/Persons/);

  // Create test person via API for search
  const timestamp = Date.now();
  const personData = {
    firstName: `SearchTest${timestamp}`,
    lastName: `Person${timestamp}`,
    employeeId: `SEARCH-${timestamp}`,
    department: 'Search Test Dept'
  };

  const createdPerson = await page.evaluate(async (data) => {
    const r = await fetch('/api/admin/people', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!r.ok) throw new Error(`Failed to create person: ${r.status}`);
    return r.json();
  }, personData);

  // Search by first name
  const searchInput = page.locator('input[type="text"]').first();
  await searchInput.fill(personData.firstName);
  await page.waitForTimeout(500);

  await expect(page.locator(`text=${personData.firstName}`)).toBeVisible({ timeout: 10000 });

  // Search by employee ID
  await searchInput.fill(personData.employeeId);
  await page.waitForTimeout(500);

  await expect(page.locator(`text=${personData.employeeId}`)).toBeVisible({ timeout: 10000 });

  // Cleanup
  await adminHelpers.deletePerson(page, createdPerson.id);

  console.log('✓ Persons search test passed');
});
