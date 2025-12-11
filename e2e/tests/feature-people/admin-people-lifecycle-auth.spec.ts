import { test, expect } from '@playwright/test';
import admin from '../helpers/admin';

test.describe('Admin - Person Lifecycle Auth Logic', () => {
  test.beforeEach(async ({ page }) => {
    await admin.loginAsAdminViaIdP(page);
  });

  test('Persons with different statuses show correct canAuthenticate values', async ({ page }) => {
    // 1. Active -> True
    const activeName = `Auth_Active_${Date.now()}`;
    const activePerson = await admin.createPersonWithLifecycle(page, {
      firstName: activeName,
      lastName: 'User',
      status: 'Active'
    });
    // Check API response immediately or fetch details
    const activeDetails = await admin.getPersonDetails(page, activePerson.id);
    expect(activeDetails.canAuthenticate).toBe(true);
    expect(activeDetails.status).toBe('Active');

    // 2. Pending -> False
    const pendingName = `Auth_Pending_${Date.now()}`;
    const pendingPerson = await admin.createPersonWithLifecycle(page, {
      firstName: pendingName,
      lastName: 'User',
      status: 'Pending'
    });
    const pendingDetails = await admin.getPersonDetails(page, pendingPerson.id);
    expect(pendingDetails.canAuthenticate).toBe(false);
    expect(pendingDetails.status).toBe('Pending');

    // 3. Suspended -> False
    const suspendedName = `Auth_Suspended_${Date.now()}`;
    const suspendedPerson = await admin.createPersonWithLifecycle(page, {
      firstName: suspendedName,
      lastName: 'User',
      status: 'Suspended'
    });
    const suspendedDetails = await admin.getPersonDetails(page, suspendedPerson.id);
    expect(suspendedDetails.canAuthenticate).toBe(false);
    expect(suspendedDetails.status).toBe('Suspended');

    // 4. Terminated -> False
    const terminatedName = `Auth_Terminated_${Date.now()}`;
    const terminatedPerson = await admin.createPersonWithLifecycle(page, {
      firstName: terminatedName,
      lastName: 'User',
      status: 'Terminated'
    });
    const terminatedDetails = await admin.getPersonDetails(page, terminatedPerson.id);
    expect(terminatedDetails.canAuthenticate).toBe(false);
    expect(terminatedDetails.status).toBe('Terminated');
  });

  test('Active person with future StartDate cannot authenticate', async ({ page }) => {
    const firstName = `FutureStart_${Date.now()}`;
    // StartDate tomorrow
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const startDate = tomorrow.toISOString().split('T')[0];

    const person = await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active',
      startDate
    });

    const details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Active');
    expect(details.canAuthenticate).toBe(false);
  });

  test('Active person with past EndDate cannot authenticate', async ({ page }) => {
    const firstName = `PastEnd_${Date.now()}`;
    // EndDate yesterday
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const endDate = yesterday.toISOString().split('T')[0];

    const person = await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active',
      endDate
    });

    const details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Active');
    expect(details.canAuthenticate).toBe(false);
  });

  test('Active person within valid date range can authenticate', async ({ page }) => {
    const firstName = `ValidDates_${Date.now()}`;
    // Start yesterday, End tomorrow
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);

    const person = await admin.createPersonWithLifecycle(page, {
      firstName,
      lastName: 'User',
      status: 'Active',
      startDate: yesterday.toISOString().split('T')[0],
      endDate: tomorrow.toISOString().split('T')[0]
    });

    const details = await admin.getPersonDetails(page, person.id);
    expect(details.status).toBe('Active');
    expect(details.canAuthenticate).toBe(true);
  });
});
