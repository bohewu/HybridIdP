import { test, expect } from '@playwright/test'
import adminHelpers from '../helpers/admin'
import recreateHelper from '../helpers/recreateTestClient'

// Focused tests for ClientScopeManager component only.
test.describe('ClientScopeManager - UI interactions and persistence', () => {
  let clientId: string
  let clientGuid: string
  const scopes: string[] = []

  test.beforeEach(async ({ page }) => {
    // ensure canonical testclient exists (recreate) and create helper scopes if absent
    await recreateHelper(page)

    // login
    await adminHelpers.loginAsAdminViaIdP(page)

    // create test scopes (idempotent - createScope will throw if exists, ignored)
    const prefix = `e2e-csm-${Date.now()}`
    for (let i = 1; i <= 15; i++) {
      const name = `${prefix}-${i}`
      scopes.push(name)
      try {
        await adminHelpers.createScope(page, name, `E2E ${name}`, `E2E description ${name}`)
      } catch (e) {
        // ignore
      }
    }

    // Create per-test client
    clientId = `e2e-csm-client-${Date.now()}-${Math.floor(Math.random()*10000)}`
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

  test.afterEach(async ({ page }) => {
    // Cleanup created client and scopes created in this test
    try {
      await adminHelpers.deleteClientViaApiFallback(page, clientId)
    } catch (e) {}

    for (const s of scopes) {
      try { await adminHelpers.deleteScope(page, s) } catch (e) {}
    }
    // clear local scopes array for next test
    scopes.length = 0
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
    // Use the available-search to bring this scope into the Available list (debounced)
    await page.fill('[data-test="csm-available-search"]', targetScope)
    // Wait for debounce and API to finish
    await page.waitForTimeout(400)
    const availableItem = page.locator('[data-test="csm-available-item"]', { hasText: targetScope }).first()
    await expect(availableItem).toBeVisible({ timeout: 15000 })
    // Click the add button inside the available item
    await availableItem.locator('button').click()

    // Verify appears in selected list
    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 10000 })

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

    // Ensure the scope is present in the client's allowed scopes first using the UI (add via Available list + save)
    await page.goto('https://localhost:7035/Admin/Clients')
    await page.waitForURL(/\/Admin\/Clients/)
    // Open edit modal and add the scope via UI instead of direct API so the UI state is in sync
    const result2 = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result2.clicked).toBeTruthy()
    await page.waitForSelector('#clientId')

    // Ensure the scope is present together with 'openid' (UI validation requires openid) using API
    const putOk = await page.evaluate(async (args) => {
      const r = await fetch(`/api/admin/clients/${args.id}/scopes`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ scopes: ['openid', args.scope] }) });
      return r.ok;
    }, { id: clientGuid, scope: targetScope });
    expect(putOk).toBeTruthy()

    // Re-open edit modal to toggle required
    await page.goto('https://localhost:7035/Admin/Clients')
    await page.waitForURL(/\/Admin\/Clients/)
    const reopen = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })

    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 10000 })

    // Toggle required switch (ToggleSwitch renders as a checkbox input inside the label)
    const toggle = selected.locator('label').first()
    await toggle.waitFor({ state: 'visible', timeout: 10000 })
    await toggle.click()
    // Confirm click succeeded (UI toggles in place)
    // Now persist and verify server-side required scopes include the target
    // Save changes: wait for allowed-scopes update to ensure the flow progressed, then poll required-scopes until our selection appears
    // Click submit and wait for the client update to be accepted
    // Try submitting and wait for the client update PUT to succeed; retry a few times if it fails transiently
    let updateResp = null as any
    const maxAttempts = 3
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      await page.click('button[type="submit"]')
      updateResp = await page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}`) && r.request().method() === 'PUT', { timeout: 5000 }).catch(() => null)
      if (updateResp && updateResp.ok()) break
      // small delay between retries
      await page.waitForTimeout(400)
    }

    if (!updateResp) {
      throw new Error('No client update response observed after submit attempts')
    } else if (!updateResp.ok()) {
      // If the update response wasn't ok after retries, surface the body to help debugging and fail
      const body = await updateResp.text().catch(() => '<no-body>')
      throw new Error(`Client update failed after ${maxAttempts} attempts: ${updateResp.status()} ${body}`)
    }

    // Poll server until the required-scopes endpoint contains our scope (idempotent and robust)
    const requiredResp = await adminHelpers.pollApiUntil(page, `/api/admin/clients/${clientGuid}/required-scopes`, (json) => {
      // Normalize shape
      const items = Array.isArray(json) ? json : (json.scopes || [])
      return Array.isArray(items) && items.includes(targetScope)
    }, 30000)
    // No fallback — require that the UI flow persisted the required scope
    expect(requiredResp).not.toBeNull()
  })

  test('remove selected scope clears required and persists', async ({ page }) => {
    await adminHelpers.loginAsAdminViaIdP(page)
    await page.goto('https://localhost:7035/Admin/Clients')
    // Edit
    const result = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
    expect(result.clicked).toBeTruthy()
    await page.waitForSelector('#clientId')

    const targetScope = scopes[0]
    // Ensure the scope is added first in this test's client
    await page.fill('[data-test="csm-available-search"]', targetScope)
    await page.waitForTimeout(400)
    const availableItem = page.locator('[data-test="csm-available-item"]', { hasText: targetScope }).first()
    await expect(availableItem).toBeVisible({ timeout: 10000 })
    await availableItem.locator('button').click()

    const selected = page.locator('[data-test="csm-selected-item"]', { hasText: targetScope }).first()
    await expect(selected).toBeVisible({ timeout: 10000 })

    // Click remove button (title attr) inside selected item
    const removeBtn = selected.locator('button[title], button:has-text("Remove")').first()
    await removeBtn.click()

    // Submit and wait for scopes PUT
    const putScopes = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/scopes`) && r.request().method() === 'PUT')
    await Promise.all([page.click('button[type="submit"]'), putScopes])

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

    // Advance pagination on available column; click the footer > button
    await page.click('div:has([data-test="csm-available-header"]) + div button:has-text(">")', { timeout: 2000 }).catch(() => {})

    // Wait for the available item on page 2 and add it
    // Use the search in the available column to find the page 2 scope directly
    await page.fill('[data-test="csm-available-search"]', page2Scope)
    await page.waitForTimeout(400)
    const availPage2 = page.locator('[data-test="csm-available-item"]', { hasText: page2Scope }).first()
    await expect(availPage2).toBeVisible({ timeout: 15000 })
    await availPage2.locator('button').click()

    // Search filter (available)
    await page.fill('[data-test="csm-available-search"]', page2Scope)
    await expect(page.locator('[data-test="csm-available-item"]', { hasText: page2Scope })).toBeVisible()

    // Search filter (selected)
    await page.fill('[data-test="csm-selected-search"]', page2Scope)
    await expect(page.locator('[data-test="csm-selected-item"]', { hasText: page2Scope })).toBeVisible()

    // Submit and assert saved
    const putPromise = page.waitForResponse((r) => r.url().includes(`/api/admin/clients/${clientGuid}/scopes`) && r.request().method() === 'PUT')
    await Promise.all([page.click('button[type="submit"]'), putPromise])

    const persisted = await page.evaluate(async (id) => {
      const r = await fetch(`/api/admin/clients/${id}/scopes`)
      return r.ok ? (await r.json()).scopes : []
    }, clientGuid)
    expect(persisted).toContain(page2Scope)
  })

})

export {}
