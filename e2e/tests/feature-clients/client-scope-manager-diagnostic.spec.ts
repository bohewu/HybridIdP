import { test, expect } from '@playwright/test'
import adminHelpers from '../helpers/admin'
import recreateHelper from '../helpers/recreateTestClient'

// Diagnostic test that captures network traffic to investigate required-scopes behavior
test('Diagnostic - capture network when toggling required scope', async ({ page }) => {
  // Ensure testclient exists
  await recreateHelper(page)
  await adminHelpers.loginAsAdminViaIdP(page)

  // Create a client
  const clientId = `diag-client-${Date.now()}-${Math.floor(Math.random()*1000)}`
  const created = await page.evaluate(async (id) => {
    const payload = {
      clientId: id,
      displayName: id,
      applicationType: 'web',
      type: 'public',
      consentType: 'explicit',
      clientSecret: null,
      redirectUris: ['https://localhost:7001/signin-oidc'],
      postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
      permissions: ['ept:authorization','ept:token','gt:authorization_code','scp:openid']
    }
    const r = await fetch('/api/admin/clients', { method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(payload) })
    return r.json()
  }, clientId)
  const clientGuid = created.id

  // Create a scope to use for this test
  const scopeName = `diag-scope-${Date.now()}`
  await adminHelpers.createScope(page, scopeName, scopeName, 'diagnostic')

  // Track responses we see while doing the UI flow
  const seenResponses: Array<{url:string, status:number, method:string, ok:boolean}> = []
  page.on('response', (r) => {
    const url = r.url();
    if (url.includes('/api/admin/clients')) {
      seenResponses.push({ url, status: r.status(), method: r.request().method(), ok: r.ok() })
    }
  })

  // Open admin clients and edit the created client
  await page.goto('https://localhost:7035/Admin/Clients')
  await page.waitForURL(/\/Admin\/Clients/)

  const res = await adminHelpers.searchAndClickAction(page, 'clients', clientId, 'Edit', { listSelector: 'ul[role="list"], table tbody' })
  expect(res.clicked).toBeTruthy()

  await page.waitForSelector('#clientId')

  // Add the scope via available search
  await page.fill('[data-test="csm-available-search"]', scopeName)
  await page.waitForTimeout(500)
  const avail = page.locator('[data-test="csm-available-item"]', { hasText: scopeName }).first()
  await expect(avail).toBeVisible({ timeout: 10000 })
  await avail.locator('button').click()

  // Toggle required for it
  const selected = page.locator('[data-test="csm-selected-item"]', { hasText: scopeName }).first()
  await expect(selected).toBeVisible({ timeout: 10000 })
  const toggle = selected.locator('button').first()
  await toggle.click()

  // Submit and wait a bit — capture responses
  await page.click('button[type="submit"]')
  await page.waitForTimeout(2000)

  // Now inspect the captured responses
  const reqScopes = seenResponses.filter(r => r.url.includes('/required-scopes') || r.url.includes('/scopes'))

  // Assert that required-scopes PUT occurred or else we will fail and print details
  const reqPut = reqScopes.find(r => r.url.includes('/required-scopes') && r.method === 'PUT')
  expect(reqPut, 'expected a PUT request to /required-scopes').not.toBeUndefined()

  // Cleanup
  await adminHelpers.deleteClientViaApiFallback(page, clientId)
  await adminHelpers.deleteScope(page, scopeName)
})
