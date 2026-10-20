import { afterEach, expect, it, vi } from 'vitest'
import { profileSyncApi } from './profileSyncApi'
afterEach(() => { vi.unstubAllGlobals(); document.head.innerHTML = '' })
it('uses cookie CSRF and sends only the preview token when applying', async () => {
  document.head.innerHTML = '<meta name="csrf-token" content="synthetic-csrf">'
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ status: 'applied' }) }))
  await profileSyncApi.preview('example account')
  await profileSyncApi.apply('example account', 'synthetic-token')
  expect(fetch.mock.calls[0][0]).toBe('/api/admin/profile-sync/users/example%20account/preview')
  expect(fetch.mock.calls[0][1].body).toBeUndefined()
  expect(fetch.mock.calls[1][0]).toBe('/api/admin/profile-sync/users/example%20account/apply')
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ previewToken: 'synthetic-token' })
  expect(fetch.mock.calls[1][1].headers['X-XSRF-TOKEN']).toBe('synthetic-csrf')
  expect(fetch.mock.calls[1][1].credentials).toBe('include')
})
it('propagates stale preview errors for mandatory re-preview', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 409, json: async () => ({ error: 'preview_stale' }) }))
  await expect(profileSyncApi.apply('user-a', 'old-token')).rejects.toThrow('preview_stale')
})
