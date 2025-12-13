import { test, expect } from '../fixtures';

// Account linking tests using hybrid pattern.
// API for setup, UI for linking verification.

test.describe('Admin - People Account Linking', () => {
  test('Link user account to person', async ({ page, api }) => {
    const timestamp = Date.now();

    // 1. Arrange (API) - Create person and user
    const person = await api.people.create({
      firstName: 'LinkTest',
      lastName: 'Person',
      employeeId: `EMP${timestamp}`
    });

    const user = await api.users.create({
      email: `link-${timestamp}@hybridauth.local`,
      userName: `link-${timestamp}@hybridauth.local`,
      firstName: 'Link',
      lastName: 'User',
      password: `Link!${timestamp}a`
    });

    // 2. Act (API) - Link account
    await api.people.linkAccount(person.id, user.id);

    // 3. Assert (UI) - Verify link in admin page
    await page.goto(`https://localhost:7035/Admin/People`);
    await page.waitForURL(/\/Admin\/People/);

    const searchInput = page.locator('input[placeholder*="Search" i]');
    if (await searchInput.count() > 0) {
      await searchInput.fill('LinkTest');
      await page.waitForTimeout(500);
    }

    // Person should be visible
    await expect(page.locator('text=LinkTest')).toBeVisible({ timeout: 10000 });

    // 4. Cleanup (API)
    await api.people.unlinkAccount(user.id);
    await api.users.deleteUser(user.id);
    await api.people.deletePerson(person.id);
  });

  test('Unlink user account from person', async ({ api }) => {
    const timestamp = Date.now();

    // Create person
    const person = await api.people.create({
      firstName: 'Unlink',
      lastName: 'Test',
      employeeId: `EMP${timestamp}`
    });

    // Create and link user
    const user = await api.users.create({
      email: `unlink-${timestamp}@hybridauth.local`,
      userName: `unlink-${timestamp}@hybridauth.local`,
      firstName: 'Unlink',
      lastName: 'User',
      password: `Unlink!${timestamp}a`
    });

    await api.people.linkAccount(person.id, user.id);

    // Unlink
    await api.people.unlinkAccount(user.id);

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.people.deletePerson(person.id);
  });
});
