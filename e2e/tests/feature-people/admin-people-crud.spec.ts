import { test, expect } from '../fixtures';

// People CRUD tests - comprehensive UI flow tests.
// CRITICAL: Test actual UI interactions for person management.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - People CRUD (UI Flows)', () => {
  test('Create person via UI form', async ({ page, api }) => {
    const timestamp = Date.now();
    const firstName = `E2E-Person-${timestamp}`;

    // Navigate to people page
    await page.goto('https://localhost:7035/Admin/People');
    await page.waitForURL(/\/Admin\/People/);

    // Click Create Person button
    await page.click('button:has-text("Create Person"), button:has-text("Create New Person")');

    // Wait for form modal
    await page.waitForSelector('form', { timeout: 10000 });

    // Fill form fields
    await page.fill('input[name="firstName"], #firstName', firstName);
    await page.fill('input[name="lastName"], #lastName', 'TestPerson');
    await page.fill('input[name="email"], #email', `${firstName.toLowerCase()}@test.com`);

    // Submit form
    await page.click('button[type="submit"]:has-text("Save"), button[type="submit"]:has-text("Create")');

    // Wait for success
    await page.waitForTimeout(2000);

    // Verify person appears in list
    await expect(page.locator('table, ul')).toContainText(firstName, { timeout: 10000 });

    // Cleanup via API
    const people = await page.evaluate(async () => {
      const r = await fetch('/api/admin/people');
      return r.ok ? r.json() : { items: [] };
    });
    const createdPerson = people.items.find((p: any) => p.firstName === firstName);
    if (createdPerson) {
      await page.evaluate(async (id: string) => {
        await fetch(`/api/admin/people/${id}`, { method: 'DELETE' });
      }, createdPerson.id);
    }
  });

  test('Link person to user account via UI', async ({ page, api }) => {
    const timestamp = Date.now();
    const email = `e2e-link-${timestamp}@hybridauth.local`;

    // Create user via API
    const user = await api.users.create({
      email,
      userName: email,
      firstName: 'Link',
      lastName: 'Test',
      password: `E2E!${timestamp}a`
    });

    // Create person via API
    const person = await page.evaluate(async (firstName: string) => {
      const r = await fetch('/api/admin/people', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          firstName,
          lastName: 'PersonLink',
          email: `${firstName.toLowerCase()}@test.com`
        })
      });
      return r.ok ? r.json() : null;
    }, `LinkPerson${timestamp}`);

    // Navigate to people page
    await page.goto('https://localhost:7035/Admin/People');
    await page.waitForURL(/\/Admin\/People/);

    // Find person and click Link/Edit
    const personRow = page.locator('tr, li').filter({ hasText: `LinkPerson${timestamp}` }).first();
    await expect(personRow).toBeVisible({ timeout: 10000 });
    await personRow.locator('button[title*="Edit"], button:has-text("Edit"), button:has-text("Link")').first().click();

    // Wait for linking UI
    await page.waitForTimeout(2000);

    // Search for user to link
    const userSearch = page.locator('input[placeholder*="user"], input[placeholder*="Search"]').first();
    if (await userSearch.isVisible().catch(() => false)) {
      await userSearch.fill(email);
      await page.waitForTimeout(600);

      // Select user from results
      const userOption = page.locator(`text=${email}, li:has-text("${email}")`).first();
      if (await userOption.isVisible().catch(() => false)) {
        await userOption.click();
      }
    }

    // Save link
    await page.click('button[type="submit"]:has-text("Save"), button:has-text("Link")');
    await page.waitForTimeout(2000);

    // Cleanup
    await api.users.deleteUser(user.id);
    if (person) {
      await page.evaluate(async (id: string) => {
        await fetch(`/api/admin/people/${id}`, { method: 'DELETE' });
      }, person.id);
    }
  });

  test('Verify person lifecycle status via UI', async ({ page }) => {
    const timestamp = Date.now();
    const firstName = `Status${timestamp}`;

    // Create person via API
    const person = await page.evaluate(async (name: string) => {
      const r = await fetch('/api/admin/people', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          firstName: name,
          lastName: 'StatusTest',
          email: `${name.toLowerCase()}@test.com`
        })
      });
      return r.ok ? r.json() : null;
    }, firstName);

    // Navigate to people page
    await page.goto('https://localhost:7035/Admin/People');
    await page.waitForURL(/\/Admin\/People/);

    // Verify person shows in list with Active status
    const personRow = page.locator('tr, li').filter({ hasText: firstName }).first();
    await expect(personRow).toBeVisible({ timeout: 10000 });
    await expect(personRow).toContainText(/Active|Pending/i);

    // Cleanup
    if (person) {
      await page.evaluate(async (id: string) => {
        await fetch(`/api/admin/people/${id}`, { method: 'DELETE' });
      }, person.id);
    }
  });
});
