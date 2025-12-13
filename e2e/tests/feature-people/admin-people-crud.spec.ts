import { test, expect } from '../fixtures';

// People CRUD tests using hybrid pattern.
// Pure API tests - no UI needed for CRUD operations.

test.describe('Admin - People CRUD Operations', () => {
  test('Create and delete person with identity document', async ({ api }) => {
    const timestamp = Date.now();

    const person = await api.people.create({
      firstName: 'CrudTest',
      lastName: 'Person',
      employeeId: `EMP${timestamp}`,
      department: 'E2E Testing',
      jobTitle: 'Test Engineer'
    });

    expect(person.id).toBeTruthy();
    expect(person.firstName).toBe('CrudTest');
    expect(person.lastName).toBe('Person');

    // Cleanup
    await api.people.deletePerson(person.id);
  });

  test('Create multiple persons and search', async ({ api }) => {
    const timestamp = Date.now();

    const person1 = await api.people.create({
      firstName: 'SearchTest',
      lastName: 'Alpha',
      employeeId: `EMP${timestamp}-1`
    });

    const person2 = await api.people.create({
      firstName: 'SearchTest',
      lastName: 'Beta',
      employeeId: `EMP${timestamp}-2`
    });

    // Search via API
    const results = await api.people.list('SearchTest');
    expect(results.items.length).toBeGreaterThanOrEqual(2);

    // Cleanup
    await api.people.deletePerson(person1.id);
    await api.people.deletePerson(person2.id);
  });

  test('Search person by employee ID', async ({ api }) => {
    const uniqueEmpId = `SEARCH${Date.now()}`;

    const person = await api.people.create({
      firstName: 'Employee',
      lastName: 'SearchByID',
      employeeId: uniqueEmpId
    });

    // Search by employee ID
    const results = await api.people.list(uniqueEmpId);
    expect(results.items.some(p => p.employeeId === uniqueEmpId)).toBeTruthy();

    // Cleanup
    await api.people.deletePerson(person.id);
  });
});
