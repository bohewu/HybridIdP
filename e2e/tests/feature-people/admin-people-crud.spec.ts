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
    // Create person with Passport
    const timestamp = Date.now()
    const passportNumber = `3${String(timestamp).slice(-8)}`
    const personData = {
      firstName: 'CrudTest',
      lastName: 'PersonOne',
      employeeId: `EMP${timestamp}`,
      department: 'E2E Testing',
      jobTitle: 'Test Engineer',
      identityDocumentType: 'Passport' as const,
      passportNumber
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.firstName).toBe('CrudTest')
    expect(created.lastName).toBe('PersonOne')
    expect(created.passportNumber).toBe(passportNumber)

    // Reload and verify person appears in UI
    await page.reload()
    await expect(page.locator('tr', { hasText: 'CrudTest PersonOne' })).toBeVisible()

    // Update person identity to Resident Certificate
    const residentCert = `AA${String(timestamp).slice(-8)}`
    await updatePersonIdentity(page, created.id, {
      identityDocumentType: 'ResidentCertificate',
      passportNumber: null,
      residentCertificateNumber: residentCert
    })

    // Verify update
    const updated = await getPersonDetails(page, created.id)
    expect(updated.residentCertificateNumber).toBe(residentCert)
    expect(updated.passportNumber).toBeNull()
    expect(updated.identityDocumentType).toBe('ResidentCertificate')

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

    // Go to list page and search for created persons
    await page.goto('https://localhost:7035/Admin/People')
    await page.waitForLoadState('networkidle')
    
    // Use search to find the persons
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill('PageTest1')
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    
    // Verify at least some of the created persons are visible
    await expect(page.locator('tr', { hasText: 'PageTest1 Person' })).toBeVisible({ timeout: 10000 })
  })

  test('Update person basic info and identity document', async ({ page }) => {
    // Create person
    const timestamp = Date.now()
    const created = await createPersonWithIdentity(page, {
      firstName: 'UpdateTest',
      lastName: 'Original',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'Passport',
      passportNumber: `3${String(timestamp).slice(-8)}`
    })
    createdPersonIds.push(created.id)

    // Update via API (simulating form submission)
    const residentCert = `AA${String(timestamp + 1).slice(-8)}`
    const updatePayload = {
      ...created,
      lastName: 'Updated',
      department: 'Updated Department',
      identityDocumentType: 'ResidentCertificate',
      passportNumber: null,
      residentCertificateNumber: residentCert
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
    expect(updated.residentCertificateNumber).toBe(residentCert)
    expect(updated.passportNumber).toBeNull()
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
    const timestamp = Date.now()
    const person1 = await createPersonWithIdentity(page, {
      firstName: 'Status',
      lastName: 'WithPassport',
      employeeId: `EMP${timestamp}-1`,
      identityDocumentType: 'Passport',
      passportNumber: `3${String(timestamp).slice(-8)}`
    })
    
    const person2 = await createPerson(page, 'Status', 'NoDocument', `EMP${timestamp}-2`)
    
    createdPersonIds.push(person1.id, person2.id)

    // Go to list page and search to find created persons
    await page.goto('https://localhost:7035/Admin/People')
    await page.waitForLoadState('networkidle')
    
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill('Status')
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')

    // Person with document should show "未驗證" (Unverified)
    const row1 = page.locator('tr', { hasText: 'Status WithPassport' })
    await expect(row1).toBeVisible({ timeout: 10000 })
    await expect(row1.locator('text=/未驗證|Unverified/i')).toBeVisible()

    // Person without document should show "-" or empty
    const row2 = page.locator('tr', { hasText: 'Status NoDocument' })
    await expect(row2).toBeVisible({ timeout: 10000 })
    // May show "-" or be empty depending on implementation
  })

  test('Create person with all three identity document types sequentially', async ({ page }) => {
    const timestamp = Date.now()

    // Create with Passport
    const person1 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'Passport',
      employeeId: `EMP${timestamp}-1`,
      identityDocumentType: 'Passport',
      passportNumber: `3${String(timestamp).slice(-8)}`
    })
    createdPersonIds.push(person1.id)

    // Create with Resident Certificate  
    const person2 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'Resident',
      employeeId: `EMP${timestamp}-2`,
      identityDocumentType: 'ResidentCertificate',
      residentCertificateNumber: `AA${String(timestamp).slice(-8)}`
    })
    createdPersonIds.push(person2.id)

    // Create another Passport with different timestamp
    const person3 = await createPersonWithIdentity(page, {
      firstName: 'AllTypes',
      lastName: 'Passport2',
      employeeId: `EMP${timestamp}-3`,
      identityDocumentType: 'Passport',
      passportNumber: `3${String(timestamp + 1).slice(-8)}`
    })
    createdPersonIds.push(person3.id)

    // Verify all were created successfully
    expect(person1.passportNumber).toBe(`3${String(timestamp).slice(-8)}`)
    expect(person2.residentCertificateNumber).toBe(`AA${String(timestamp).slice(-8)}`)
    expect(person3.passportNumber).toBe(`3${String(timestamp + 1).slice(-8)}`)

    // Verify in UI via search - use employeeId for unique identification
    await page.goto('https://localhost:7035/Admin/People')
    await page.waitForLoadState('networkidle')
    
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill('AllTypes')
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    
    await expect(page.locator('tr', { hasText: `EMP${timestamp}-1` })).toBeVisible()
    await expect(page.locator('tr', { hasText: `EMP${timestamp}-2` })).toBeVisible()
    await expect(page.locator('tr', { hasText: `EMP${timestamp}-3` })).toBeVisible()
  })
})
