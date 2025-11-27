import { Page } from '@playwright/test'
import { ensureAdminAvailable, loginAsAdminViaIdP } from './admin'

export async function recreateTestClientViaApi(page: Page, clientId = 'testclient-public') {
  // Ensure admin is reachable and that we are logged in
  await ensureAdminAvailable(page)
  await loginAsAdminViaIdP(page)

  // Search for existing client
  const found = await page.evaluate(async (id) => {
    const r = await fetch(`/api/admin/clients?search=${encodeURIComponent(id)}&take=100`)
    if (!r.ok) return null
    const json = await r.json()
    const items = Array.isArray(json) ? json : (json.items || [])
    return items.find((c: any) => c.clientId === id) || null
  }, clientId)

  // Create canonical client payload
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

  // If exists, attempt to update the existing client to match canonical test client
  if (found && found.id) {
    try {
      const updateBody = { ...createBody }
      // Ensure clientId matches existing; update endpoint uses id path
      const updated = await page.evaluate(async (args) => {
        const { id, body } = args
        const r = await fetch(`/api/admin/clients/${id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        })
        if (!r.ok) {
          const text = await r.text().catch(() => '')
          throw new Error(`Failed to update test client: ${r.status} ${text}`)
        }
        return r.json()
      }, { id: found.id, body: updateBody })

      return updated
    } catch (e) {
      // If update failed, attempt delete+create as a fallback
      try {
        await page.evaluate(async (id) => {
          await fetch(`/api/admin/clients/${id}`, { method: 'DELETE' })
        }, found.id)
      } catch (err) {
        // continue to create below and let create handle duplicate errors
      }
    }
  }

  // Create canonical client (createBody declared above)

  const created = await page.evaluate(async (body) => {
    // Attempt to create; if a duplicate exists due to race we handle by fetching the existing client
    const r = await fetch('/api/admin/clients', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    })
    if (!r.ok) {
      const text = await r.text().catch(() => '')
      // If server indicates the client already exists (409 or message contains 'already exists' / 'duplicate'), try to return the existing client
      if (r.status === 409 || /already exists|duplicate key|IX_OpenIddictApplications_ClientId/i.test(text)) {
        const s = await fetch(`/api/admin/clients?search=${encodeURIComponent(body.clientId)}&take=100`)
        if (s.ok) {
          const json = await s.json()
          const items = Array.isArray(json) ? json : (json.items || [])
          return items.find((c: any) => c.clientId === body.clientId) || null
        }
      }
      throw new Error(`Failed to create test client: ${r.status} ${text}`)
    }
    return r.json()
  }, createBody)

  // Return created object for assertions
  return created
}

export default recreateTestClientViaApi
