import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import QueryConfigurationDialog from './QueryConfigurationDialog.vue'
import { queryConfigurationApi } from '@/services/queryConfigurationApi'
import en from '@/i18n/locales/en-US/query-fields.json'
import zh from '@/i18n/locales/zh-TW/query-fields.json'

vi.mock('@/services/queryConfigurationApi', async original => ({ ...await original(), queryConfigurationApi: { get: vi.fn(), save: vi.fn() } }))
const capabilities = ['native-profile', 'profile', 'metadata', 'lifecycle-status', 'recovery-identity-verification', 'password-sync']
const source = capability => ({ capability, sourceId: 'records', providerNamespace: 'example.records', available: true, unavailableReason: null, queryRule: null,
  availableCandidates: [{ attribute: 'employeeId', dataType: 'String', valueSources: ['person.employeeId', 'user.employeeId'] }, { attribute: 'nationalIdHash', dataType: 'Sha256Hex', valueSources: ['person.nationalId'] }],
  acceptedAttributeSets: [['employeeId'], ['employeeId', 'nationalIdHash']] })
const config = capability => ({ capability, sourceId: 'records', providerNamespace: 'example.records', wireMode: 'neutral-v2', mappings: [{ attribute: 'employeeId', valueSource: 'person.employeeId' }], queryRule: null })
const fixture = () => ({ revision: 'r1', configurations: [], sources: capabilities.map(source) })
let wrapper
async function render(data = fixture(), props = {}, locale = 'en-US') {
  queryConfigurationApi.get.mockResolvedValue(data)
  wrapper = mount(QueryConfigurationDialog, { props: { canUpdate: true, canReadUsers: true, canUpdateUsers: true, ...props }, global: {
    plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { 'query-fields': en }, 'zh-TW': { 'query-fields': zh } } })],
    stubs: { BaseModal: { template: '<div><slot name="body"/><slot name="footer"/></div>' }, QueryAccountApproval: true }
  } })
  await flushPromises()
}
async function configure(capability = 'native-profile') {
  await wrapper.get('[data-test="query-capability"]').setValue(capability)
  await wrapper.get('[data-test="query-mode"]').setValue('neutral-v2')
  await wrapper.get('[data-test="query-source"]').setValue('0')
  await wrapper.get('[data-test="query-attributes"]').setValue('employeeId')
  await wrapper.get('[data-test="query-value-employeeId"]').setValue('person.employeeId')
}
beforeEach(() => {
  vi.resetAllMocks()
  queryConfigurationApi.save.mockImplementation(async request => ({ ...fixture(), revision: 'r2', configurations: request.configurations }))
})
afterEach(() => wrapper?.unmount())

