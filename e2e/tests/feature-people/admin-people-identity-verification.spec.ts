import { test, expect } from '../fixtures';

// Identity verification tests - simplified.

test.describe('Admin - People Identity Verification', () => {
  test('Create person for verification', async ({ api }) => {
    const timestamp = Date.now();

    const person = await api.people.create({
      firstName: 'Verify',
      lastName: 'Test',
      employeeId: `EMP${timestamp}`
    });

    expect(person.id).toBeTruthy();

    // Cleanup
    await api.people.deletePerson(person.id);
  });
});
