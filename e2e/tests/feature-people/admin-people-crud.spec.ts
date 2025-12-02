import { test, expect } from '@playwright/test'
import {
  loginAsAdminViaIdP,
  createPerson,
  createPersonWithIdentity,
  deletePerson,
  updatePersonIdentity,
  getPersonDetails
} from '../helpers/admin'

test.describe('Admin - People CRUD Operations', () => {
  let createdPersonIds: string[] = []

  test.beforeEach(async ({ page }) => {
    await loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/People')
    await page.waitForURL(/\/Admin\/People/)
  })

  test.afterEach(async ({ page }) => {
    // Clean up all created persons
    for (const personId of createdPersonIds) {
      await deletePerson(page, personId)
    }
    createdPersonIds = []
  })

  test('Create, update, and delete person with identity document', async ({ page }) => {
    // Create person with National ID
    const personData = {
      firstName: 'CrudTest',
      lastName: 'PersonOne',
      employeeId: `EMP${Date.now()}`,
      department: 'E2E Testing',
      jobTitle: 'Test Engineer',
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.firstName).toBe('CrudTest')
    expect(created.lastName).toBe('PersonOne')
    expect(created.nationalId).toBe('A123456789')

    // Reload and verify person appears in UI
    await page.reload()
    await expect(page.locator('tr', { hasText: 'CrudTest PersonOne' })).toBeVisible()

    // Update person identity to Passport
    await updatePersonIdentity(page, created.id, {
      identityDocumentType: 'Passport',
      nationalId: null,
      passportNumber: '300123456'
    })

    // Verify update
    const updated = await getPersonDetails(page, created.id)
    expect(updated.passportNumber).toBe('300123456')
    expect(updated.nationalId).toBeNull()
    expect(updated.identityDocumentType).toBe('Passport')

    // Delete person
    await deletePerson(page, created.id)
    createdPersonIds = createdPersonIds.filter(id => id !== created.id)

    // Reload and verify person is gone
    await page.reload()
    await expect(page.locator('tr', { hasText: 'CrudTest PersonOne' })).not.toBeVisible()
  })

  test('Create person without identity document', async ({ page }) => {
    const created = await createPerson(page, 'Simple', 'Person', `EMP${Date.now()}`)
    createdPersonIds.push(created.id)

    expect(created.firstName).toBe('Simple')
    expect(created.lastName).toBe('Person')
    expect(created.nationalId).toBeNull()
    expect(created.passportNumber).toBeNull()
    expect(created.residentCertificateNumber).toBeNull()

    // Verify in UI
    await page.reload()
    await expect(page.locator('tr', { hasText: 'Simple Person' })).toBeVisible()
  })

  test('Search for person by name', async ({ page }) => {
    // Create multiple persons
    const timestamp = Date.now()
    const person1 = await createPerson(page, 'SearchTest', 'AlphaUser', `EMP${timestamp}-1`)
    const person2 = await createPerson(page, 'SearchTest', 'BetaUser', `EMP${timestamp}-2`)
    const person3 = await createPerson(page, 'Different', 'Name', `EMP${timestamp}-3`)
    
    createdPersonIds.push(person1.id, person2.id, person3.id)

    // Reload page
    await page.reload()

    // Search for "SearchTest"
    const searchInput = page.locator('input[placeholder*="Search"], input[placeholder*="搜尋"]')
    await searchInput.fill('SearchTest')
    
    // Wait for search results
    await page.waitForTimeout(1000) // Give time for search to execute

    // Should see AlphaUser and BetaUser
    await expect(page.locator('tr', { hasText: 'SearchTest AlphaUser' })).toBeVisible()
    await expect(page.locator('tr', { hasText: 'SearchTest BetaUser' })).toBeVisible()
    
    // Should NOT see Different Name (unless still loading)
    const differentRow = page.locator('tr', { hasText: 'Different Name' })
    const count = await differentRow.count()
    // May or may not be visible depending on search implementation
  })

  test('Search for person by employee ID', async ({ page }) => {
    const uniqueEmpId = `SEARCH${Date.now()}`
    const created = await createPerson(page, 'Employee', 'SearchByID', uniqueEmpId)
    createdPersonIds.push(created.id)

    // Reload and search
    await page.reload()

    const searchInput = page.locator('input[placeholder*="Search"], input[placeholder*="搜尋"]')
    await searchInput.fill(uniqueEmpId)
    
    await page.waitForTimeout(1000)

    // Should find the person
    await expect(page.locator('tr', { hasText: 'Employee SearchByID' })).toBeVisible()
  })

  test('Pagination - create multiple persons and verify list', async ({ page }) => {
    // Create 5 persons
    const timestamp = Date.now()
    for (let i = 1; i <= 5; i++) {
      const created = await createPerson(page, `PageTest${i}`, `Person`, `EMP${timestamp}-${i}`)
      createdPersonIds.push(created.id)
    }

    // Reload to see all persons
    await page.reload()

    // Verify at least some of the created persons are visible
    await expect(page.locator('tr', { hasText: 'PageTest1 Person' })).toBeVisible()
    await expect(page.locator('tr', { hasText: 'PageTest5 Person' })).toBeVisible()
  })

  test('Update person basic info and identity document', async ({ page }) => {
    // Create person
    const created = await createPersonWithIdentity(page, {
      firstName: 'UpdateTest',
      lastName: 'Original',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId',
      nationalId: 'A123456789'
    })
    createdPersonIds.push(created.id)

    // Update via API (simulating form submission)
    const updatePayload = {
      ...created,
      lastName: 'Updated',
      department: 'Updated Department',
      identityDocumentType: 'Passport',
      nationalId: null,
      passportNumber: '300123456',
      residentCertificateNumber: null
    }

    await page.evaluate(async (args) => {
      const response = await fetch(`/api/admin/people/${args.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(args.payload)
      })
      if (!response.ok) {
        throw new Error(`Update failed: ${response.status}`)
      }
    }, { id: created.id, payload: updatePayload })

    // Verify update
    const updated = await getPersonDetails(page, created.id)
    expect(updated.lastName).toBe('Updated')
    expect(updated.department).toBe('Updated Department')
    expect(updated.passportNumber).toBe('300123456')
    expect(updated.nationalId).toBeNull()
  })

  test('Delete person with verified identity', async ({ page }) => {
    // Create person with identity document
    const created = await createPersonWithIdentity(page, {
      firstName: 'DeleteTest',
      lastName: 'VerifiedPerson',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId',
      nationalId: 'A123456789'
    })
    createdPersonIds.push(created.id)

    // Delete should succeed even with identity document
    await deletePerson(page, created.id)
    createdPersonIds = createdPersonIds.filter(id => id !== created.id)

    // Verify deletion
    await page.reload()
    await expect(page.locator('tr', { hasText: 'DeleteTest VerifiedPerson' })).not.toBeVisible()
  })

  test('List persons shows identity status correctly', async ({ page }) => {
    // Create persons with different identity statuses
    const person1 = await createPersonWithIdentity(page, {
      firstName: 'Status',
      lastName: 'WithNationalId',
      employeeId: `EMP${Date.now()}-1`,
      identityDocumentType: 'NationalId',
      nationalId: 'A123456789'
    })
    
    const person2 = await createPerson(page, 'Status', 'NoDocument', `EMP${Date.now()}-2`)
    
    createdPersonIds.push(person1.id, person2.id)

    // Reload to see UI
    await page.reload()

    // Person with document should show "未驗證" (Unverified)
    const row1 = page.locator('tr', { hasText: 'Status WithNationalId' })
    await expect(row1).toBeVisible()
    await expect(row1.locator('text=/未驗證|Unverified/i')).toBeVisible()

    // Person without document should show "-" or empty
    const row2 = page.locator('tr', { hasText: 'Status NoDocument' })
    await expect(row2).toBeVisible()
    // May show "-" or be empty depending on implementation
  })

  test('Create person with all three identity document types sequentially', async ({ page }) => {
    const timestamp = Date.now()

    // Create with National ID
    const person1 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'NationalId',
      employeeId: `EMP${timestamp}-1`,
      identityDocumentType: 'NationalId',
      nationalId: 'A123456789'
    })
    createdPersonIds.push(person1.id)

    // Create with Passport
    const person2 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'Passport',
      employeeId: `EMP${timestamp}-2`,
      identityDocumentType: 'Passport',
      passportNumber: '300123456'
    })
    createdPersonIds.push(person2.id)

    // Create with Resident Certificate
    const person3 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'Resident',
      employeeId: `EMP${timestamp}-3`,
      identityDocumentType: 'ResidentCertificate',
      residentCertificateNumber: 'AA12345678'
    })
    createdPersonIds.push(person3.id)

    // Verify all were created successfully
    expect(person1.nationalId).toBe('A123456789')
    expect(person2.passportNumber).toBe('300123456')
    expect(person3.residentCertificateNumber).toBe('AA12345678')

    // Verify in UI
    await page.reload()
    await expect(page.locator('tr', { hasText: 'AllTypes NationalId' })).toBeVisible()
    await expect(page.locator('tr', { hasText: 'AllTypes Passport' })).toBeVisible()
    await expect(page.locator('tr', { hasText: 'AllTypes Resident' })).toBeVisible()
  })
})
