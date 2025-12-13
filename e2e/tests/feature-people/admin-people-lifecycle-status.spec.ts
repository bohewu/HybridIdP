import { test, expect } from '../fixtures';

// Lifecycle status tests - simplified.

test.describe('Admin - People Lifecycle Status', () => {
  test('Create person with lifecycle fields', async ({ api }) => {
    const timestamp = Date.now();

    const person = await api.people.create({
      firstName: 'Lifecycle',
      lastName: 'Test',
      employeeId: `EMP${timestamp}`
    });

    expect(person.id).toBeTruthy();

    // Cleanup
    await api.people.deletePerson(person.id);
  });
});
