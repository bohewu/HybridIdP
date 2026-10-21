import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, expect, it, vi } from 'vitest'
import ProfileSyncDialog from './ProfileSyncDialog.vue'
import UsersApp from '@/admin/users/UsersApp.vue'
import PersonsApp from '@/admin/persons/PersonsApp.vue'
import permissionService from '@/utils/permissionService'
vi.mock('@/utils/permissionService', async importOriginal => ({ ...await importOriginal(), default: { loadPermissions: vi.fn().mockResolvedValue(), hasPermission: vi.fn() } }))
const list = { name: 'UserList', props: ['canSync'], emits: ['profile-sync'], template: '<div />' }
let wrapper
afterEach(() => { wrapper?.unmount(); vi.unstubAllGlobals(); vi.clearAllMocks() })
const globalOptions = () => ({ plugins: [createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false, messages: {} })], stubs: { UserList: list, PageHeader: true, ProfileSyncDialog: true, UserForm: true, RoleAssignment: true, UserSessions: true, LoginHistoryDialog: true, RecoveryAssistanceDialog: true, AccessDeniedDialog: true, NativeFieldPolicyDialog: true, PersonForm: true, LinkedAccountsDialog: true, TransferAssetsDialog: true } })
it.each([[true, false], [false, true], [true, true]])('requires both update permissions on Users and People entries (users=%s people=%s)', async (usersUpdate, personsUpdate) => {
  permissionService.hasPermission.mockImplementation(permission => permission === 'users.update' ? usersUpdate : permission === 'persons.update' ? personsUpdate : true)
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [{ id: 'user-a', userName: 'example-a' }], persons: [{ id: 'person-a', nickname: 'Example person' }], totalCount: 1 }) }))
  wrapper = mount(UsersApp, { global: globalOptions() }); await flushPromises()
  expect(wrapper.findComponent(list).props('canSync')).toBe(usersUpdate && personsUpdate)
  wrapper.findComponent(list).vm.$emit('profile-sync', [{ id: 'user-a', userName: 'example-a' }]); await flushPromises()
  expect(wrapper.find('profile-sync-dialog-stub').exists()).toBe(usersUpdate && personsUpdate)
  wrapper.unmount()
  wrapper = mount(PersonsApp, { global: globalOptions() }); await flushPromises()
  expect(wrapper.find('[data-test="person-manual-sync"]').exists()).toBe(usersUpdate && personsUpdate)
  if (usersUpdate && personsUpdate) {
    await wrapper.get('[data-test="person-manual-sync"]').trigger('click')
    expect(wrapper.findComponent(ProfileSyncDialog).props('personId')).toBe('person-a')
  }
})
