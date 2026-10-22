import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { queryConfigurationApi } from './queryConfigurationApi'

beforeEach(() => {
  document.head.innerHTML = '<meta name="csrf-token" content="synthetic-csrf">'
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ revision: 'r2' }) }))
})
afterEach(() => { vi.unstubAllGlobals(); document.head.innerHTML = '' })
it('uses protected uncached reads and CSRF revisioned writes', async () => {
  await queryConfigurationApi.get()
  await queryConfigurationApi.save({ expectedRevision: 'r1', configurations: [] })
  expect(fetch.mock.calls[0]).toEqual(['/api/admin/query-configuration', { method: 'GET', cache: 'no-store', credentials: 'include' }])
  expect(fetch.mock.calls[1][1]).toMatchObject({ method: 'PUT', credentials: 'include', headers: { 'X-XSRF-TOKEN': 'synthetic-csrf' }, body: JSON.stringify({ expectedRevision: 'r1', configurations: [] }) })
})
it('encodes approval identifiers and sends only the revision and approval choices', async () => {
  await queryConfigurationApi.approval('native-profile', 'example/source', 'account a')
  await queryConfigurationApi.saveApproval('native-profile', 'example/source', 'account a', { expectedRevision: 'a1', approved: true, nationalIdAlgorithmApproved: true })
  expect(fetch.mock.calls[0][0]).toBe('/api/admin/query-configuration/native-profile/example%2Fsource/users/account%20a/approval')
  expect(fetch.mock.calls[0][1].cache).toBe('no-store')
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ expectedRevision: 'a1', approved: true, nationalIdAlgorithmApproved: true })
  expect(fetch.mock.calls[1][1].headers['X-XSRF-TOKEN']).toBe('synthetic-csrf')
})
it.each([[401, 'session'], [403, 'forbidden'], [409, 'conflict'], [400, 'invalid_mapping']])('classifies %s responses', async (status, code) => {
  fetch.mockResolvedValue({ ok: false, status, json: async () => ({ error: 'invalid_mapping' }) })
  await expect(queryConfigurationApi.save({ expectedRevision: 'r1', configurations: [] })).rejects.toThrow(code)
})
it('does not treat a redirected sign-in page as configuration', async () => {
  fetch.mockResolvedValue({ ok: true, redirected: true, status: 200, json: async () => { throw new Error('html') } })
  await expect(queryConfigurationApi.get()).rejects.toThrow('session')
})
