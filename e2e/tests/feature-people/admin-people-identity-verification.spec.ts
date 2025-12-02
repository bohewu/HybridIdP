import { test, expect } from '@playwright/test'
import {
  loginAsAdminViaIdP,
  createPersonWithIdentity,
  deletePerson,
  verifyPersonIdentity,
  getPersonDetails,
  updatePersonIdentity
} from '../helpers/admin'

test.describe('Admin - People Identity Verification', () => {
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

  test('Create person with valid Taiwan National ID', async ({ page }) => {
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidNationalId',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789' // Valid Taiwan National ID with correct checksum
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.nationalId).toBe('A123456789')
    expect(created.identityDocumentType).toBe('NationalId')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list
    await page.reload()
    await expect(page.locator('tr', { hasText: 'TestUser ValidNationalId' })).toBeVisible()
  })

  test('Create person with valid Passport Number', async ({ page }) => {
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidPassport',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'Passport' as const,
      passportNumber: '300123456' // Valid passport format
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.passportNumber).toBe('300123456')
    expect(created.identityDocumentType).toBe('Passport')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list
    await page.reload()
    await expect(page.locator('tr', { hasText: 'TestUser ValidPassport' })).toBeVisible()
  })

  test('Create person with valid Resident Certificate', async ({ page }) => {
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidResident',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'ResidentCertificate' as const,
      residentCertificateNumber: 'AA12345678' // Valid resident certificate format
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.residentCertificateNumber).toBe('AA12345678')
    expect(created.identityDocumentType).toBe('ResidentCertificate')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list
    await page.reload()
    await expect(page.locator('tr', { hasText: 'TestUser ValidResident' })).toBeVisible()
  })

  test('Verify person identity with National ID successfully', async ({ page }) => {
    // Create person with National ID
    const personData = {
      firstName: 'TestUser',
      lastName: 'ToVerify',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Verify initially not verified
    expect(created.identityVerifiedAt).toBeNull()

    // Call verify identity API
    await verifyPersonIdentity(page, created.id)

    // Get updated person details
    const updated = await getPersonDetails(page, created.id)
    expect(updated.identityVerifiedAt).not.toBeNull()
    expect(updated.identityVerifiedBy).not.toBeNull()

    // Reload page and check UI shows verified status
    await page.reload()
    const row = page.locator('tr', { hasText: 'TestUser ToVerify' })
    await expect(row).toBeVisible()
    
    // Check for verified badge/status (may be in Chinese "已驗證" or English "Verified")
    await expect(row.locator('text=/已驗證|Verified/i')).toBeVisible()
  })

  test('Cannot verify identity without identity document', async ({ page }) => {
    // Create person without any identity document
    const personData = {
      firstName: 'TestUser',
      lastName: 'NoDocument',
      employeeId: `EMP${Date.now()}`
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Attempt to verify identity should fail with error
    await expect(async () => {
      await verifyPersonIdentity(page, created.id)
    }).rejects.toThrow(/400|No identity document/)
  })

  test('Update person identity document type resets verification', async ({ page }) => {
    // Create person with National ID and verify
    const personData = {
      firstName: 'TestUser',
      lastName: 'ChangeDocument',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Verify identity
    await verifyPersonIdentity(page, created.id)
    
    let details = await getPersonDetails(page, created.id)
    expect(details.identityVerifiedAt).not.toBeNull()

    // Update to Passport Number (should reset verification)
    await updatePersonIdentity(page, created.id, {
      identityDocumentType: 'Passport',
      nationalId: null,
      passportNumber: '300123456'
    })

    // Check verification was reset
    details = await getPersonDetails(page, created.id)
    expect(details.passportNumber).toBe('300123456')
    expect(details.nationalId).toBeNull()
    expect(details.identityVerifiedAt).toBeNull() // Should be reset
  })

  test('Attempt to create duplicate person with same National ID fails', async ({ page }) => {
    // Create first person with National ID
    const personData1 = {
      firstName: 'FirstPerson',
      lastName: 'OriginalID',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created1 = await createPersonWithIdentity(page, personData1)
    createdPersonIds.push(created1.id)

    // Attempt to create second person with same National ID should fail
    const personData2 = {
      firstName: 'SecondPerson',
      lastName: 'DuplicateID',
      employeeId: `EMP${Date.now() + 1}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789' // Same National ID
    }

    await expect(async () => {
      await createPersonWithIdentity(page, personData2)
    }).rejects.toThrow(/duplicate|unique|already exists/i)
  })

  test('Attempt to create duplicate person with same Passport Number fails', async ({ page }) => {
    // Create first person with Passport
    const personData1 = {
      firstName: 'FirstPerson',
      lastName: 'OriginalPassport',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'Passport' as const,
      passportNumber: '300123456'
    }

    const created1 = await createPersonWithIdentity(page, personData1)
    createdPersonIds.push(created1.id)

    // Attempt to create second person with same Passport Number should fail
    const personData2 = {
      firstName: 'SecondPerson',
      lastName: 'DuplicatePassport',
      employeeId: `EMP${Date.now() + 1}`,
      identityDocumentType: 'Passport' as const,
      passportNumber: '300123456' // Same Passport Number
    }

    await expect(async () => {
      await createPersonWithIdentity(page, personData2)
    }).rejects.toThrow(/duplicate|unique|already exists/i)
  })

  test('Verify identity button is visible for unverified person with document', async ({ page }) => {
    // Create person with identity document
    const personData = {
      firstName: 'TestUser',
      lastName: 'UnverifiedDoc',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Reload to see UI
    await page.reload()
    
    const row = page.locator('tr', { hasText: 'TestUser UnverifiedDoc' })
    await expect(row).toBeVisible()

    // Verify button should be visible (look for button with verify icon or title)
    // The button might have title attribute or specific icon
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    await expect(verifyButton).toBeVisible()
  })

  test('Verify identity button is hidden for already verified person', async ({ page }) => {
    // Create and verify person
    const personData = {
      firstName: 'TestUser',
      lastName: 'AlreadyVerified',
      employeeId: `EMP${Date.now()}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: 'A123456789'
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Verify identity
    await verifyPersonIdentity(page, created.id)

    // Reload to see UI
    await page.reload()
    
    const row = page.locator('tr', { hasText: 'TestUser AlreadyVerified' })
    await expect(row).toBeVisible()

    // Verify button should NOT be visible
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    await expect(verifyButton).not.toBeVisible()

    // But verified badge should be visible
    await expect(row.locator('text=/已驗證|Verified/i')).toBeVisible()
  })

  test('Person without identity document does not show verify button', async ({ page }) => {
    // Create person without identity document
    const personData = {
      firstName: 'TestUser',
      lastName: 'NoDocNoButton',
      employeeId: `EMP${Date.now()}`
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Reload to see UI
    await page.reload()
    
    const row = page.locator('tr', { hasText: 'TestUser NoDocNoButton' })
    await expect(row).toBeVisible()

    // Verify button should NOT be visible
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    await expect(verifyButton).not.toBeVisible()
  })

  test('Multiple document types: National ID checksum validation variants', async ({ page }) => {
    // Test multiple valid Taiwan National IDs with different checksums
    const validIds = [
      { id: 'A123456789', firstName: 'Valid1' },
      { id: 'B234567890', firstName: 'Valid2' },
      { id: 'Z198765432', firstName: 'Valid3' }
    ]

    for (const { id, firstName } of validIds) {
      const personData = {
        firstName,
        lastName: 'NationalIdTest',
        employeeId: `EMP${Date.now()}-${firstName}`,
        identityDocumentType: 'NationalId' as const,
        nationalId: id
      }

      const created = await createPersonWithIdentity(page, personData)
      createdPersonIds.push(created.id)

      expect(created.nationalId).toBe(id)
    }

    // Verify all created persons appear
    await page.reload()
    for (const { firstName } of validIds) {
      await expect(page.locator('tr', { hasText: `${firstName} NationalIdTest` })).toBeVisible()
    }
  })
})
