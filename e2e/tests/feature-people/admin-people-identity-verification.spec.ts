import { test, expect } from '@playwright/test'
import {
  loginAsAdminViaIdP,
  createPersonWithIdentity,
  deletePerson,
  verifyPersonIdentity,
  getPersonDetails,
  updatePersonIdentity
} from '../helpers/admin'

/**
 * Generate a valid Taiwan National ID with correct checksum
 * Based on algorithm from useIdentityValidation.js and IdentityDocumentValidator.cs
 * 
 * @param letter - First letter (A-Z)
 * @param digits - 9 digits as string (last digit will be replaced with checksum)
 * @returns Valid Taiwan National ID
 */
function generateValidNationalId(letter: string, digits: string): string {
  const letterMapping: Record<string, number> = {
    A: 10, B: 11, C: 12, D: 13, E: 14, F: 15, G: 16, H: 17, I: 34, J: 18,
    K: 19, L: 20, M: 21, N: 22, O: 35, P: 23, Q: 24, R: 25, S: 26, T: 27,
    U: 28, V: 29, W: 32, X: 30, Y: 31, Z: 33
  }

  const letterValue = letterMapping[letter.toUpperCase()]
  const firstDigit = Math.floor(letterValue / 10)
  const secondDigit = letterValue % 10
  const weights = [1, 9, 8, 7, 6, 5, 4, 3, 2, 1]

  // Use first 8 digits from input
  const first8Digits = digits.slice(0, 8)
  
  // Calculate sum with first 8 digits
  let sum = firstDigit * weights[0] + secondDigit * weights[1]
  for (let i = 0; i < 8; i++) {
    sum += parseInt(first8Digits[i]) * weights[i + 2]
  }

  // Calculate checksum digit
  const checksum = (10 - (sum % 10)) % 10

  return `${letter.toUpperCase()}${first8Digits}${checksum}`
}

