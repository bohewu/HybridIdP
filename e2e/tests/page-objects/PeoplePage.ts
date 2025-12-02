import { Page, Locator } from '@playwright/test'

/**
 * Page Object Model for People Management page
 * Centralizes selectors and common operations for maintainability
 */
export class PeoplePage {
  readonly page: Page
  readonly searchInput: Locator
  readonly createButton: Locator
  readonly tableBody: Locator

  constructor(page: Page) {
    this.page = page
    this.searchInput = page.locator('input[placeholder*="Search"], input[placeholder*="搜尋"]')
    this.createButton = page.locator('button:has-text("Create Person"), button:has-text("建立人員")')
    this.tableBody = page.locator('tbody')
  }

  /**
   * Navigate to the People management page
   */
  async navigate() {
    await this.page.goto('https://localhost:7035/Admin/People')
    await this.page.waitForURL(/\/Admin\/People/)
  }

  /**
   * Search for persons by name, employee ID, or department
   */
  async searchPerson(query: string) {
    await this.searchInput.fill(query)
    // Wait for API response
    await this.page.waitForResponse(
      response => response.url().includes('/api/admin/people') && response.request().method() === 'GET',
      { timeout: 5000 }
    ).catch(() => {
      // Fallback to timeout if no API call
    })
    await this.page.waitForTimeout(500) // Allow UI to update
  }

  /**
   * Get a person row by name
   */
  getPersonRow(firstName: string, lastName: string): Locator {
    return this.page.locator('tr', { hasText: `${firstName} ${lastName}` })
  }

  /**
   * Get the identity status badge for a person
   */
  async getIdentityStatus(firstName: string, lastName: string): Promise<string | null> {
    const row = this.getPersonRow(firstName, lastName)
    
    // Try to find verified badge
    const verifiedBadge = row.locator('text=/已驗證|Verified/i')
    if (await verifiedBadge.isVisible()) {
      return 'verified'
    }

    // Try to find unverified badge
    const unverifiedBadge = row.locator('text=/未驗證|Unverified/i')
    if (await unverifiedBadge.isVisible()) {
      return 'unverified'
    }

    // No identity document
    return null
  }

