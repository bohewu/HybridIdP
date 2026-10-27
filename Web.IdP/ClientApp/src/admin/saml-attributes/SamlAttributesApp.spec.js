import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import App from './SamlAttributesApp.vue'
import Picker from '../claims-preview/PreviewEntityPicker.vue'
import { samlAttributesApi as api } from './samlAttributesApi'
import permissions, { Permissions } from '@/utils/permissionService'
import en from '@/i18n/locales/en-US/saml-attributes.json'
import zh from '@/i18n/locales/zh-TW/saml-attributes.json'
import pickerEn from '@/i18n/locales/en-US/claims-preview.json'
import pickerZh from '@/i18n/locales/zh-TW/claims-preview.json'
vi.mock('./samlAttributesApi', () => ({ samlAttributesApi: { partners: vi.fn(), catalog: vi.fn(), policy: vi.fn(), save: vi.fn(), accounts: vi.fn(), preview: vi.fn() } }))
let wrapper, policies
async function render(locale = 'en-US') {
  wrapper = mount(App, { global: { plugins: [createI18n({ legacy: false, locale, messages: {
    'en-US': { 'saml-attributes': en, 'claims-preview': pickerEn }, 'zh-TW': { 'saml-attributes': zh, 'claims-preview': pickerZh }
  } })] } }); await flushPromises()
}
async function partner(id = 'a') { await wrapper.get('#saml-partner').setValue(id); await flushPromises() }
async function preview() {
  wrapper.getComponent(Picker).vm.$emit('select', { value: 'user', label: 'Example account' }); await flushPromises()
  await wrapper.get('[data-test="preview"]').trigger('click'); await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.spyOn(permissions, 'reloadPermissions').mockResolvedValue(true)
  vi.spyOn(permissions, 'hasPermission').mockReturnValue(true)
  policies = { a: [], b: [{ source: 'Person.Email', name: 'contact' }] }
  api.partners.mockResolvedValue({ enabled: true, items: [{ entityId: 'a' }, { entityId: 'b' }] })
  api.catalog.mockResolvedValue({ items: [{ source: 'FirstName', defaultName: 'FirstName' }, { source: 'Person.Email', defaultName: 'Person.Email' }] })
  api.policy.mockImplementation(async entityId => ({ entityId, mappings: policies[entityId].map(item => ({ ...item })) }))
  api.save.mockImplementation(async policy => { policies[policy.entityId] = policy.mappings; return policy })
  api.accounts.mockResolvedValue({ items: [{ userId: 'user', displayName: 'Example account' }] })
  api.preview.mockResolvedValue({ entityId: 'a', nameId: 'fixed-subject', attributes: [{ name: 'urn:hybrididp:claims:subject', value: 'fixed-subject' }], omitted: [{ source: 'Person.Email', name: 'contact', reason: 'missing_value' }] })
})
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks() })
describe('SAML attribute administration', () => {
  it('saves, reopens and isolates application selections and discards unsaved switches', async () => {
    await render(); await partner(); expect(wrapper.text()).toContain(en.noAttributes)
    await wrapper.get('[data-test="add"]').trigger('click'); await wrapper.get('#name-0').setValue('given_name')
    await wrapper.get('[data-test="save"]').trigger('click'); await flushPromises()
    expect(api.save).toHaveBeenCalledWith({ entityId: 'a', mappings: [{ source: 'FirstName', name: 'given_name' }] })
    await partner('b'); expect(wrapper.get('#name-0').element.value).toBe('contact')
    await wrapper.get('#name-0').setValue('unsaved'); await partner()
    expect(wrapper.get('#name-0').element.value).toBe('given_name')
    await partner('b'); expect(wrapper.get('#name-0').element.value).toBe('contact')
  })
  it('previews saved policy only, clears prior results and blocks dirty preview', async () => {
    await render(); await partner(); await preview()
    expect(api.preview).toHaveBeenCalledWith({ entityId: 'a', userId: 'user' })
    expect(wrapper.get('[data-test="result"]').text()).toContain('fixed-subject')
    await wrapper.get('[data-test="add"]').trigger('click')
    expect(wrapper.find('[data-test="result"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="preview"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain(en.dirtyPreview)
  })
  it('ignores stale policy and preview responses after application changes', async () => {
    let resolvePolicy, resolvePreview
    api.policy.mockImplementationOnce(() => new Promise(resolve => { resolvePolicy = resolve }))
    await render(); await partner(); await partner('b')
    resolvePolicy({ entityId: 'a', mappings: [] }); await flushPromises()
    expect(wrapper.get('#name-0').element.value).toBe('contact')
    api.preview.mockImplementationOnce(() => new Promise(resolve => { resolvePreview = resolve }))
    await preview(); await partner()
    resolvePreview({ attributes: [], omitted: [], nameId: 'stale' }); await flushPromises()
    expect(wrapper.find('[data-test="result"]').exists()).toBe(false)
  })
  it.each([false, true])('loads using the real registry and independently gates account preview (granted: %s)', async previewGranted => {
    const granted = [Permissions.Settings.Read, Permissions.Settings.Update]
    if (previewGranted) granted.push(Permissions.Claims.Preview)
    permissions.hasPermission.mockImplementation(p => granted.includes(p))
    await render()
    expect(wrapper.text()).not.toContain(en.loading)
    expect(wrapper.get('#saml-partner').text()).toContain('a')
    await partner()
    expect(wrapper.get('[data-test="add"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.findComponent(Picker).exists()).toBe(previewGranted)
    if (previewGranted) {
      expect(api.accounts).toHaveBeenCalledWith('', 0)
      await preview()
      expect(wrapper.get('[data-test="result"]').text()).toContain('fixed-subject')
    } else {
      expect(api.accounts).not.toHaveBeenCalled()
      expect(api.preview).not.toHaveBeenCalled()
      expect(wrapper.find('[data-test="preview"]').exists()).toBe(false)
      expect(wrapper.text()).toContain(en.previewDenied)
    }
  })
  it('gates update independently and denies lookups without read permission', async () => {
    permissions.hasPermission.mockImplementation(p => p !== Permissions.Settings.Update)
    await render(); await partner(); await preview()
    expect(wrapper.get('[data-test="add"]').attributes('disabled')).toBeDefined(); expect(api.preview).toHaveBeenCalledTimes(1)
    wrapper.unmount(); api.partners.mockClear(); api.accounts.mockClear()
    permissions.hasPermission.mockImplementation(p => p === Permissions.Claims.Preview)
    await render(); expect(api.partners).not.toHaveBeenCalled(); expect(api.accounts).not.toHaveBeenCalled()
    expect(wrapper.get('[role="alert"]').text()).toBe(en.errors.forbidden)
  })
  it.each(['en-US', 'zh-TW'])('localizes fields, missing values and typed errors in %s', async locale => {
    const m = locale === 'en-US' ? en : zh
    await render(locale); await partner('b')
    expect(wrapper.get('optgroup').attributes('label')).toBe(m.accountSource)
    expect(wrapper.get('#source-0').text()).toContain(m.fields.Email)
    expect(wrapper.get('#source-0').text()).not.toContain('Person.Email')
    await preview(); expect(wrapper.get('[data-test="result"]').text()).toContain(m.person)
    api.preview.mockRejectedValueOnce(new Error('value_invalid')); await preview()
    expect(wrapper.get('[role="alert"]').text()).toBe(m.errors.value_invalid)
    expect(wrapper.find('[data-test="result"]').exists()).toBe(false)
  })
  it('shows disabled and no-partner states without requesting catalog or account PII', async () => {
    api.partners.mockResolvedValueOnce({ enabled: false, items: [] }); await render()
    expect(wrapper.text()).toContain(en.disabled); expect(api.catalog).not.toHaveBeenCalled(); expect(api.accounts).not.toHaveBeenCalled()
    wrapper.unmount(); api.partners.mockResolvedValueOnce({ enabled: true, items: [] }); await render()
    expect(wrapper.text()).toContain(en.noPartners)
  })
  it('bounds account pages and localizes lookup failures', async () => {
    await render(); await partner(); wrapper.getComponent(Picker).vm.$emit('page', 50); await flushPromises()
    expect(api.accounts).toHaveBeenLastCalledWith('', 50)
    api.accounts.mockRejectedValueOnce(new Error('forbidden')); wrapper.getComponent(Picker).vm.$emit('retry'); await flushPromises()
    expect(wrapper.getComponent(Picker).props('error')).toBe(en.errors.forbidden)
  })
})
