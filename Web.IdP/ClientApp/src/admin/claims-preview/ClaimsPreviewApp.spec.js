import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import ClaimsPreviewApp from './ClaimsPreviewApp.vue'
import { claimsPreviewApi as api } from './claimsPreviewApi'
import en from '@/i18n/locales/en-US/claims-preview.json'
import zh from '@/i18n/locales/zh-TW/claims-preview.json'

vi.mock('./claimsPreviewApi', () => ({ claimsPreviewApi: {
  clients: vi.fn(), accounts: vi.fn(), scopes: vi.fn(), preview: vi.fn()
} }))

const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
const payload = () => ({ idToken: { sub: 'account-1', enabled: false, categories: ['staff', 'reader'], empty: [] },
  accessToken: { sub: 'account-1', scope: 'openid' }, userInfo: { sub: 'account-1', enabled: false },
  reasons: [
    { claimType: 'enabled', destination: 'idToken', status: 'included', code: 'included' },
    { claimType: 'amr', destination: 'idToken', status: 'unknown', code: 'authentication_not_performed' },
    { claimType: 'name', destination: 'idToken', status: 'omitted', code: 'scope_not_selected' },
    { claimType: 'jti', destination: 'accessToken', status: 'unknown', code: 'issuance_not_performed' }
  ], sources: [{ source: 'example', status: 'expired', confirmedAt: '2026-10-18T00:00:00Z', expiresAt: '2026-10-19T00:00:00Z' }] })
function render(locale = 'en-US') {
  return mount(ClaimsPreviewApp, { global: { plugins: [createI18n({ legacy: false, locale,
    messages: { 'en-US': { 'claims-preview': en }, 'zh-TW': { 'claims-preview': zh } } })] } })
}
async function select(wrapper) {
  await flushPromises()
  await wrapper.get('#preview-client').setValue('client-1')
  await flushPromises()
  await wrapper.get('#preview-account').setValue('account-1')
  await wrapper.get('input[type="checkbox"][value="openid"]').setValue(true)
}
beforeEach(() => {
  vi.resetAllMocks()
  api.clients.mockResolvedValue({ items: [{ clientId: 'client-1', displayName: 'Example client', supported: true }, { clientId: 'm2m', displayName: 'Machine client', supported: false }] })
  api.accounts.mockResolvedValue({ items: [{ userId: 'account-1', displayName: 'Example account' }] })
  api.scopes.mockImplementation(id => Promise.resolve({ clientId: id, supported: id !== 'm2m', scopes: [{ name: 'openid', usable: true }, { name: 'profile', usable: true }, { name: 'restricted', usable: false }] }))
  api.preview.mockResolvedValue(payload())
})

describe('Claims preview', () => {
  it.each(['en-US', 'zh-TW'])('renders typed JSON, destination reasons and cached source freshness in %s', async locale => {
    const wrapper = render(locale)
    await select(wrapper)
    expect(wrapper.get('input[value="restricted"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await flushPromises()
    expect(api.preview).toHaveBeenCalledWith({ clientId: 'client-1', userId: 'account-1', scopes: ['openid'] })
    const json = JSON.parse(wrapper.get('pre').text())
    expect(json.enabled).toBe(false)
    expect(json.categories).toEqual(['staff', 'reader'])
    expect(json.empty).toEqual([])
    expect(json).not.toHaveProperty('missing')
    expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).sourceStatuses.expired)
    expect(wrapper.text()).toContain('2026-10-18T00:00:00Z')
    expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).codes.authentication_not_performed)
    await wrapper.get('#tab-accessToken').trigger('click')
    expect(JSON.parse(wrapper.get('pre').text())).toEqual(payload().accessToken)
    expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).codes.issuance_not_performed)
    await wrapper.get('#tab-userInfo').trigger('click')
    expect(JSON.parse(wrapper.get('pre').text())).toEqual(payload().userInfo)
    wrapper.unmount()
  })

  it('clears results on scope changes and ignores a late preview response', async () => {
    const wrapper = render()
    await select(wrapper)
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(true)
    await wrapper.get('input[value="profile"]').setValue(true)
    expect(wrapper.find('pre').exists()).toBe(false)
    const old = deferred()
    api.preview.mockReturnValueOnce(old.promise)
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await wrapper.get('input[value="openid"]').setValue(false)
    old.resolve(payload())
    await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await flushPromises()
    expect(api.preview).toHaveBeenLastCalledWith({ clientId: 'client-1', userId: 'account-1', scopes: ['profile'] })
    wrapper.unmount()
  })

  it('invalidates preview on account search changes and ignores outdated account and scope responses', async () => {
    const wrapper = render()
    await select(wrapper)
    const oldAccounts = deferred(), oldScopes = deferred()
    api.accounts.mockReturnValueOnce(oldAccounts.promise)
    await wrapper.get('form').trigger('submit')
    await wrapper.get('#preview-search').setValue('new')
    oldAccounts.resolve({ items: [{ userId: 'old', displayName: 'Old account' }] })
    await flushPromises()
    expect(wrapper.get('#preview-account').text()).not.toContain('Old account')
    expect(wrapper.get('#preview-account').element.value).toBe('')
    api.scopes.mockReturnValueOnce(oldScopes.promise)
    await wrapper.get('#preview-client').setValue('m2m')
    await wrapper.get('#preview-client').setValue('client-1')
    await flushPromises()
    oldScopes.resolve({ supported: false, scopes: [{ name: 'old-scope', usable: true }] })
    await flushPromises()
    expect(wrapper.find('input[value="old-scope"]').exists()).toBe(false)
    expect(wrapper.find('input[value="profile"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('shows unsupported M2M and handles denied preview without keeping old payload', async () => {
    const wrapper = render()
    await select(wrapper)
    await wrapper.get('#preview-client').setValue('m2m')
    await flushPromises()
    expect(wrapper.text()).toContain(en.errors.unsupported_client)
    expect(wrapper.get('[data-test-id="claims-preview-run"]').attributes('disabled')).toBeDefined()
    await wrapper.get('#preview-client').setValue('client-1')
    await flushPromises()
    api.preview.mockRejectedValueOnce(new Error('forbidden'))
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(en.errors.forbidden)
    expect(wrapper.find('pre').exists()).toBe(false)
    wrapper.unmount()
  })

  it('pages clients and accounts in bounded requests', async () => {
    api.clients.mockResolvedValue({ items: Array.from({ length: 50 }, (_, i) => ({ clientId: `c${i}`, displayName: `Client ${i}`, supported: true })) })
    api.accounts.mockResolvedValue({ items: Array.from({ length: 50 }, (_, i) => ({ userId: `u${i}`, displayName: `Account ${i}` })) })
    const wrapper = render()
    await flushPromises()
    const next = wrapper.findAll('button').filter(button => button.text() === en.next)
    await next[0].trigger('click')
    await next[1].trigger('click')
    await flushPromises()
    expect(api.clients).toHaveBeenLastCalledWith(50)
    expect(api.accounts).toHaveBeenLastCalledWith('', 50)
    wrapper.unmount()
  })
})
