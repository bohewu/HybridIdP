import { test, expect } from '../fixtures';

// Account linking tests using hybrid pattern.
// Simple API tests.

test.describe('Admin - People Account Linking', () => {
  test('Create person and user for linking', async ({ api }) => {
    const timestamp = Date.now();

    // Create person 
    const person = await api.people.create({
      firstName: 'LinkTest',
      lastName: 'Person',
      employeeId: `EMP${timestamp}`
    });

    // Create user
    const user = await api.users.create({
      email: `link-${timestamp}@hybridauth.local`,
      userName: `link-${timestamp}@hybridauth.local`,
      firstName: 'Link',
      lastName: 'User',
      password: `Link!${timestamp}a`
    });

    expect(person.id).toBeTruthy();
    expect(user.id).toBeTruthy();

    // Cleanup
    await api.users.deleteUser(user.id);
    await api.people.deletePerson(person.id);
  });
});
