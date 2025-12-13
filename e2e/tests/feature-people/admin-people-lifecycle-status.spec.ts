import { test, expect } from '../fixtures';

// Lifecycle status tests using hybrid pattern.
// Pure API tests for person lifecycle status.

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

  test('Multiple persons lifecycle management', async ({ api }) => {
    const timestamp = Date.now();

    // Create multiple persons
    const persons = await Promise.all([
      api.people.create({ firstName: 'Life1', lastName: 'Test', employeeId: `EMP${timestamp}-1` }),
      api.people.create({ firstName: 'Life2', lastName: 'Test', employeeId: `EMP${timestamp}-2` }),
      api.people.create({ firstName: 'Life3', lastName: 'Test', employeeId: `EMP${timestamp}-3` })
    ]);

    expect(persons).toHaveLength(3);

    // Cleanup
    await Promise.all(persons.map(p => api.people.deletePerson(p.id)));
  });
});