  /**
   * Check if verify identity button is visible for a person
   */
  async isVerifyButtonVisible(firstName: string, lastName: string): Promise<boolean> {
    const row = this.getPersonRow(firstName, lastName)
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]')
    return await verifyButton.isVisible()
  }

  /**
   * Click verify identity button for a person
   */
  async clickVerifyIdentity(firstName: string, lastName: string) {
    const row = this.getPersonRow(firstName, lastName)
    const verifyButton = row.locator('button[title*="驗證"], button[title*="Verify"]').first()
    await verifyButton.click()

    // Wait for confirmation dialog or modal
    await this.page.waitForTimeout(500)
  }

  /**
   * Click edit button for a person
   */
  async clickEditPerson(firstName: string, lastName: string) {
    const row = this.getPersonRow(firstName, lastName)
    const editButton = row.locator('button[title*="Edit"], button[title*="編輯"]').first()
    await editButton.click()

    // Wait for form modal to appear
    await this.page.waitForSelector('[role="dialog"], .modal', { timeout: 3000 }).catch(() => {})
  }

  /**
   * Click delete button for a person
   */
  async clickDeletePerson(firstName: string, lastName: string) {
    const row = this.getPersonRow(firstName, lastName)
    const deleteButton = row.locator('button[title*="Delete"], button[title*="刪除"]').first()
    await deleteButton.click()

    // Wait for confirmation dialog
    await this.page.waitForTimeout(500)
  }

  /**
   * Click manage accounts button for a person
   */
  async clickManageAccounts(firstName: string, lastName: string) {
    const row = this.getPersonRow(firstName, lastName)
    const accountsButton = row.locator('button[title*="Account"], button[title*="帳號"]').first()
    await accountsButton.click()

    // Wait for accounts dialog
    await this.page.waitForSelector('[role="dialog"], .modal', { timeout: 3000 }).catch(() => {})
  }

  /**
   * Confirm action in dialog (e.g., delete confirmation, verify confirmation)
   */
  async confirmDialog() {
    const confirmButton = this.page.locator('button:has-text("確認"), button:has-text("Confirm"), button:has-text("Yes")')
    await confirmButton.click()
  }

  /**
   * Cancel action in dialog
   */
  async cancelDialog() {
    const cancelButton = this.page.locator('button:has-text("取消"), button:has-text("Cancel"), button:has-text("No")')
    await cancelButton.click()
  }

  /**
   * Get the number of linked accounts shown for a person
   */
  async getLinkedAccountsCount(firstName: string, lastName: string): Promise<number> {
    const row = this.getPersonRow(firstName, lastName)
    
    // Look for accounts count badge or text
    const accountsCell = row.locator('td').nth(4) // Adjust index based on table structure
    const text = await accountsCell.textContent()
    
    if (!text) return 0

    // Extract number from text like "2 linked accounts" or "已連結 2 個帳號"
    const match = text.match(/(\d+)/)
    return match ? parseInt(match[1]) : 0
  }

  /**
   * Check if a person exists in the list
   */
  async personExists(firstName: string, lastName: string): Promise<boolean> {
    const row = this.getPersonRow(firstName, lastName)
    return await row.isVisible()
  }

  /**
   * Get all visible person rows
   */
  async getAllPersonRows(): Promise<Locator[]> {
    const rows = await this.tableBody.locator('tr').all()
    return rows
  }

  /**
   * Wait for person list to load
   */
  async waitForPersonsToLoad() {
    await this.page.waitForSelector('tbody tr', { timeout: 10000 }).catch(() => {
      // May not have any persons
    })
  }

  /**
   * Check if "no persons found" message is visible
   */
  async isNoPersonsMessageVisible(): Promise<boolean> {
    const noDataMessage = this.page.locator('text=/No persons|找不到|沒有人員/i')
    return await noDataMessage.isVisible()
  }

  /**
   * Fill the person creation/edit form
   */
  async fillPersonForm(data: {
    firstName?: string
    lastName?: string
    employeeId?: string
    department?: string
    jobTitle?: string
    identityDocumentType?: 'None' | 'NationalId' | 'Passport' | 'ResidentCertificate'
    nationalId?: string
    passportNumber?: string
    residentCertificateNumber?: string
  }) {
    if (data.firstName) {
      await this.page.fill('#firstName', data.firstName)
    }
    if (data.lastName) {
      await this.page.fill('#lastName', data.lastName)
    }
    if (data.employeeId) {
      await this.page.fill('#employeeId', data.employeeId)
    }
    if (data.department) {
      await this.page.fill('#department', data.department)
    }
    if (data.jobTitle) {
      await this.page.fill('#jobTitle', data.jobTitle)
    }
    if (data.identityDocumentType) {
      await this.page.selectOption('#identityDocumentType', data.identityDocumentType)
    }
    if (data.nationalId) {
      await this.page.fill('#nationalId', data.nationalId)
    }
    if (data.passportNumber) {
      await this.page.fill('#passportNumber', data.passportNumber)
    }
    if (data.residentCertificateNumber) {
      await this.page.fill('#residentCertificateNumber', data.residentCertificateNumber)
    }
  }

  /**
   * Submit the person form
   */
  async submitPersonForm() {
    const saveButton = this.page.locator('button:has-text("Save"), button:has-text("儲存")')
    await saveButton.click()

    // Wait for API response
    await this.page.waitForResponse(
      response => {
        const url = response.url()
        return (url.includes('/api/admin/people') && 
               (response.request().method() === 'POST' || response.request().method() === 'PUT'))
      },
      { timeout: 5000 }
    ).catch(() => {})

    // Wait for modal to close
    await this.page.waitForTimeout(500)
  }

  /**
   * Create a new person via UI form
   */
  async createPersonViaUI(data: {
    firstName: string
    lastName: string
    employeeId?: string
    department?: string
    jobTitle?: string
    identityDocumentType?: 'None' | 'NationalId' | 'Passport' | 'ResidentCertificate'
    nationalId?: string
    passportNumber?: string
    residentCertificateNumber?: string
  }) {
    await this.createButton.click()
    await this.page.waitForSelector('[role="dialog"], .modal', { timeout: 3000 })
    
    await this.fillPersonForm(data)
    await this.submitPersonForm()
  }
}
