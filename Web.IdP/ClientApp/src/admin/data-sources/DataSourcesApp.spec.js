import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DataSourcesApp from './DataSourcesApp.vue'
import permissionService from '@/utils/permissionService'
import { nativeFieldPolicyApi } from '@/services/nativeFieldPolicyApi'
import { profileSyncApi } from '@/services/profileSyncApi'
import en from '@/i18n/locales/en-US/data-sources.json'
import zh from '@/i18n/locales/zh-TW/data-sources.json'
import native from '@/i18n/locales/en-US/native-fields.json'

vi.mock('@/utils/permissionService', () => ({ default: { isAdmin: true, loadPermissions: vi.fn(), hasPermission: vi.fn() }, Permissions: { Settings: { Read: 'settings.read', Update: 'settings.update' }, Users: { Read: 'users.read', Update: 'users.update' }, Persons: { Update: 'persons.update' } } }))
vi.mock('@/services/nativeFieldPolicyApi', async original => ({ ...await original(), nativeFieldPolicyApi: { sources: vi.fn() } }))
vi.mock('@/services/profileSyncApi', () => ({ profileSyncApi: { status: vi.fn() } }))
let wrapper
async function render(locale = 'en-US') {
  wrapper = mount(DataSourcesApp, { global: {
    plugins: [createI18n({ legacy: false, locale, missingWarn: false, fallbackWarn: false, messages: { 'en-US': { 'data-sources': en, 'native-fields': native }, 'zh-TW': { 'data-sources': zh, 'native-fields': native } } })],
    stubs: { ExternalDataSources: { template: '<div data-sources />' }, AccessDeniedDialog: { template: '<div data-denied />' }, ProfileMappingDialog: { template: '<div data-mappings />' }, ProfileSyncAccountPicker: { emits: ['selected'], template: '<button data-picker @click="$emit(\'selected\', [{ userId: \'one\' }])">Select</button>' }, ProfileSyncDialog: { template: '<div data-review />' } }
  } }); await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks()
  permissionService.isAdmin = true
  permissionService.loadPermissions.mockResolvedValue()
  permissionService.hasPermission.mockReturnValue(true)
  nativeFieldPolicyApi.sources.mockResolvedValue([])
  profileSyncApi.status.mockResolvedValue({ enabled: true })
})
afterEach(() => wrapper?.unmount())
describe('Data sources and sync', () => {
  it.each(['en-US', 'zh-TW'])('offers three areas and independent manual review in %s', async locale => {
    await render(locale)
    const text = locale === 'en-US' ? en : zh
    for (const area of ['sources', 'mappings', 'manual']) expect(wrapper.get(`[data-test="area-${area}"]`).text()).toBe(text[area])
    await wrapper.get('[data-test="area-manual"]').trigger('click'); await flushPromises()
    expect(wrapper.find('[data-test="open-mappings"]').exists()).toBe(false)
    await wrapper.get('[data-test="open-manual-sync"]').trigger('click')
    await wrapper.get('[data-picker]').trigger('click')
    expect(wrapper.find('[data-review]').exists()).toBe(true)
    await wrapper.get('[data-test="area-sources"]').trigger('click')
    expect(wrapper.find('[data-review]').exists()).toBe(false)
    await wrapper.get('[data-test="area-manual"]').trigger('click'); await flushPromises()
    expect(wrapper.find('[data-review]').exists()).toBe(false)
    expect(wrapper.find('[data-picker]').exists()).toBe(false)
  })
  it.each(['users.read', 'users.update', 'persons.update'])('requires %s for manual selection', async missing => {
    permissionService.hasPermission.mockImplementation(key => key !== missing)
    await render(); await wrapper.get('[data-test="area-manual"]').trigger('click'); await flushPromises()
    expect(wrapper.get('[data-test="open-manual-sync"]').element.disabled).toBe(true)
  })
  it('denies non-Admin even with settings permissions', async () => {
    permissionService.isAdmin = false
    await render()
    expect(wrapper.find('[data-denied]').exists()).toBe(true)
    expect(wrapper.find('[data-test="area-sources"]').exists()).toBe(false)
  })
})