describe('Query configuration', () => {
  it('opens the exact requested non-Profile row and persists a source switch', async () => {
    const data = fixture()
    const other = { ...source('native-profile'), sourceId: 'second', providerNamespace: 'other.provider', queryRule: 'second-rule' }
    data.sources.push(other)
    data.configurations = [config('native-profile'), { ...config('native-profile'), sourceId: other.sourceId, providerNamespace: other.providerNamespace, queryRule: other.queryRule }]
    await render(data, { initialCapability: 'native-profile', initialSourceId: 'second' })
    expect(wrapper.get('[data-test="query-source"]').element.value).toBe('1')
    expect(wrapper.get('[data-test="query-value-employeeId"]').element.value).toBe('person.employeeId')
    await wrapper.get('[data-test="query-source"]').setValue('0')
    await wrapper.get('[data-test="query-attributes"]').setValue('employeeId')
    await wrapper.get('[data-test="query-value-employeeId"]').setValue('user.employeeId')
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    const request = queryConfigurationApi.save.mock.calls[0][0]
    expect(request.configurations).toEqual([{ ...config('native-profile'), mappings: [{ attribute: 'employeeId', valueSource: 'user.employeeId' }] }])
    wrapper.unmount()
    await render({ ...data, revision: 'r2', configurations: request.configurations }, { initialSourceId: 'records' })
    expect(wrapper.get('[data-test="query-source"]').element.value).toBe('0')
    expect(wrapper.get('[data-test="query-value-employeeId"]').element.value).toBe('user.employeeId')
  })
  it.each(['en-US', 'zh-TW'])('saves six independent services and reloads in %s', async locale => {
    await render(fixture(), {}, locale)
    expect(wrapper.get('[data-test="query-mode"]').element.value).toBe('legacy')
    expect(queryConfigurationApi.save).not.toHaveBeenCalled()
    for (const capability of capabilities) await configure(capability)
    expect(wrapper.get('[data-test="save-query"]').element.disabled).toBe(false)
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(queryConfigurationApi.save).toHaveBeenCalledWith({ expectedRevision: 'r1', configurations: capabilities.map(config) })
    expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).saved)
    const persisted = { ...fixture(), revision: 'r2', configurations: capabilities.map(config) }
    wrapper.unmount(); await render(persisted, {}, locale)
    expect(wrapper.get('[data-test="query-value-employeeId"]').element.value).toBe('person.employeeId')
    expect(wrapper.find('[data-test="open-query-approval"]').exists()).toBe(true)
  })
  it('offers only supported combinations and requires all value sources', async () => {
    await render()
    await configure()
    const sets = wrapper.get('[data-test="query-attributes"]')
    expect(sets.findAll('option').map(option => option.element.value)).toEqual(['', 'employeeId', 'employeeId|nationalIdHash'])
    await sets.setValue('employeeId|nationalIdHash')
    expect(wrapper.get('[data-test="save-query"]').element.disabled).toBe(true)
    expect(wrapper.get('[data-test="query-value-nationalIdHash"]').findAll('option').map(option => option.element.value)).toEqual(['', 'person.nationalId'])
    await wrapper.get('[data-test="query-value-nationalIdHash"]').setValue('person.nationalId')
    expect(wrapper.get('[data-test="save-query"]').element.disabled).toBe(false)
    expect(wrapper.get('[data-test="open-query-approval"]').element.disabled).toBe(true)
  })
  it('retains unavailable persisted selections exactly when another service changes', async () => {
    const data = fixture(); data.configurations = [config('native-profile')]
    data.sources[0] = { ...data.sources[0], available: false, availableCandidates: [], acceptedAttributeSets: [] }
    await render(data)
    expect(wrapper.text()).toContain(en.blocked)
    expect(wrapper.get('[data-test="query-source"]').findAll('option')[1].element.disabled).toBe(true)
    expect(wrapper.get('[data-test="query-attributes"]').element.disabled).toBe(true)
    await configure('metadata')
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(queryConfigurationApi.save.mock.calls[0][0].configurations[0]).toEqual(data.configurations[0])
  })
  it('preserves stale drafts and requires reload before saving again', async () => {
    queryConfigurationApi.save.mockRejectedValue(new Error('conflict'))
    await render(); await configure()
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain(en.draftKept)
    expect(wrapper.get('[data-test="query-value-employeeId"]').element.value).toBe('person.employeeId')
    expect(wrapper.get('[data-test="save-query"]').element.disabled).toBe(true)
    await wrapper.get('[data-test="reload-query"]').trigger('click'); await flushPromises()
    expect(wrapper.get('[data-test="query-mode"]').element.value).toBe('legacy')
  })
  it('uses the server rule as read-only detail without requiring an input', async () => {
    const data = fixture(); data.sources[0].queryRule = 'agreed-rule'
    await render(data); await configure()
    expect(wrapper.text()).toContain(en.rule)
    expect(wrapper.find('input').exists()).toBe(false)
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(queryConfigurationApi.save.mock.calls[0][0].configurations[0].queryRule).toBe('agreed-rule')
  })
  it('keeps view-only configuration and account permissions separate', async () => {
    const data = fixture(); data.configurations = [config('native-profile')]
    await render(data, { canUpdate: false, canReadUsers: false })
    expect(wrapper.find('[data-test="save-query"]').exists()).toBe(false)
    expect(wrapper.get('fieldset').element.disabled).toBe(true)
    expect(wrapper.get('[data-test="open-query-approval"]').element.disabled).toBe(true)
    expect(queryConfigurationApi.save).not.toHaveBeenCalled()
  })
  it('shows non-Profile ambiguity until an explicit source replacement', async () => {
    const data = fixture(); data.sources.push({ ...source('native-profile'), sourceId: 'other' })
    data.configurations = [config('native-profile'), { ...config('native-profile'), sourceId: 'other' }]
    await render(data)
    expect(wrapper.text()).toContain(en.multipleSources)
    expect(wrapper.get('[data-test="query-source"]').element.value).toBe('-1')
    await wrapper.get('[data-test="query-source"]').setValue('1')
    await wrapper.get('[data-test="query-attributes"]').setValue('employeeId')
    await wrapper.get('[data-test="query-value-employeeId"]').setValue('user.employeeId')
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(queryConfigurationApi.save.mock.calls[0][0].configurations).toHaveLength(1)
    expect(queryConfigurationApi.save.mock.calls[0][0].configurations[0].sourceId).toBe('other')
  })
  it('edits a named Profile source without dropping another source', async () => {
    const data = fixture(); data.sources.push({ ...source('profile'), sourceId: 'other' })
    data.configurations = [config('profile'), { ...config('profile'), sourceId: 'other' }]
    await render(data)
    await wrapper.get('[data-test="query-capability"]').setValue('profile')
    expect(wrapper.findAll('[data-test="select-profile-query"]')).toHaveLength(2)
    await wrapper.findAll('[data-test="select-profile-query"]')[1].trigger('click')
    await wrapper.get('[data-test="query-value-employeeId"]').setValue('user.employeeId')
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    const submitted = queryConfigurationApi.save.mock.calls[0][0].configurations
    expect(submitted).toHaveLength(2)
    expect(submitted[0]).toEqual(data.configurations[0])
    expect(submitted[1].mappings[0].valueSource).toBe('user.employeeId')
  })
  it('returns to the existing method only after explicitly choosing and saving it', async () => {
    const data = fixture(); data.configurations = [config('native-profile')]
    await render(data)
    await wrapper.get('[data-test="query-mode"]').setValue('legacy')
    expect(queryConfigurationApi.save).not.toHaveBeenCalled()
    await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
    expect(queryConfigurationApi.save).toHaveBeenCalledWith({ expectedRevision: 'r1', configurations: [] })
  })
})

