import { test, expect } from '@playwright/test'
import adminHelpers from '../helpers/admin'
import recreateHelper from '../helpers/recreateTestClient'

// Focused tests for ClientScopeManager component only.
test.describe('ClientScopeManager - UI interactions and persistence', () => {
  let clientId: string
  let clientGuid: string
  const scopes: string[] = []

  test.beforeAll(async ({ page }) => {
    // Ensure a canonical testclient exists (recreate) and prepare test scopes
    await recreateHelper.recreateTestClientViaApi(page)

    // Login as admin
    await adminHelpers.loginAsAdminViaIdP(page)

    // Create a set of scopes used by these tests (15 scopes -> pagination)
    const prefix = `e2e-csm-${Date.now()}`
    for (let i = 1; i <= 15; i++) {
      const name = `${prefix}-${i}`
      scopes.push(name)
      try {
        await adminHelpers.createScope(page, name, `E2E ${name}`, `E2E description ${name}`)
      } catch (e) {
        // ignore if exists
      }
    }

    // Create a client that we will edit in tests
    clientId = `e2e-csm-client-${Date.now()}`
    const created = await page.evaluate(async (id) => {
      const payload = {
        clientId: id,
        displayName: `E2E CSM Client ${id}`,
        applicationType: 'web',
        type: 'public',
        consentType: 'explicit',
        clientSecret: null,
        redirectUris: ['https://localhost:7001/signin-oidc'],
        postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
        permissions: ['ept:authorization','ept:token','gt:authorization_code','scp:openid']
      }
      const r = await fetch('/api/admin/clients', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(payload) })
      const json = await r.json()
      return json
    }, clientId)
    clientGuid = created.id
  })

  test.afterAll(async ({ page }) => {
    // Cleanup created client and scopes
    try {
      await adminHelpers.deleteClientViaApiFallback(page, clientId)
    } catch (e) {}

    for (const s of scopes) {
      try { await adminHelpers.deleteScope(page, s) } catch (e) {}
    }
  })

  test('add available scope -> selected + persisted', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/Clients')

    // Open edit modal for our created client
    const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result.clicked).toBeTruthy()

    // Wait for modal
    await page.waitForSelector('#clientId')

    // Add first scope from available list
    const targetScope = scopes[0]
    const availableItem = page.locator('[data-test="csm-available-item"]', { hasText: targetScope }).first()
    await expect(availableItem).toBeVisible({ timeout: 10000 })
    // Click the add button inside the available item
    await availableItem.locator('button').click()

    // Verify appears in selected list
    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 5000 })

    // Submit update and wait for scopes PUT
    const putScopes = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/scopes`) && r.request().method() === 'PUT')
    await page.click('button[type="submit"]')
    await putScopes

    // Validate server persisted selection
    const server = await page.evaluate(async (id) => {
      const r = await fetch(`/api/admin/clients/${id}/scopes`)
      return (r.ok ? r.json() : null)
    }, clientGuid)
    expect(server).not.toBeNull()
    expect(server.scopes).toContain(targetScope)
  })

  test('toggle required on selected scope is persisted', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/Clients')

    // Edit again
    const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result.clicked).toBeTruthy()
    await page.waitForSelector('#clientId')

    const targetScope = scopes[0]
    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 5000 })

    // Toggle required switch (button[role="switch"] inside selected scope)
    const toggle = selected.locator('button[role="switch"]').first()
    await expect(toggle).toBeVisible()
    await toggle.click()

    // Submit and wait for required scopes PUT
    const putRequired = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/required-scopes`) && r.request().method() === 'PUT')
    await page.click('button[type="submit"]')
    await putRequired

    // Confirm required persisted
    const requiredServer = await page.evaluate(async (id) => {
      const r = await fetch(`/api/admin/clients/${id}/required-scopes`)
      return (r.ok ? r.json() : null)
    }, clientGuid)
    expect(requiredServer).not.toBeNull()
    expect(requiredServer.scopes).toContain(targetScope)
  })

  test('remove selected scope clears required and persists', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/Clients')
    // Edit
    const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result.clicked).toBeTruthy()
    await page.waitForSelector('#clientId')

    const targetScope = scopes[0]
    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 5000 })

    // Click remove button (title attr) inside selected item
    const removeBtn = selected.locator('button[title], button:has-text("Remove")').first()
    await removeBtn.click()

    // Submit and wait for scopes PUT
    const putScopes = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/scopes`) && r.request().method() === 'PUT')
    const putRequired = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/required-scopes`) && r.request().method() === 'PUT')
    await page.click('button[type="submit"]')
    await Promise.all([putScopes, putRequired])

    // Confirm server no longer contains the scope in allowed or required
    const server = await page.evaluate(async (id) => {
      const a = await fetch(`/api/admin/clients/${id}/scopes`); const ar = await a.json();
      const b = await fetch(`/api/admin/clients/${id}/required-scopes`); const br = await b.json();
      return { allowed: ar.scopes || [], required: br.scopes || [] }
    }, clientGuid)
    expect(server.allowed).not.toContain(targetScope)
    expect(server.required).not.toContain(targetScope)
  })

  test('pagination and search in available list work', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/Clients')

    // Edit
    const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result.clicked).toBeTruthy()
    await page.waitForSelector('#clientId')

    // Page 2 item (page size 10) - pick something high index
    const page2Scope = scopes[12] // should be on page 2

    // Advance pagination on available column
    const nextBtn = page.locator('div:has([data-test="csm-available-header"]) + div [aria-label="next"]');
    // There's a simple footer pager, click the '>' button in left column if present
    const footerNext = page.locator('div[data-test="csm-available-header"]').locator('xpath=../../..').locator('button:has-text(">")')
    // Fallback to clicking the visible '>' in the left column footer
    await page.click('button:has-text(">")', { timeout: 5000 }).catch(() => {})

    // Wait for the available item on page 2 and add it
    const availPage2 = page.locator('[data-test="csm-available-item"]', { hasText: page2Scope }).first()
    await expect(availPage2).toBeVisible({ timeout: 10000 })
    await availPage2.locator('button').click()

    // Search filter (available)
    await page.fill('[data-test="csm-available-search"]', page2Scope)
    await expect(page.locator('[data-test="csm-available-item"]', { hasText: page2Scope })).toBeVisible()

    // Search filter (selected)
    await page.fill('[data-test="csm-selected-search"]', page2Scope)
    await expect(page.locator('[data-test="csm-selected-item"]', { hasText: page2Scope })).toBeVisible()

    // Submit and assert saved
    await page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/scopes`) && r.request().method() === 'PUT')
    await page.click('button[type="submit"]')

    const persisted = await page.evaluate(async (id) => {
      const r = await fetch(`/api/admin/clients/${id}/scopes`)
      return r.ok ? (await r.json()).scopes : []
    }, clientGuid)
    expect(persisted).toContain(page2Scope)
  })

})

export {}
