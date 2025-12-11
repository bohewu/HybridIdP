/**
 * Phase 18: Person Lifecycle E2E Tests
 * Tests for Person Status column, badges, and lifecycle management in the Admin UI
 */
import { test, expect } from '@playwright/test'
import {
  loginAsAdminViaIdP,
  deletePerson
} from '../helpers/admin'

const API_BASE = 'https://localhost:7035'

test.describe('Admin - Person Lifecycle Management', () => {
  let createdPersonIds: string[] = []

  test.beforeEach(async ({ page }) => {
    await loginAsAdminViaIdP(page)
    await page.goto(`${API_BASE}/Admin/People`)
    await page.waitForURL(/\/Admin\/People/)
  })

  test.afterEach(async ({ page }) => {
    // Clean up all created persons
    for (const personId of createdPersonIds) {
      await deletePerson(page, personId)
    }
    createdPersonIds = []
  })

  test('Create person with Active status - shows green badge', async ({ page }) => {
    const timestamp = Date.now()
    
    // Create person via API with Active status
    const response = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'LifecycleTest',
        lastName: 'ActivePerson',
        employeeId: `EMP${timestamp}`,
        status: 'Active',
        identityDocumentType: 'None'
      }
    })
    expect(response.ok()).toBeTruthy()
    const created = await response.json()
    createdPersonIds.push(created.id)

    // Verify status was set
    expect(created.status).toBe('Active')
    expect(created.canAuthenticate).toBe(true)

    // Reload and verify UI shows green Active badge
    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('LifecycleTest')
    await page.waitForLoadState('networkidle')
    
    // Find the row
    const row = page.locator('tr', { hasText: 'LifecycleTest ActivePerson' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    // Verify status badge - look for Active badge with green styling
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toBeVisible()
    await expect(statusBadge).toContainText(/Active|啟用中/i)
    // Green badge class
    await expect(statusBadge).toHaveClass(/bg-green-100/)
  })

  test('Create person with Pending status - shows blue badge', async ({ page }) => {
    const timestamp = Date.now()
    const futureDate = new Date()
    futureDate.setDate(futureDate.getDate() + 7) // Start date 7 days in the future
    
    const response = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'LifecycleTest',
        lastName: 'PendingPerson',
        employeeId: `EMP${timestamp}`,
        status: 'Pending',
        startDate: futureDate.toISOString(),
        identityDocumentType: 'None'
      }
    })
    expect(response.ok()).toBeTruthy()
    const created = await response.json()
    createdPersonIds.push(created.id)

    expect(created.status).toBe('Pending')
    expect(created.canAuthenticate).toBe(false)

    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('LifecycleTest')
    await page.waitForLoadState('networkidle')
    
    const row = page.locator('tr', { hasText: 'LifecycleTest PendingPerson' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toContainText(/Pending|待啟用/i)
    await expect(statusBadge).toHaveClass(/bg-blue-100/)
  })

  test('Create person with Suspended status - shows orange badge', async ({ page }) => {
    const timestamp = Date.now()
    
    const response = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'LifecycleTest',
        lastName: 'SuspendedPerson',
        employeeId: `EMP${timestamp}`,
        status: 'Suspended',
        identityDocumentType: 'None'
      }
    })
    expect(response.ok()).toBeTruthy()
    const created = await response.json()
    createdPersonIds.push(created.id)

    expect(created.status).toBe('Suspended')
    expect(created.canAuthenticate).toBe(false)

    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('LifecycleTest')
    await page.waitForLoadState('networkidle')
    
    const row = page.locator('tr', { hasText: 'LifecycleTest SuspendedPerson' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toContainText(/Suspended|已停權/i)
    await expect(statusBadge).toHaveClass(/bg-orange-100/)
  })

  test('Create person with Terminated status - shows red badge', async ({ page }) => {
    const timestamp = Date.now()
    
    const response = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'LifecycleTest',
        lastName: 'TerminatedPerson',
        employeeId: `EMP${timestamp}`,
        status: 'Terminated',
        identityDocumentType: 'None'
      }
    })
    expect(response.ok()).toBeTruthy()
    const created = await response.json()
    createdPersonIds.push(created.id)

    expect(created.status).toBe('Terminated')
    expect(created.canAuthenticate).toBe(false)

    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('LifecycleTest')
    await page.waitForLoadState('networkidle')
    
    const row = page.locator('tr', { hasText: 'LifecycleTest TerminatedPerson' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toContainText(/Terminated|已終止/i)
    await expect(statusBadge).toHaveClass(/bg-red-100/)
  })

  test('Active person with future StartDate shows clock icon', async ({ page }) => {
    const timestamp = Date.now()
    const futureDate = new Date()
    futureDate.setDate(futureDate.getDate() + 7)
    
    const response = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'LifecycleTest',
        lastName: 'FutureStart',
        employeeId: `EMP${timestamp}`,
        status: 'Active',
        startDate: futureDate.toISOString(),
        identityDocumentType: 'None'
      }
    })
    expect(response.ok()).toBeTruthy()
    const created = await response.json()
    createdPersonIds.push(created.id)

    // Active status but canAuthenticate should be false due to future start date
    expect(created.status).toBe('Active')
    expect(created.canAuthenticate).toBe(false)

    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('LifecycleTest')
    await page.waitForLoadState('networkidle')
    
    const row = page.locator('tr', { hasText: 'LifecycleTest FutureStart' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    // Should show yellow badge (Active but can't authenticate) and clock icon
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toHaveClass(/bg-yellow-100/)
    
    // Clock icon for date restriction
    const clockIcon = row.locator('[data-test-id="person-date-restricted"]')
    await expect(clockIcon).toBeVisible()
  })

  test('Status column header is visible in Person list', async ({ page }) => {
    // Verify the Status column header exists
    const statusHeader = page.locator('th', { hasText: /Status|狀態/i })
    await expect(statusHeader).toBeVisible()
  })

  test('Update person status from Active to Suspended via API', async ({ page }) => {
    const timestamp = Date.now()
    
    // Create Active person
    const createResponse = await page.request.post(`${API_BASE}/api/admin/people`, {
      data: {
        firstName: 'StatusChange',
        lastName: 'TestPerson',
        employeeId: `EMP${timestamp}`,
        status: 'Active',
        identityDocumentType: 'None'
      }
    })
    expect(createResponse.ok()).toBeTruthy()
    const created = await createResponse.json()
    createdPersonIds.push(created.id)

    // Update to Suspended
    const updateResponse = await page.request.put(`${API_BASE}/api/admin/people/${created.id}`, {
      data: {
        ...created,
        status: 'Suspended'
      }
    })
    expect(updateResponse.ok()).toBeTruthy()
    const updated = await updateResponse.json()
    
    expect(updated.status).toBe('Suspended')
    expect(updated.canAuthenticate).toBe(false)

    // Verify in UI
    await page.reload()
    const searchInput = page.locator('[data-test-id="person-search"], input[placeholder*="Search" i]')
    await searchInput.fill('StatusChange')
    await page.waitForLoadState('networkidle')
    
    const row = page.locator('tr', { hasText: 'StatusChange TestPerson' })
    await expect(row).toBeVisible({ timeout: 10000 })
    
    const statusBadge = row.locator('[data-test-id="person-status-badge"]')
    await expect(statusBadge).toContainText(/Suspended|已停權/i)
  })

  test('Persons with different statuses show correct canAuthenticate values', async ({ page }) => {
    const timestamp = Date.now()
    
    // Create persons with each status
    const statuses = ['Pending', 'Active', 'Suspended', 'Resigned', 'Terminated'] as const
    
    for (let i = 0; i < statuses.length; i++) {
      const response = await page.request.post(`${API_BASE}/api/admin/people`, {
        data: {
          firstName: 'CanAuth',
          lastName: `Test${statuses[i]}`,
          employeeId: `EMP${timestamp}-${i}`,
          status: statuses[i],
          identityDocumentType: 'None'
        }
      })
      expect(response.ok()).toBeTruthy()
      const created = await response.json()
      createdPersonIds.push(created.id)
      
      // Only Active should be able to authenticate
      if (statuses[i] === 'Active') {
        expect(created.canAuthenticate).toBe(true)
      } else {
        expect(created.canAuthenticate).toBe(false)
      }
    }
  })
})
