import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ProfileMappingDialog from './ProfileMappingDialog.vue'
import { nativeFieldPolicyApi } from '@/services/nativeFieldPolicyApi'
import queryEn from '@/i18n/locales/en-US/query-fields.json'
import queryZh from '@/i18n/locales/zh-TW/query-fields.json'
import { queryConfigurationApi } from '@/services/queryConfigurationApi'
import en from '@/i18n/locales/en-US/native-fields.json'
import zh from '@/i18n/locales/zh-TW/native-fields.json'
vi.mock('@/services/nativeFieldPolicyApi', async original => ({ ...await original(), nativeFieldPolicyApi: { mappings: vi.fn(), saveMappings: vi.fn() } }))
vi.mock('@/services/queryConfigurationApi', () => ({ queryConfigurationApi: { get: vi.fn() } }))
const source = { source: 'Proof', providerNamespace: 'example.provider', enabled: true, paused: false, userNamePolicyAvailable: true }
const row = { ...source, field: 'FirstName', sourceProperty: 'givenName' }
delete row.enabled; delete row.paused; delete row.userNamePolicyAvailable
const fixture = () => ({ revision: 'revision-a', mappings: [{ ...row }], firstNameFallback: { providerNamespace: source.providerNamespace, enabled: false }, sources: [source, { source: 'Stage1', providerNamespace: 'directory.example', enabled: false, paused: false }], sourceProperties: ['displayName', 'givenName', 'surname', 'department', 'title', 'employeeId', 'EmailConfirmed', 'NationalId'] })
let wrapper
async function render(props = {}, locale = 'en-US') {
  wrapper = mount(ProfileMappingDialog, { props: { canUpdate: true, canSync: true, ...props }, global: { plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { 'native-fields': en, 'query-fields': queryEn }, 'zh-TW': { 'native-fields': zh, 'query-fields': queryZh } }, missingWarn: false })], stubs: { BaseModal: { template: '<div><slot name="body"/><slot name="footer"/></div>' } } } })
  await flushPromises()
}
beforeEach(() => { vi.resetAllMocks(); nativeFieldPolicyApi.mappings.mockResolvedValue(fixture()); nativeFieldPolicyApi.saveMappings.mockResolvedValue(fixture()) })
afterEach(() => wrapper?.unmount())
describe('Profile mapping draft', () => {
  it('defaults to canonical naming and offers only the two supported strategies', async () => {
    await render()
    const select = wrapper.get('[data-test="username-strategy"]')
    expect(select.element.value).toBe('CanonicalAccount')
    expect(select.findAll('option').map(option => option.element.value)).toEqual(['CanonicalAccount', 'Email'])
    expect(wrapper.get('[data-test="add-field"]').text()).not.toContain('username')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].userNamePolicies).toEqual([])
  })
  it.each(['en-US', 'zh-TW'])('saves explicit email naming atomically without immediate sync in %s', async locale => {
    await render({}, locale)
    await wrapper.get('[data-test="username-strategy"]').setValue('Email')
    const words = locale === 'en-US' ? en : zh
    expect(wrapper.text()).toContain(words.userName.help)
    expect(wrapper.text()).toContain(words.userName.emailHelp)
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0]).toMatchObject({ expectedRevision: 'revision-a', mappings: [row], userNamePolicies: [{ source: 'Proof', providerNamespace: source.providerNamespace, strategy: 'Email' }] })
    expect(wrapper.emitted('sync')).toBeUndefined()
  })
  it('retains an unavailable namespace policy and starts a new namespace at canonical', async () => {
    const data = fixture(); data.userNamePolicies = [{ source: 'Proof', providerNamespace: source.providerNamespace, strategy: 'Email' }]
    data.sources[0] = { ...source, providerNamespace: 'new.provider' }
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    const selects = wrapper.findAll('[data-test="username-strategy"]')
    expect(selects[0].element.value).toBe('CanonicalAccount')
    expect(selects[1].element.value).toBe('Email')
    expect(selects[1].attributes('disabled')).toBeDefined()
    await selects[0].setValue('Email')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].userNamePolicies).toEqual([...data.userNamePolicies, { source: 'Proof', providerNamespace: 'new.provider', strategy: 'Email' }])
  })
  it('keeps saved naming when saving mappings and invoking the existing sync flow', async () => {
    const data = fixture(); data.userNamePolicies = [{ source: 'Proof', providerNamespace: source.providerNamespace, strategy: 'Email' }]
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    expect(wrapper.get('[data-test="username-strategy"]').element.value).toBe('Email')
    await wrapper.get('[data-test="remove-mapping"]').trigger('click')
    await wrapper.get('[data-test="save-sync-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].userNamePolicies).toEqual(data.userNamePolicies)
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].mappings).toEqual([])
    expect(wrapper.emitted('sync')).toHaveLength(1)
  })
  it('cancels naming changes without saving', async () => {
    await render()
    await wrapper.get('[data-test="username-strategy"]').setValue('Email')
    await wrapper.findAll('button').find(button => button.text() === en.mapping.cancel).trigger('click')
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
  })
  it('keeps the naming draft and stops edits after a revision conflict', async () => {
    nativeFieldPolicyApi.saveMappings.mockRejectedValue(new Error('conflict'))
    await render()
    await wrapper.get('[data-test="username-strategy"]').setValue('Email')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(wrapper.get('[data-test="username-strategy"]').element.value).toBe('Email')
    expect(wrapper.get('[data-test="username-strategy"]').attributes('disabled')).toBeDefined()
  })
  it('keeps naming editable when native synchronization is disabled and paused', async () => {
    const data = fixture(); data.sources[0] = { ...source, enabled: false, paused: true }
    data.userNamePolicies = [{ source: 'Proof', providerNamespace: source.providerNamespace, strategy: 'Email' }]
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    const select = wrapper.get('[data-test="username-strategy"]')
    expect(select.element.value).toBe('Email')
    expect(select.attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-test="add-source"]').findAll('option')).toHaveLength(1)
    await select.setValue('CanonicalAccount')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].userNamePolicies[0].strategy).toBe('CanonicalAccount')
  })
  it.each([{ canUpdate: false }, { unavailable: true }])('disables naming without update or source authority: %j', async condition => {
    const data = fixture()
    if (condition.unavailable) data.sources[0].userNamePolicyAvailable = false
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render(condition)
    expect(wrapper.get('[data-test="username-strategy"]').attributes('disabled')).toBeDefined()
  })
  it('filters unavailable sources and arbitrary attributes, adds then removes a mapping without saving', async () => {
    await render()
    expect(wrapper.get('[data-test="add-source"]').findAll('option')).toHaveLength(2)
    await wrapper.get('[data-test="add-source"]').setValue('0')
    expect(wrapper.get('[data-test="add-property"]').findAll('option')).toHaveLength(7)
    expect(wrapper.get('[data-test="add-field"]').text()).not.toContain('First name')
    await wrapper.get('[data-test="add-field"]').setValue('Nickname')
    await wrapper.get('[data-test="add-property"]').setValue('displayName')
    await wrapper.get('[data-test="add-mapping"]').trigger('click')
    expect(wrapper.findAll('[data-test="remove-mapping"]')).toHaveLength(2)
    await wrapper.findAll('[data-test="remove-mapping"]')[1].trigger('click')
    expect(wrapper.findAll('[data-test="remove-mapping"]')).toHaveLength(1)
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
  })
  it('cancels inline edits and saves only explicitly completed changes', async () => {
    await render()
    await wrapper.get('[data-test="edit-mapping"]').trigger('click')
    await wrapper.get('[data-test="edit-property"]').setValue('displayName')
    await wrapper.get('[data-test="cancel-edit"]').trigger('click')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].mappings[0].sourceProperty).toBe('givenName')
    expect(wrapper.emitted('sync')).toBeUndefined()
    expect(wrapper.emitted('close')).toHaveLength(1)
    await wrapper.get('[data-test="edit-mapping"]').trigger('click')
    await wrapper.get('[data-test="edit-property"]').setValue('surname')
    await wrapper.get('[data-test="complete-mapping"]').trigger('click')
    await wrapper.get('[data-test="save-sync-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[1][0].mappings[0].sourceProperty).toBe('surname')
    expect(wrapper.emitted('sync')).toHaveLength(1)
  })
  it('preserves a conflicting draft until explicit reload', async () => {
    nativeFieldPolicyApi.saveMappings.mockRejectedValue(new Error('conflict'))
    await render()
    await wrapper.get('[data-test="remove-mapping"]').trigger('click')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain(en.mapping.draftKept)
    expect(wrapper.findAll('[data-test="remove-mapping"]')).toHaveLength(0)
    expect(wrapper.get('[data-test="save-mappings"]').attributes('disabled')).toBeDefined()
    expect(nativeFieldPolicyApi.mappings).toHaveBeenCalledTimes(1)
  })
  it.each(['en-US', 'zh-TW'])('shows unavailable mappings and read-only labels in %s', async locale => {
    const data = fixture(); data.sources[0] = { ...source, paused: true }; nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render({ canUpdate: false, canSync: false }, locale)
    expect(wrapper.find('[data-test="edit-mapping"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="save-mappings"]').exists()).toBe(false)
    expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).mapping.unavailable)
  })
  it('disables add for six mapped fields and immediate sync without authority', async () => {
    const data = fixture(); data.mappings = ['Nickname', 'FirstName', 'LastName', 'Department', 'JobTitle', 'EmployeeId'].map(field => ({ ...row, field })); nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render({ canSync: false })
    expect(wrapper.text()).toContain(en.mapping.allMapped)
    expect(wrapper.get('[data-test="add-mapping"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-test="save-sync-mappings"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings).toHaveBeenCalledOnce()
  })
  it('cancels the whole draft without a settings write', async () => {
    await render(); await wrapper.get('[data-test="remove-mapping"]').trigger('click')
    const cancel = wrapper.findAll('button').find(button => button.text() === en.mapping.cancel)
    await cancel.trigger('click')
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
  })
  it('saves the exact-source fallback only through the mapping footer', async () => {
    await render()
    await wrapper.get('[data-test="mapping-fallback"]').setValue(true)
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].firstNameFallback).toEqual({ providerNamespace: 'example.provider', enabled: true })
  })
  it.each([{ providerNamespace: 'example.provider', enabled: true }, null])('offers a new Proof namespace fallback off until explicitly selected with stored fallback %j', async storedFallback => {
    const data = fixture()
    data.firstNameFallback = storedFallback
    data.sources[0] = { ...source, providerNamespace: 'new.provider' }
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    await wrapper.get('[data-test="add-source"]').setValue('0')
    await wrapper.get('[data-test="add-field"]').setValue('FirstName')
    await wrapper.get('[data-test="add-property"]').setValue('givenName')
    await wrapper.get('[data-test="add-mapping"]').trigger('click')
    const checkbox = wrapper.get('[data-test="mapping-fallback"]')
    expect(checkbox.element.checked).toBe(false)
    expect(checkbox.attributes('disabled')).toBeUndefined()
    await checkbox.setValue(true)
    expect(nativeFieldPolicyApi.saveMappings).not.toHaveBeenCalled()
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0].firstNameFallback).toEqual({ providerNamespace: 'new.provider', enabled: true })
  })
  it('preserves an untouched unavailable namespace fallback while saving another mapping', async () => {
    const data = fixture()
    data.firstNameFallback.enabled = true
    data.sources[0] = { ...source, providerNamespace: 'new.provider' }
    data.mappings.push({ ...row, providerNamespace: 'new.provider' })
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    expect(wrapper.get('[data-test="mapping-fallback"]').element.checked).toBe(false)
    await wrapper.get('[data-test="add-source"]').setValue('0')
    await wrapper.get('[data-test="add-field"]').setValue('Nickname')
    await wrapper.get('[data-test="add-property"]').setValue('displayName')
    await wrapper.get('[data-test="add-mapping"]').trigger('click')
    await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
    expect(nativeFieldPolicyApi.saveMappings.mock.calls[0][0]).toMatchObject({
      firstNameFallback: { providerNamespace: 'example.provider', enabled: true },
      mappings: [...data.mappings, { field: 'Nickname', source: 'Proof', providerNamespace: 'new.provider', sourceProperty: 'displayName' }]
    })
  })
  it('disables fallback changes without update authority', async () => {
    await render({ canUpdate: false })
    expect(wrapper.get('[data-test="mapping-fallback"]').attributes('disabled')).toBeDefined()
  })
  it('disables fallback changes while a save is in progress', async () => {
    let finishSave
    nativeFieldPolicyApi.saveMappings.mockImplementation(() => new Promise(resolve => { finishSave = resolve }))
    await render()
    await wrapper.get('[data-test="save-mappings"]').trigger('click')
    expect(wrapper.get('[data-test="mapping-fallback"]').attributes('disabled')).toBeDefined()
    finishSave(fixture()); await flushPromises()
  })
  it('does not offer fallback for a paused Proof source', async () => {
    const data = fixture(); data.sources[0] = { ...source, paused: true }
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    expect(wrapper.find('[data-test="mapping-fallback"]').exists()).toBe(false)
  })
  it('allows the same target on a distinct source but refuses a duplicate source tuple', async () => {
    const data = fixture(); data.sources[1].enabled = true
    nativeFieldPolicyApi.mappings.mockResolvedValue(data)
    await render()
    await wrapper.get('[data-test="add-source"]').setValue('1')
    expect(wrapper.get('[data-test="add-field"]').text()).toContain('First name')
    await wrapper.get('[data-test="add-field"]').setValue('FirstName')
    await wrapper.get('[data-test="add-property"]').setValue('givenName')
    await wrapper.get('[data-test="add-mapping"]').trigger('click')
    expect(wrapper.findAll('[data-test="edit-mapping"]')).toHaveLength(2)
    expect(wrapper.get('[data-test="add-field"]').text()).not.toContain('First name')
  })
})

