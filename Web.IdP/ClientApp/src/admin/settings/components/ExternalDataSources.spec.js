import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ExternalDataSources from './ExternalDataSources.vue'
import { queryConfigurationApi } from '@/services/queryConfigurationApi'
import en from '@/i18n/locales/en-US/query-fields.json'
import zh from '@/i18n/locales/zh-TW/query-fields.json'
vi.mock('@/services/queryConfigurationApi', async original => ({ ...await original(), queryConfigurationApi: { get: vi.fn() } }))
vi.mock('@/utils/permissionService', () => ({ default: { hasPermission: () => false }, Permissions: { Users: { Read: 'users.read', Update: 'users.update' } } }))
const configuration = { capability: 'profile', sourceId: 'records', providerNamespace: 'example.provider', wireMode: 'neutral-v2', mappings: [{ attribute: 'employeeId', valueSource: 'person.employeeId' }] }
const source = { capability: 'profile', sourceId: 'records', providerNamespace: 'example.provider', available: false, unavailableReason: 'source_paused' }
let wrapper
async function render(data, canUpdate = true, locale = 'en-US') {
  queryConfigurationApi.get.mockResolvedValue(data)
  wrapper = mount(ExternalDataSources, { props: { canUpdate }, global: {
    plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { 'query-fields': en }, 'zh-TW': { 'query-fields': zh } } })],
    stubs: { QueryConfigurationDialog: { name: 'QueryConfigurationDialog', props: ['canUpdate', 'canReadUsers', 'canUpdateUsers', 'initialCapability', 'initialSourceId'], emits: ['saved', 'close'], template: '<div data-dialog />' } }
  } }); await flushPromises()
}
beforeEach(() => vi.resetAllMocks())
afterEach(() => wrapper?.unmount())
describe('External data sources settings', () => {
  it.each(['en-US', 'zh-TW'])('shows stored inputs and blocked status safely in %s', async locale => {
    await render({ configurations: [configuration], sources: [source] }, true, locale)
    const text = locale === 'en-US' ? en : zh
    expect(wrapper.text()).toContain(text.sectionTitle)
    expect(wrapper.text()).toContain(text.attributes.employeeId)
    expect(wrapper.text()).toContain(text.values.person_employeeId)
    expect(wrapper.text()).toContain(text.reasons.source_paused)
    expect(wrapper.text()).toContain(text.blocked)
    expect(wrapper.text()).not.toContain('example.provider')
    expect(queryConfigurationApi.get).toHaveBeenCalledTimes(1)
  })
  it('keeps missing saved sources alongside unconfigured blocked sources with safe fallback', async () => {
    await render({ configurations: [configuration], sources: [{ ...source, sourceId: 'other', unavailableReason: 'unknown private detail' }] })
    expect(wrapper.findAll('[data-test="query-summary-row"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('records')
    expect(wrapper.text()).toContain(en.notConfigured)
    expect(wrapper.text()).toContain(en.reasons.source_unavailable)
    expect(wrapper.text()).not.toContain('unknown private detail')
  })
  it('opens the selected source read-only and refreshes only after save', async () => {
    await render({ configurations: [configuration], sources: [source] }, false)
    await wrapper.get('[data-test="edit-query-summary"]').trigger('click')
    const dialog = wrapper.findComponent({ name: 'QueryConfigurationDialog' })
    expect(dialog.props()).toMatchObject({ canUpdate: false, canReadUsers: false, canUpdateUsers: false, initialCapability: 'profile', initialSourceId: 'records' })
    expect(queryConfigurationApi.get).toHaveBeenCalledTimes(1)
    queryConfigurationApi.get.mockResolvedValue({ configurations: [], sources: [] })
    dialog.vm.$emit('saved'); await flushPromises()
    expect(queryConfigurationApi.get).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain(en.empty)
    expect(wrapper.find('[data-dialog]').exists()).toBe(true)
  })
})

it('does not treat neutral deployment limits as legacy source health', async () => {
  await render({ configurations: [{ ...configuration, wireMode: 'legacy', mappings: [] }], sources: [source] })
  expect(wrapper.get('[data-test="query-summary-row"]').text()).toContain(en.legacyStatus)
  expect(wrapper.get('[data-test="query-summary-row"]').text()).not.toContain(en.reasons.source_paused)
  expect(wrapper.text()).not.toContain(en.blocked)
})
it('identifies an unsupported saved input without hiding its configuration', async () => {
  await render({ configurations: [configuration], sources: [{ ...source, available: true, acceptedAttributeSets: [], availableCandidates: [] }] })
  expect(wrapper.text()).toContain(en.blocked)
  expect(wrapper.text()).toContain(en.values.person_employeeId)
})
