import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import NativeFieldPolicyDialog from './NativeFieldPolicyDialog.vue'
import NativeFieldSources from '@/admin/settings/components/NativeFieldSources.vue'
import permissionService from '@/utils/permissionService'
import en from '@/i18n/locales/en-US/native-fields.json'
import zh from '@/i18n/locales/zh-TW/native-fields.json'

vi.mock('@/utils/permissionService', () => ({
  default: { loadPermissions: vi.fn(), hasPermission: vi.fn() },
  Permissions: { Settings: { Read: 'settings.read' }, Users: { Read: 'users.read', Update: 'users.update' }, Persons: { Update: 'persons.update' } }
}))
const identity = (source, account = 'account-a') => ({ source, localAccountId: account, providerNamespace: 'example', stableSubject: 'subject-a' })
const sources = [
  { source: 'Proof', enabled: true, paused: false, providerNamespace: 'example.provider', firstNameDisplayNameFallback: false, supportedFields: ['Nickname'], revision: 'source-r1' },
  { source: 'Stage1', enabled: false, paused: false, supportedFields: ['Nickname', 'FirstName'], revision: 'source-r2' }
]
const target = () => ({ target: 'Person', targetId: 'target-a', revision: 'r1', availableBindings: [identity('Proof'), identity('Stage1'), identity('Proof', 'account-b')], fields: [
  { field: 'Nickname', value: 'Local value', manualFixed: true, attributed: false, owner: identity('Proof'), allowOverwrite: false },
  { field: 'FirstName', value: '', manualFixed: false, attributed: false, owner: null, allowOverwrite: false }
] })
const response = (data, status = 200) => ({ ok: status < 400, status, json: async () => JSON.parse(JSON.stringify(data)) })
const i18n = locale => createI18n({ legacy: false, locale, messages: { 'en-US': { 'native-fields': en }, 'zh-TW': { 'native-fields': zh } } })
const modal = { template: '<div><slot name="body"/><slot name="footer"/></div>' }
describe('Native field policy controls', () => {
  let wrapper, current
  beforeEach(() => {
    current = target()
    document.head.innerHTML = '<meta name="csrf-token" content="synthetic-csrf">'
    permissionService.loadPermissions.mockResolvedValue()
    permissionService.hasPermission.mockReturnValue(true)
    vi.stubGlobal('fetch', vi.fn(async (url, options) => {
      if (options?.method === 'PUT') {
        const body = JSON.parse(options.body)
        if (body.operation === 'Manual') current.fields[0] = { ...current.fields[0], value: body.manualValue, manualFixed: true }
        if (body.operation === 'Unlock') current.fields[0].manualFixed = false
        current.revision = 'r2'
        return response(current)
      }
      return response(url.endsWith('/sources') ? sources : current)
    }))
  })
  afterEach(() => { wrapper?.unmount(); vi.unstubAllGlobals(); vi.restoreAllMocks(); document.head.innerHTML = '' })
  async function dialog(canUpdate = true, locale = 'en-US') {
    wrapper = mount(NativeFieldPolicyDialog, { props: { target: 'Person', targetId: 'target-a', targetLabel: 'Example person', canUpdate }, global: { plugins: [i18n(locale)], stubs: { BaseModal: modal } } })
    await flushPromises()
  }
  const mutations = () => fetch.mock.calls.filter(([, options]) => options?.method === 'PUT')

  it.each([['en-US', en], ['zh-TW', zh]])('explains sources and intentional clearing in %s', async (locale, strings) => {
    await dialog(true, locale)
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    const input = wrapper.get('[data-test="manual-value"]')
    expect(input.attributes('placeholder')).toBe(strings.manualPlaceholders.Nickname)
    expect(wrapper.get(`#${input.attributes('aria-describedby')}`).text()).toBe(strings.manualHelp)
    expect(wrapper.text()).toContain(strings.sources.Proof)
    await wrapper.get('[data-test="field"]').setValue('FirstName')
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    expect(wrapper.get('[data-test="manual-value"]').attributes('placeholder')).toBe(strings.manualPlaceholders.FirstName)
    expect(wrapper.text()).toContain(strings.noClear)
    expect(mutations()).toHaveLength(0)
    wrapper.unmount()
    wrapper = mount(NativeFieldSources, { props: { canUpdate: false }, global: { plugins: [i18n(locale)] } })
    await flushPromises()
    expect(wrapper.text()).toContain(strings.sources.Proof)
    expect(wrapper.text()).toContain(strings.sources.Stage1)
    expect(wrapper.text()).toContain(strings.mapping.sourceHelp)
  })

  it('disables deployment-disabled, unsupported and different-owner bindings', async () => {
    await dialog()
    expect(wrapper.text()).toContain('Person record: Example person')
    expect(wrapper.text()).toContain('target-a')
    await wrapper.get('[data-test="choose-assignment"]').trigger('click')
    const choices = wrapper.get('[data-test="binding"]').findAll('option')
    expect(choices[1].element.disabled).toBe(false)
    expect(choices[2].element.disabled).toBe(true)
    expect(choices[3].element.disabled).toBe(true)
    await wrapper.get('[data-test="field"]').setValue('FirstName')
    await wrapper.get('[data-test="choose-assignment"]').trigger('click')
    expect(wrapper.get('[data-test="binding"]').findAll('option')[1].element.disabled).toBe(true)
    expect(wrapper.get('[data-test="assign"]').element.disabled).toBe(true)
    expect(mutations()).toHaveLength(0)
    expect(wrapper.text()).toContain(en.noClear)
  })

  it('sends deliberate fixed blank and explicit unlock with the returned revision and cookie CSRF', async () => {
    await dialog()
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    await wrapper.get('[data-test="manual-value"]').setValue('')
    expect(wrapper.get('[data-test="fix"]').text()).toBe(en.clearAndFix)
    await wrapper.get('[data-test="fix"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(mutations()[0][1].body)).toEqual({ operation: 'Manual', expectedRevision: 'r1', manualValue: '', allowSourceClear: false })
    expect(mutations()[0][0]).toBe('/api/admin/native-field-policies/persons/target-a/Nickname')
    expect(mutations()[0][1].credentials).toBe('include')
    expect(mutations()[0][1].headers['X-XSRF-TOKEN']).toBe('synthetic-csrf')
    expect(wrapper.text()).toContain(en.fixed)
    expect(wrapper.text()).toContain(en.blank)
    await wrapper.get('[data-test="unlock"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(mutations()[1][1].body)).toEqual({ operation: 'Unlock', expectedRevision: 'r2', allowSourceClear: false })
    expect(wrapper.text()).toContain(en.unlocked)
    expect(wrapper.emitted('changed')).toHaveLength(2)
  })

  it('assigns only a persisted binding with explicit overwrite and retains fixed status', async () => {
    await dialog()
    await wrapper.get('[data-test="choose-assignment"]').trigger('click')
    await wrapper.get('[data-test="binding"]').setValue('0')
    await wrapper.get('[data-test="overwrite"]').setValue(true)
    await wrapper.get('[data-test="assign"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(mutations()[0][1].body)).toEqual({ operation: 'AssignSource', expectedRevision: 'r1', sourceIdentity: identity('Proof'), allowOverwrite: true, allowSourceClear: false })
    expect(wrapper.text()).toContain(en.fixed)
    expect(wrapper.text()).toContain(en.timing)
  })

  it('keeps manual actions available without settings.read but refuses unverifiable source selection', async () => {
    permissionService.hasPermission.mockReturnValue(false)
    await dialog()
    expect(fetch.mock.calls.some(([url]) => url.endsWith('/sources'))).toBe(false)
    await wrapper.get('[data-test="choose-assignment"]').trigger('click')
    expect(wrapper.text()).toContain(en.sourcePermission)
    expect(wrapper.get('[data-test="binding"]').findAll('option')[1].element.disabled).toBe(true)
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    expect(wrapper.get('[data-test="fix"]').element.disabled).toBe(false)
  })

  it('shows saved values with no mutation controls for a read-only operator', async () => {
    await dialog(false)
    expect(wrapper.find('[data-test="fix"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="unlock"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="assign"]').exists()).toBe(false)
    expect(wrapper.findAll('fieldset')).toHaveLength(0)
    expect(wrapper.find('[data-test="choose-manual"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="choose-assignment"]').exists()).toBe(false)
    expect(wrapper.text()).toContain(en.readOnly)
    expect(mutations()).toHaveLength(0)
  })

  it.each([[400, 'source_unavailable'], [409, 'conflict'], [400, 'owner_mismatch']])('localizes server rejection %s %s without claiming success', async (status, code) => {
    await dialog(true, 'zh-TW')
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    fetch.mockResolvedValueOnce(response({ error: code }, status))
    await wrapper.get('[data-test="fix"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(zh.errors[code])
    expect(wrapper.emitted('changed')).toBeUndefined()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })

  it('shows deployment state and prevents activation, while pausing only an enabled source', async () => {
    wrapper = mount(NativeFieldSources, { props: { canUpdate: true }, global: { plugins: [i18n('en-US')] } })
    await flushPromises()
    const buttons = wrapper.findAll('[data-test="toggle-source"]')
    expect(buttons[0].element.disabled).toBe(false)
    expect(buttons).toHaveLength(1)
    expect(wrapper.text()).toContain(en.status.disabled)
    await buttons[0].trigger('click')
    await flushPromises()
    expect(mutations()[0][0]).toBe('/api/admin/native-field-policies/sources/Proof')
    expect(JSON.parse(mutations()[0][1].body)).toEqual({ paused: true, expectedRevision: 'source-r1' })
  })

  it.each([['en-US', en], ['zh-TW', zh]])('progressively opens one action while keeping unlock separate in %s', async (locale, strings) => {
    await dialog(true, locale)
    expect(wrapper.get('[data-test="current-value"]').text()).toBe('Local value')
    expect(wrapper.find('[data-test="manual-value"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="binding"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="unlock"]').text()).toBe(strings.unlock)
    expect(wrapper.get('[data-test="policy-details"]').element.open).toBe(false)
    expect(wrapper.get('[data-test="policy-details"]').text()).toContain('target-a')
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    expect(wrapper.get('[data-test="choose-manual"]').attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('[data-test="binding"]').exists()).toBe(false)
    await wrapper.get('[data-test="choose-assignment"]').trigger('click')
    expect(wrapper.find('[data-test="manual-value"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="binding"]').exists()).toBe(true)
    expect(wrapper.text()).toContain(strings.assignmentTiming)
    await wrapper.get('[data-test="field"]').setValue('FirstName')
    expect(wrapper.find('[data-test="binding"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="unlock"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="current-value"]').text()).toBe(strings.blank)
    expect(mutations()).toHaveLength(0)
  })

  it('blocks duplicate manual saves while busy and keeps the action after a conflict for explicit reload', async () => {
    await dialog()
    await wrapper.get('[data-test="choose-manual"]').trigger('click')
    let finish
    fetch.mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
    await wrapper.get('[data-test="fix"]').trigger('click')
    expect(wrapper.get('fieldset').element.disabled).toBe(true)
    expect(wrapper.get('[data-test="choose-assignment"]').element.disabled).toBe(true)
    await wrapper.get('[data-test="fix"]').trigger('click')
    expect(mutations()).toHaveLength(1)
    finish(response({ error: 'conflict' }, 409))
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(en.errors.conflict)
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="manual-value"]').element.value).toBe('Local value')
    expect(wrapper.get('fieldset').element.disabled).toBe(false)
  })

  it('has matching localized policy keys', () => {
    const keys = object => Object.entries(object).flatMap(([key, value]) => typeof value === 'object' ? keys(value).map(child => `${key}.${child}`) : [key]).sort()
    expect(keys(zh)).toEqual(keys(en))
  })
})