test.describe('Admin - People Identity Verification', () => {
  let createdPersonIds: string[] = []
  let nationalIdCounter = 0 // Track unique ID generation
  
  // Helper to get next unique National ID with valid checksum
  const getNextNationalId = () => {
    const letters = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T']
    const letter = letters[nationalIdCounter % letters.length]
    const baseDigits = `${100000000 + nationalIdCounter}`.slice(0, 8)
    nationalIdCounter++
    return generateValidNationalId(letter, baseDigits + '0')
  }

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
    const timestamp = Date.now()
    const nationalId = getNextNationalId()
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidNationalId',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'NationalId' as const,
      nationalId // Valid Taiwan National ID with correct checksum
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.nationalId).toBe(nationalId)
    expect(created.identityDocumentType).toBe('NationalId')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list via search
    await page.reload()
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    await expect(page.locator('tr', { hasText: personData.employeeId })).toBeVisible()
  })

  test('Create person with valid Passport Number', async ({ page }) => {
    const timestamp = Date.now()
    const passportNumber = `3${String(timestamp).slice(-8)}`
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidPassport',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'Passport' as const,
      passportNumber // Valid passport format
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.passportNumber).toBe(passportNumber)
    expect(created.identityDocumentType).toBe('Passport')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list via search
    await page.reload()
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    await expect(page.locator('tr', { hasText: personData.employeeId })).toBeVisible()
  })

  test('Create person with valid Resident Certificate', async ({ page }) => {
    const timestamp = Date.now()
    const residentCertificateNumber = `AA${String(timestamp).slice(-8)}`
    const personData = {
      firstName: 'TestUser',
      lastName: 'ValidResident',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'ResidentCertificate' as const,
      residentCertificateNumber // Valid resident certificate format
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    expect(created.residentCertificateNumber).toBe(residentCertificateNumber)
    expect(created.identityDocumentType).toBe('ResidentCertificate')
    expect(created.identityVerifiedAt).toBeNull()

    // Verify person appears in list via search
    await page.reload()
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    await expect(page.locator('tr', { hasText: personData.employeeId })).toBeVisible()
  })

  test('Verify person identity with National ID successfully', async ({ page }) => {
    // Create person with National ID
    const timestamp = Date.now()
    const personData = {
      firstName: 'TestUser',
      lastName: 'ToVerify',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: getNextNationalId()
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
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
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
    const timestamp = Date.now()
    const personData = {
      firstName: 'TestUser',
      lastName: 'ChangeDocument',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: getNextNationalId()
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Verify identity
    await verifyPersonIdentity(page, created.id)
    
    let details = await getPersonDetails(page, created.id)
    expect(details.identityVerifiedAt).not.toBeNull()

    // Update to Passport Number (should reset verification)
    const passportNumber = `3${String(timestamp).slice(-8)}`
    await updatePersonIdentity(page, created.id, {
      identityDocumentType: 'Passport',
      nationalId: null,
      passportNumber
    })

    // Check verification was reset
    details = await getPersonDetails(page, created.id)
    expect(details.passportNumber).toBe(passportNumber)
    expect(details.nationalId).toBeNull()
    expect(details.identityVerifiedAt).toBeNull() // Should be reset
  })

  test('Attempt to create duplicate person with same National ID fails', async ({ page }) => {
    // Create first person with National ID
    const timestamp = Date.now()
    const nationalId = getNextNationalId()
    const personData1 = {
      firstName: 'FirstPerson',
      lastName: 'OriginalID',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'NationalId' as const,
      nationalId
    }

    const created1 = await createPersonWithIdentity(page, personData1)
    createdPersonIds.push(created1.id)

    // Attempt to create second person with same National ID should fail
    const personData2 = {
      firstName: 'SecondPerson',
      lastName: 'DuplicateID',
      employeeId: `EMP${timestamp + 1}`,
      identityDocumentType: 'NationalId' as const,
      nationalId // Same National ID
    }

    await expect(async () => {
      await createPersonWithIdentity(page, personData2)
    }).rejects.toThrow(/duplicate|unique|already exists/i)
  })

  test('Attempt to create duplicate person with same Passport Number fails', async ({ page }) => {
    // Create first person with Passport
    const timestamp = Date.now()
    const passportNumber = `3${String(timestamp).slice(-8)}`
    const personData1 = {
      firstName: 'FirstPerson',
      lastName: 'OriginalPassport',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'Passport' as const,
      passportNumber
    }

    const created1 = await createPersonWithIdentity(page, personData1)
    createdPersonIds.push(created1.id)

    // Attempt to create second person with same Passport Number should fail
    const personData2 = {
      firstName: 'SecondPerson',
      lastName: 'DuplicatePassport',
      employeeId: `EMP${timestamp + 1}`,
      identityDocumentType: 'Passport' as const,
      passportNumber // Same Passport Number
    }

    await expect(async () => {
      await createPersonWithIdentity(page, personData2)
    }).rejects.toThrow(/duplicate|unique|already exists/i)
  })

  test('Verify identity button is visible for unverified person with document', async ({ page }) => {
    // Create person with identity document
    const timestamp = Date.now()
    const personData = {
      firstName: 'TestUser',
      lastName: 'UnverifiedDoc',
      employeeId: `EMP${timestamp}`,
      identityDocumentType: 'NationalId' as const,
      nationalId: getNextNationalId()
    }

    const created = await createPersonWithIdentity(page, personData)
    createdPersonIds.push(created.id)

    // Reload to see UI
    await page.reload()
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
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
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    const row = page.locator('tr', { hasText: 'TestUser AlreadyVerified' })
    await expect(row).toBeVisible()

    // Verify button should NOT be visible
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    await expect(verifyButton).not.toBeVisible()

    // But verified badge should be visible (use first() to avoid strict mode violation)
    await expect(row.locator('text=/已驗證|Verified/i').first()).toBeVisible()
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
    const searchInput = page.locator('input[placeholder*="Search" i], input[type="search"]')
    await searchInput.fill(personData.employeeId)
    await searchInput.press('Enter')
    await page.waitForLoadState('networkidle')
    const row = page.locator('tr', { hasText: 'TestUser NoDocNoButton' })
    await expect(row).toBeVisible()

    // Verify button should NOT be visible
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    await expect(verifyButton).not.toBeVisible()
  })

  test('Multiple document types: National ID checksum validation variants', async ({ page }) => {
    // Test multiple valid Taiwan National IDs with different checksums
    const validIds = [
      { id: getNextNationalId(), firstName: 'Valid1' },
      { id: getNextNationalId(), firstName: 'Valid2' },
      { id: getNextNationalId(), firstName: 'Valid3' }
    ]

    for (const { id, firstName } of validIds) {
      const timestamp = Date.now()
      const personData = {
        firstName,
        lastName: 'NationalIdTest',
        employeeId: `EMP${timestamp}-${firstName}`,
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
