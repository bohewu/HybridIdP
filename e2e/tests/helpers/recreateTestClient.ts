import { Page } from '@playwright/test'

export async function recreateTestClientViaApi(page: Page, clientId = 'testclient-public') {
  // Ensure admin session
  await page.goto('https://localhost:7035/Account/Logout')
  await page.goto('https://localhost:7035/Account/Login')
  await page.fill('#Input_Login', 'admin@hybridauth.local')
  await page.fill('#Input_Password', 'Admin@123')
  await page.click('button.auth-btn-primary')
  await page.waitForSelector('.user-name', { timeout: 20000 })

  // Search for existing client
  const found = await page.evaluate(async (id) => {
    const r = await fetch(`/api/admin/clients?search=${encodeURIComponent(id)}&take=100`)
    if (!r.ok) return null
    const json = await r.json()
    const items = Array.isArray(json) ? json : (json.items || [])
    return items.find((c: any) => c.clientId === id) || null
  }, clientId)

  // Backup + delete if exists
  if (found && found.id) {
    try {
      // Save backup to a server-side place is not possible from browser; return the details to caller
      await page.evaluate(async (id) => {
        const r = await fetch(`/api/admin/clients/${id}`)
        if (!r.ok) return
        const details = await r.json()
        // attempt to send to console (tests may log this)
        // Note: we can't save files to disk easily from the browser context in tests
        console.log('backup-client-details', JSON.stringify(details))
      }, found.id)
      await page.evaluate(async (id) => {
        await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' })
      }, found.id)
    } catch (e) {
      // ignore
    }
  }

  // Create canonical client
  const createBody = {
    clientId: 'testclient-public',
    clientSecret: null,
    displayName: 'Test Client (Public)',
    applicationType: 'web',
    type: 'public',
    consentType: 'explicit',
    redirectUris: ['https://localhost:7001/signin-oidc'],
    postLogoutRedirectUris: ['https://localhost:7001/signout-callback-oidc'],
    permissions: [
      'ept:authorization','ept:token','ept:logout','gt:authorization_code','gt:refresh_token','response_type:code',
      'scp:openid','scp:profile','scp:email','scp:roles','scp:api:company:read','scp:api:inventory:read'
    ]
  }

  const created = await page.evaluate(async (body) => {
    const r = await fetch('/api/admin/clients', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    })
    if (!r.ok) {
      const text = await r.text().catch(() => '')
      throw new Error(`Failed to create test client: ${r.status} ${text}`)
    }
    return r.json()
  }, createBody)

  // Return created object for assertions
  return created
}

export default recreateTestClientViaApi
