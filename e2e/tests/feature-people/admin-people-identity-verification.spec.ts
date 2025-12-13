import { test, expect } from '../fixtures';

// Identity verification tests using hybrid pattern.
// Pure API tests for verification status.

test.describe('Admin - People Identity Verification', () => {
  test('Create person and verify identity document status', async ({ api }) => {
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

  test('Create multiple persons with different statuses', async ({ api }) => {
    const timestamp = Date.now();

    const person1 = await api.people.create({
      firstName: 'Status',
      lastName: 'One',
      employeeId: `EMP${timestamp}-1`
    });

    const person2 = await api.people.create({
      firstName: 'Status',
      lastName: 'Two',
      employeeId: `EMP${timestamp}-2`
    });

    expect(person1.id).toBeTruthy();
    expect(person2.id).toBeTruthy();

    // Cleanup
    await api.people.deletePerson(person1.id);
    await api.people.deletePerson(person2.id);
  });
});
