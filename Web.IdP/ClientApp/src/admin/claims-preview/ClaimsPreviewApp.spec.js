import { mount, flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import ClaimsPreviewApp from './ClaimsPreviewApp.vue'
import PreviewEntityPicker from './PreviewEntityPicker.vue'
import { claimsPreviewApi as api } from './claimsPreviewApi'
import en from '@/i18n/locales/en-US/claims-preview.json'
import zh from '@/i18n/locales/zh-TW/claims-preview.json'

vi.mock('./claimsPreviewApi', () => ({ claimsPreviewApi: {
  clients: vi.fn(), accounts: vi.fn(), scopes: vi.fn(), preview: vi.fn()
} }))
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
const payload = () => ({ clientId: 'client-1', userId: 'account-1', scopes: ['openid'],
  idToken: { sub: 'account-1', enabled: false, categories: ['staff', 'reader'], empty: [] },
  accessToken: { sub: 'account-1', scope: 'openid' }, userInfo: { sub: 'account-1', enabled: false },
  reasons: [
    { claimType: 'enabled', destination: 'idToken', status: 'included', code: 'included' },
    { claimType: 'amr', destination: 'idToken', status: 'unknown', code: 'authentication_not_performed' },
    { claimType: 'name', destination: 'idToken', status: 'omitted', code: 'scope_not_selected' },
    { claimType: 'jti', destination: 'accessToken', status: 'unknown', code: 'issuance_not_performed' }
  ], sources: [{ source: 'example', status: 'confirmed', confirmedAt: '2026-10-19T00:00:00Z', expiresAt: null }] })
let wrapper
function render(locale = 'en-US') {
  wrapper = mount(ClaimsPreviewApp, { attachTo: document.body, global: { plugins: [createI18n({ legacy: false, locale,
    messages: { 'en-US': { 'claims-preview': en }, 'zh-TW': { 'claims-preview': zh } } })] } })
  return wrapper
}
async function choose(id, value) {
  await wrapper.get(`#${id}-trigger`).trigger('click')
  const picker = wrapper.findAllComponents(PreviewEntityPicker).find(component => component.props('id') === id)
  const index = picker.props('items').findIndex(item => item.value === value)
  expect(index).toBeGreaterThanOrEqual(0)
  await picker.findAll('[role="option"]')[index].trigger('click')
  await flushPromises()
}
async function select() {
  await flushPromises()
  await choose('preview-client', 'client-1')
  await choose('preview-account', 'account-1')
  await wrapper.get('input[type="checkbox"][value="openid"]').setValue(true)
}
async function run() {
  await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
  await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  api.clients.mockResolvedValue({ items: [{ clientId: 'client-1', displayName: 'Example client', supported: true }, { clientId: 'm2m', displayName: 'Machine client', supported: false }] })
  api.accounts.mockResolvedValue({ items: [{ userId: 'account-1', displayName: 'Example account' }] })
  api.scopes.mockImplementation(id => Promise.resolve({ clientId: id, supported: id !== 'm2m', scopes: [{ name: 'openid', usable: true }, { name: 'profile', usable: true }, { name: 'restricted', usable: false }] }))
  api.preview.mockResolvedValue(payload())
})
afterEach(() => { wrapper?.unmount(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('Claims preview', () => {
  it.each(['en-US', 'zh-TW'])('preserves typed payloads, live source time and localized decisions in %s', async locale => {
    render(locale)
    await select()
    expect(wrapper.get('input[value="restricted"]').attributes('disabled')).toBeDefined()
    await run()
    expect(document.activeElement).toBe(wrapper.get('[data-test-id="claims-preview-result"]').element)
    expect(api.preview).toHaveBeenCalledWith({ clientId: 'client-1', userId: 'account-1', scopes: ['openid'] })
    const json = JSON.parse(wrapper.get('pre').text())
    expect(json.enabled).toBe(false)
    expect(json.categories).toEqual(['staff', 'reader'])
    expect(json.empty).toEqual([])
    expect(json).not.toHaveProperty('missing')
    const text = locale === 'en-US' ? en : zh
    expect(wrapper.text()).toContain(text.sourceStatuses.confirmed)
    expect(wrapper.text()).toContain(text.sourcesHelp)
    expect(wrapper.get('time').attributes('datetime')).toBe('2026-10-19T00:00:00Z')
    expect(wrapper.get('dl').text()).not.toContain(text.expiresAt)
    expect(wrapper.text()).toContain(text.codes.authentication_not_performed)
    await wrapper.get('#tab-accessToken').trigger('click')
    expect(JSON.parse(wrapper.get('pre').text())).toEqual(payload().accessToken)
    expect(wrapper.text()).toContain(text.codes.issuance_not_performed)
    await wrapper.get('#tab-userInfo').trigger('click')
    expect(JSON.parse(wrapper.get('pre').text())).toEqual(payload().userInfo)
  })

  it('clears changed selections and ignores a late preview response', async () => {
    render(); await select(); await run()
    await wrapper.get('input[value="profile"]').setValue(true)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.text()).toContain(en.changedTitle)
    const old = deferred()
    api.preview.mockReturnValueOnce(old.promise)
    await wrapper.get('[data-test-id="claims-preview-run"]').trigger('click')
    await wrapper.get('input[value="openid"]').setValue(false)
    old.resolve(payload()); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    await run()
    expect(api.preview).toHaveBeenLastCalledWith({ clientId: 'client-1', userId: 'account-1', scopes: ['profile'] })
  })

  it('keeps the selected account and payload while browsing, and ignores outdated lookup responses', async () => {
    render(); await select(); await run()
    const old = deferred()
    api.accounts.mockReturnValueOnce(old.promise)
    await wrapper.get('#preview-account-trigger').trigger('click')
    await wrapper.get('#preview-account-search').setValue('old')
    await vi.advanceTimersByTimeAsync(250)
    expect(api.accounts).toHaveBeenLastCalledWith('old', 0)
    await wrapper.get('#preview-account-search').setValue('new')
    old.resolve({ items: [{ userId: 'old', displayName: 'Old account' }] }); await flushPromises()
    expect(wrapper.findAll('[role="option"]').map(option => option.text()).join()).not.toContain('Old account')
    expect(wrapper.get('#preview-account-trigger').text()).toContain('Example account')
    expect(wrapper.find('pre').exists()).toBe(true)
    await vi.advanceTimersByTimeAsync(250); await flushPromises()
    expect(api.accounts).toHaveBeenLastCalledWith('new', 0)
    await wrapper.get('#preview-account-search').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.get('#preview-account-trigger').text()).toContain('Example account')
  })

  it('ignores outdated scope responses after changing clients', async () => {
    render(); await select()
    const old = deferred(); api.scopes.mockReturnValueOnce(old.promise)
    await choose('preview-client', 'm2m')
    await choose('preview-client', 'client-1')
    old.resolve({ supported: false, scopes: [{ name: 'old-scope', usable: true }] }); await flushPromises()
    expect(wrapper.find('input[value="old-scope"]').exists()).toBe(false)
    expect(wrapper.find('input[value="profile"]').exists()).toBe(true)
  })

  it('keeps unsupported machine clients disabled and removes failed payloads', async () => {
    render(); await select(); await choose('preview-client', 'm2m')
    expect(wrapper.text()).toContain(en.errors.unsupported_client)
    expect(wrapper.get('[data-test-id="claims-preview-run"]').attributes('disabled')).toBeDefined()
    await choose('preview-client', 'client-1')
    api.preview.mockRejectedValueOnce(new Error('forbidden'))
    await run()
    expect(wrapper.get('[role="alert"]').text()).toBe(en.errors.forbidden)
    expect(wrapper.find('pre').exists()).toBe(false)
  })

  it('keeps selected identities across bounded client/account pages', async () => {
    api.clients.mockResolvedValue({ items: Array.from({ length: 50 }, (_, i) => ({ clientId: i ? `c${i}` : 'client-1', displayName: `Client ${i}`, supported: true })) })
    api.accounts.mockResolvedValue({ items: Array.from({ length: 50 }, (_, i) => ({ userId: i ? `u${i}` : 'account-1', displayName: `Account ${i}` })) })
    render(); await select(); await run()
    await wrapper.get('#preview-client-trigger').trigger('click')
    wrapper.get('[aria-label="Next Client page"]').element.focus()
    await wrapper.get('[aria-label="Next Client page"]').trigger('click'); await flushPromises()
    expect(api.clients).toHaveBeenLastCalledWith('', 50)
    expect(document.activeElement).toBe(wrapper.get('#preview-client-search').element)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(wrapper.get('#preview-client-trigger').text()).toContain('Client 0')
    await wrapper.get('#preview-account-trigger').trigger('click')
    await wrapper.get('[aria-label="Next Account page"]').trigger('click'); await flushPromises()
    expect(api.accounts).toHaveBeenLastCalledWith('', 50)
    expect(wrapper.get('#preview-account-trigger').text()).toContain('Account 0')
    expect(wrapper.find('pre').exists()).toBe(true)
  })

  it('filters reasons, supports roving keyboard tabs and copies only the selected JSON payload', async () => {
    const writeText = vi.fn().mockResolvedValue()
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    render(); await select(); await run()
    await wrapper.get('[aria-pressed="false"]').trigger('click')
    expect(wrapper.get('ul[aria-label="Claim decisions"]').text()).toContain('enabled')
    expect(wrapper.get('ul[aria-label="Claim decisions"]').text()).not.toContain('amr')
    await wrapper.get('input[aria-label="Find a claim or reason"]').setValue('not-present')
    expect(wrapper.text()).toContain(en.noMatchingReasons)
    await wrapper.get('#tab-idToken').trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.get('#tab-accessToken').attributes('aria-selected')).toBe('true')
    expect(wrapper.get('#tab-accessToken').attributes('tabindex')).toBe('0')
    expect(wrapper.get('#tab-idToken').attributes('tabindex')).toBe('-1')
    expect(wrapper.get('input[aria-label="Find a claim or reason"]').element.value).toBe('')
    await wrapper.findAll('button').find(button => button.text() === en.copyJson).trigger('click'); await flushPromises()
    expect(JSON.parse(writeText.mock.calls[0][0])).toEqual(payload().accessToken)
    expect(wrapper.text()).toContain(en.copied)
  })

  it('allows keyboard selection, handles lookup failure and explicitly selects only usable scopes', async () => {
    render(); await flushPromises()
    await wrapper.get('#preview-client-trigger').trigger('keydown', { key: 'ArrowDown' })
    await wrapper.get('#preview-client-search').trigger('keydown', { key: 'Enter' }); await flushPromises()
    expect(wrapper.get('#preview-client-trigger').text()).toContain('Example client')
    await wrapper.findAll('button').find(button => button.text() === en.selectUsable).trigger('click')
    expect(wrapper.get('input[value="openid"]').element.checked).toBe(true)
    expect(wrapper.get('input[value="profile"]').element.checked).toBe(true)
    expect(wrapper.get('input[value="restricted"]').element.checked).toBe(false)
    api.accounts.mockRejectedValueOnce(new Error('request_failed'))
    await wrapper.get('#preview-account-trigger').trigger('click')
    await wrapper.get('#preview-account-search').setValue('missing')
    await vi.advanceTimersByTimeAsync(250); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain(en.errors.request_failed)
    wrapper.findAll('button').find(button => button.text() === en.lookup.retry).element.focus()
    await wrapper.findAll('button').find(button => button.text() === en.lookup.retry).trigger('click'); await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(document.activeElement).toBe(wrapper.get('#preview-account-search').element)
  })

  it('keeps JSON available and explains a denied clipboard write', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } })
    render(); await select(); await run()
    await wrapper.findAll('button').find(button => button.text() === en.copyJson).trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain(en.copyFailed)
    expect(JSON.parse(wrapper.get('pre').text())).toEqual(payload().idToken)
  })
})
