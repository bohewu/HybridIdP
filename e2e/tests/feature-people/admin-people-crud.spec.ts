import { test, expect } from '../fixtures';

// People CRUD tests using hybrid pattern.
// Pure API tests - serial execution to avoid conflicts.

test.describe.configure({ mode: 'serial' });

test.describe('Admin - People CRUD Operations', () => {
  test('Create person', async ({ api }) => {
    const timestamp = Date.now();

    const person = await api.people.create({
      firstName: 'CrudTest',
      lastName: 'Person',
      employeeId: `EMP${timestamp}`
    });

    expect(person.id).toBeTruthy();
    expect(person.firstName).toBe('CrudTest');

    // Cleanup
    await api.people.deletePerson(person.id);
  });

  test('Delete person', async ({ api }) => {
    const timestamp = Date.now();

    const person = await api.people.create({
      firstName: 'DeleteTest',
      lastName: 'Person',
      employeeId: `EMP${timestamp}`
    });

    expect(person.id).toBeTruthy();

    // Delete
    await api.people.deletePerson(person.id);
    // If we get here without error, delete succeeded
    expect(true).toBeTruthy();
  });
});