it.each([['en-US', en, queryEn], ['zh-TW', zh, queryZh]])('saves and reopens explicit AD displayName mapping without Proof fallback in %s', async (locale, words, queryWords) => {
  const data = fixture()
  data.sources = [{ source: 'NeutralQuery', providerNamespace: 'directory.example', enabled: true, paused: false }]
  data.mappings = []
  queryConfigurationApi.get.mockResolvedValue({ sources: [{ capability: 'native-profile', providerNamespace: 'directory.example', sourceId: 'directory-records', sourceType: 'directory' }] })
  nativeFieldPolicyApi.mappings.mockResolvedValue(data)
  await render({}, locale)
  expect(wrapper.get('[data-test="add-source"]').text()).toContain(queryWords.sourceTypes.directory)
  await wrapper.get('[data-test="add-source"]').setValue('0')
  await wrapper.get('[data-test="add-field"]').setValue('FirstName')
  await wrapper.get('[data-test="add-property"]').setValue('displayName')
  await wrapper.get('[data-test="add-mapping"]').trigger('click')
  expect(wrapper.find('[data-test="mapping-fallback"]').exists()).toBe(false)
  expect(wrapper.get('[data-test="save-mappings"]').text()).toBe(words.mapping.save)
  await wrapper.get('[data-test="save-mappings"]').trigger('click'); await flushPromises()
  const request = nativeFieldPolicyApi.saveMappings.mock.calls[0][0]
  expect(request.mappings).toEqual([{ source: 'NeutralQuery', providerNamespace: 'directory.example', field: 'FirstName', sourceProperty: 'displayName' }])
  expect(request.firstNameFallback.enabled).toBe(false)
  expect(wrapper.emitted('sync')).toBeUndefined()
  wrapper.unmount()
  nativeFieldPolicyApi.mappings.mockResolvedValue({ ...data, mappings: request.mappings })
  await render({}, locale)
  await wrapper.get('[data-test="edit-mapping"]').trigger('click')
  expect(wrapper.get('[data-test="edit-property"]').element.value).toBe('displayName')
})
