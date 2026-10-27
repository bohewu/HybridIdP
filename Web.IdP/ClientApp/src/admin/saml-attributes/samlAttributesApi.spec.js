import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { samlAttributesApi as api } from './samlAttributesApi'
beforeEach(() => {
  document.head.innerHTML = '<meta name="csrf-token" content="synthetic-csrf">'
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) }))
})
afterEach(() => vi.unstubAllGlobals())
describe('SAML attribute transport', () => {
  it('uses cookie CSRF transport for policy replacement and saved-policy preview', async () => {
    const policy = { entityId: 'app', mappings: [{ source: 'Email', name: 'email' }] }
    await api.save(policy)
    expect(fetch).toHaveBeenLastCalledWith('/api/admin/saml-attributes/policy', expect.objectContaining({ method: 'PUT', credentials: 'include', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': 'synthetic-csrf' }, body: JSON.stringify(policy) }))
    await api.preview({ entityId: 'app', userId: 'user' })
    expect(fetch).toHaveBeenLastCalledWith('/api/admin/saml-attributes/preview', expect.objectContaining({ method: 'POST', body: JSON.stringify({ entityId: 'app', userId: 'user' }) }))
  })
  it('encodes partner identity and bounds account pages', async () => {
    await api.policy('https://example/sp?a&b')
    expect(fetch.mock.lastCall[0]).toBe('/api/admin/saml-attributes/policy?entityId=https%3A%2F%2Fexample%2Fsp%3Fa%26b')
    await api.accounts('a&b', 50)
    expect(fetch.mock.lastCall[0]).toBe('/api/admin/saml-attributes/accounts?search=a%26b&skip=50&take=50')
  })
  it('handles typed errors and non-JSON denied or redirected responses', async () => {
    for (const [response, code] of [
      [{ ok: false, status: 400, json: async () => ({ code: 'duplicate_name' }) }, 'duplicate_name'],
      [{ ok: false, status: 403, json: async () => { throw Error('html') } }, 'forbidden'],
      [{ ok: true, redirected: true, json: async () => ({}) }, 'session']
    ]) { fetch.mockResolvedValueOnce(response); await expect(api.partners()).rejects.toThrow(code) }
  })
})
