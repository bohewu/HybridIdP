import { test, expect } from '@playwright/test'
import {
  loginAsAdminViaIdP,
  login,
  createPerson,
  createPersonWithIdentity,
  deletePerson,
  verifyPersonIdentity,
  linkAccountToPerson,
  unlinkAccountFromPerson,
  getAvailableUsers,
  createUserWithRole,
  deleteUser
} from '../helpers/admin'

test.describe('Admin - People Account Linking', () => {
  let createdPersonIds: string[] = []
  let createdUserIds: string[] = []

  test.beforeEach(async ({ page }) => {
    await loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/People')
    await page.waitForURL(/\/Admin\/People/)
  })

  test.afterEach(async ({ page }) => {
    // Clean up users first (to unlink from persons)
    for (const userId of createdUserIds) {
      await deleteUser(page, userId)
    }
    createdUserIds = []

    // Then clean up persons
    for (const personId of createdPersonIds) {
      await deletePerson(page, personId)
    }
    createdPersonIds = []
  })

  test('Link account to person with verified identity', async ({ page }) => {
    // Create person with identity document
    const personData = {
      firstName: 'LinkTest',
      lastName: 'VerifiedPerson',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const person = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(person.id)

    // Verify identity
    await verifyPersonIdentity(page, person.id)

    // Create user account
    const userEmail = `linktest-${Date.now()}@example.com`
    const user = await createUserWithRole(page, userEmail, 'TestPassword123!', [])
    createdUserIds.push(user.id)

    // Link account to person
    await linkAccountToPerson(page, person.id, user.id)

    // Verify link via API
    const linkedAccounts = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}/accounts`)
      if (!response.ok) throw new Error('Failed to fetch accounts')
      return response.json()
    }, person.id)

    expect(linkedAccounts).toHaveLength(1)
    expect(linkedAccounts[0].id).toBe(user.id)
    expect(linkedAccounts[0].email).toBe(userEmail)
  })

  test('Unlink account from person maintains identity verification', async ({ page }) => {
    // Create verified person
    const personData = {
      firstName: 'UnlinkTest',
      lastName: 'MaintainVerify',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const person = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(person.id)

    await verifyPersonIdentity(page, person.id)

    // Create and link user
    const userEmail = `unlinktest-${Date.now()}@example.com`
    const user = await createUserWithRole(page, userEmail, 'TestPassword123!', [])
    createdUserIds.push(user.id)

    await linkAccountToPerson(page, person.id, user.id)

    // Unlink account
    await unlinkAccountFromPerson(page, user.id)

    // Verify person still has verified identity
    const personDetails = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}`)
      if (!response.ok) throw new Error('Failed to fetch person')
      return response.json()
    }, person.id)

    expect(personDetails.identityVerifiedAt).not.toBeNull()
    expect(personDetails.nationalId).toBe('A123456789')

    // Verify no linked accounts
    const linkedAccounts = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}/accounts`)
      if (!response.ok) throw new Error('Failed to fetch accounts')
      return response.json()
    }, person.id)

    expect(linkedAccounts).toHaveLength(0)
  })

  test('Multi-account login - two accounts linked to same verified person', async ({ page, context }) => {
    // Create person with verified identity
    const personData = {
      firstName: 'MultiAccount',
      lastName: 'OnePerson',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'Passport' as const,
      passportNumber: '300123456'
    }

    const person = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(person.id)

    await verifyPersonIdentity(page, person.id)

    // Create two user accounts
    const timestamp = Date.now()
    const user1Email = `multi1-${timestamp}@example.com`
    const user2Email = `multi2-${timestamp}@example.com`
    
    const user1 = await createUserWithRole(page, user1Email, 'TestPassword123!', [])
    const user2 = await createUserWithRole(page, user2Email, 'TestPassword123!', [])
    
    createdUserIds.push(user1.id, user2.id)

    // Link both accounts to the same person
    await linkAccountToPerson(page, person.id, user1.id)
    await linkAccountToPerson(page, person.id, user2.id)

    // Verify both accounts are linked
    const linkedAccounts = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}/accounts`)
      if (!response.ok) throw new Error('Failed to fetch accounts')
      return response.json()
    }, person.id)

    expect(linkedAccounts).toHaveLength(2)
    expect(linkedAccounts.map(a => a.email)).toContain(user1Email)
    expect(linkedAccounts.map(a => a.email)).toContain(user2Email)

    // Test login with first account
    const page1 = await context.newPage()
    await login(page1, user1Email, 'TestPassword123!')
    
    // Check user profile shows person name
    await page1.goto('https://localhost:7035/Account/Profile')
    await expect(page1.locator('text=/MultiAccount OnePerson/i')).toBeVisible()
    await page1.close()

    // Test login with second account
    const page2 = await context.newPage()
    await login(page2, user2Email, 'TestPassword123!')
    
    // Check user profile shows same person name
    await page2.goto('https://localhost:7035/Account/Profile')
    await expect(page2.locator('text=/MultiAccount OnePerson/i')).toBeVisible()
    await page2.close()
  })

  test('Link account shows person identity in account profile', async ({ page, context }) => {
    // Create person with verified Resident Certificate
    const personData = {
      firstName: 'ProfileTest',
      lastName: 'ShowIdentity',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'ResidentCertificate' as const,
      residentCertificateNumber: 'AA12345678'
    }

    const person = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(person.id)

    await verifyPersonIdentity(page, person.id)

    // Create user
    const userEmail = `profile-${Date.now()}@example.com`
    const user = await createUserWithRole(page, userEmail, 'TestPassword123!', [])
    createdUserIds.push(user.id)

    // Link account
    await linkAccountToPerson(page, person.id, user.id)

    // Login as that user
    const userPage = await context.newPage()
    await login(userPage, userEmail, 'TestPassword123!')

    // Go to profile
    await userPage.goto('https://localhost:7035/Account/Profile')

    // Verify person name is visible
    await expect(userPage.locator('text=/ProfileTest ShowIdentity/i')).toBeVisible()

    // Verify identity document type is shown (if UI displays it)
    // This depends on UI implementation
    
    await userPage.close()
  })

  test('Get available users excludes already-linked users', async ({ page }) => {
    // Create person
    const person = await createPerson(page, 'Available', 'UsersTest', `EMP${Date.now()}`)
    createdPersonIds.push(person.id)

    // Create two users
    const timestamp = Date.now()
    const user1Email = `available1-${timestamp}@example.com`
    const user2Email = `available2-${timestamp}@example.com`
    
    const user1 = await createUserWithRole(page, user1Email, 'TestPassword123!', [])
    const user2 = await createUserWithRole(page, user2Email, 'TestPassword123!', [])
    
    createdUserIds.push(user1.id, user2.id)

    // Link first user
    await linkAccountToPerson(page, person.id, user1.id)

    // Get available users
    const availableUsers = await getAvailableUsers(page)

    // User1 should NOT be in available list (already linked)
    const user1InList = availableUsers.some(u => u.id === user1.id)
    expect(user1InList).toBe(false)

    // User2 should be in available list (not linked)
    const user2InList = availableUsers.some(u => u.id === user2.id)
    expect(user2InList).toBe(true)
  })

  test('Link multiple accounts and unlink one preserves others', async ({ page }) => {
    // Create person
    const personData = {
      firstName: 'MultiLink',
      lastName: 'UnlinkOne',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const person = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(person.id)

    // Create three users
    const timestamp = Date.now()
    const users = []
    for (let i = 1; i <= 3; i++) {
      const email = `multilink${i}-${timestamp}@example.com`
      const user = await createUserWithRole(page, email, 'TestPassword123!', [])
      users.push(user)
      createdUserIds.push(user.id)
    }

    // Link all three users
    for (const user of users) {
      await linkAccountToPerson(page, person.id, user.id)
    }

    // Verify all linked
    let linkedAccounts = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}/accounts`)
      return response.json()
    }, person.id)
    expect(linkedAccounts).toHaveLength(3)

    // Unlink second user
    await unlinkAccountFromPerson(page, users[1].id)

    // Verify only 2 remain
    linkedAccounts = await page.evaluate(async (personId) => {
      const response = await fetch(`/api/admin/people/${personId}/accounts`)
      return response.json()
    }, person.id)
    expect(linkedAccounts).toHaveLength(2)
    expect(linkedAccounts.map(a => a.id)).toContain(users[0].id)
    expect(linkedAccounts.map(a => a.id)).toContain(users[2].id)
    expect(linkedAccounts.map(a => a.id)).not.toContain(users[1].id)
  })

  test('Cannot link same user to multiple persons', async ({ page }) => {
    // Create two persons
    const person1 = await createPerson(page, 'Person', 'One', `EMP${Date.now()}-1`)
    const person2 = await createPerson(page, 'Person', 'Two', `EMP${Date.now()}-2`)
    createdPersonIds.push(person1.id, person2.id)

    // Create one user
    const userEmail = `singleuser-${Date.now()}@example.com`
    const user = await createUserWithRole(page, userEmail, 'TestPassword123!', [])
    createdUserIds.push(user.id)

    // Link user to first person
    await linkAccountToPerson(page, person1.id, user.id)

    // Attempt to link same user to second person should fail
    await expect(async () => {
      await linkAccountToPerson(page, person2.id, user.id)
    }).rejects.toThrow(/already linked|duplicate|conflict/i)
  })
})
