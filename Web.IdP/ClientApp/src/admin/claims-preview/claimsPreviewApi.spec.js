import { beforeEach, describe, expect, it, vi } from 'vitest'
import { claimsPreviewApi as api } from './claimsPreviewApi'

beforeEach(() => {
  document.head.innerHTML = '<meta name="csrf-token" content="synthetic-csrf">'
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) }))
})
describe('Claims preview transport', () => {
  it('uses the established CSRF helper and sends only selected inputs', async () => {
    await api.preview({ clientId: 'example', userId: 'account', scopes: ['profile'] })
    expect(fetch).toHaveBeenCalledWith('/api/admin/claims-preview', expect.objectContaining({
      method: 'POST', credentials: 'include', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': 'synthetic-csrf' },
      body: JSON.stringify({ clientId: 'example', userId: 'account', scopes: ['profile'] })
    }))
  })
  it('encodes lookup inputs and bounds pages to 50', async () => {
    await api.accounts('a&b', 50)
    expect(fetch).toHaveBeenLastCalledWith('/api/admin/claims-preview/accounts?search=a%26b&skip=50&take=50', expect.objectContaining({ cache: 'no-store' }))
    await api.clients(100)
    expect(fetch).toHaveBeenLastCalledWith('/api/admin/claims-preview/clients?skip=100&take=50', expect.anything())
    await api.scopes('client&1')
    expect(fetch).toHaveBeenLastCalledWith('/api/admin/claims-preview/scopes?clientId=client%261', expect.anything())
  })
  it('handles permission, CSRF and expired-session errors without assuming JSON', async () => {
    fetch.mockResolvedValueOnce({ ok: false, status: 403, json: async () => ({}) })
    await expect(api.preview({})).rejects.toThrow('forbidden')
    fetch.mockResolvedValueOnce({ ok: false, status: 400, json: async () => { throw new Error('html') } })
    await expect(api.preview({})).rejects.toThrow('request_failed')
    fetch.mockResolvedValueOnce({ ok: true, redirected: true, status: 200, json: async () => { throw new Error('html') } })
    await expect(api.clients()).rejects.toThrow('session')
  })
})