it.each(['deployment_disabled', 'source_untrusted', 'source_paused', 'invalid_configuration', 'unsupported_inputs', 'source_unavailable'])('explains %s before a source is selectable', async reason => {
  const data = fixture(); data.sources[0] = { ...data.sources[0], available: false, unavailableReason: reason, availableCandidates: [], acceptedAttributeSets: [] }
  await render(data)
  expect(wrapper.get('[data-test="query-source-reasons"]').text()).toContain(en.reasons[reason])
  expect(wrapper.get('[data-test="query-mode"]').element.value).toBe('legacy')
  expect(queryConfigurationApi.save).not.toHaveBeenCalled()
})
it('emits saved only after configuration save succeeds', async () => {
  await render(); await configure()
  expect(wrapper.emitted('saved')).toBeUndefined()
  await wrapper.get('[data-test="save-query"]').trigger('click'); await flushPromises()
  expect(wrapper.emitted('saved')).toHaveLength(1)
})
it('opens the requested named Profile row without selecting a fallback', async () => {
  const data = fixture(); data.sources.push({ ...source('profile'), sourceId: 'other' })
  data.configurations = [config('profile'), { ...config('profile'), sourceId: 'other' }]
  await render(data, { initialCapability: 'profile', initialSourceId: 'other' })
  expect(wrapper.get('[data-test="query-capability"]').element.value).toBe('profile')
  expect(wrapper.findAll('[data-test="select-profile-query"]')[1].attributes('aria-pressed')).toBe('true')
  expect(queryConfigurationApi.save).not.toHaveBeenCalled()
})
