import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import QueryAccountApproval from './QueryAccountApproval.vue'
import { queryConfigurationApi } from '@/services/queryConfigurationApi'
import { profileSyncApi } from '@/services/profileSyncApi'
import en from '@/i18n/locales/en-US/query-fields.json'
import zh from '@/i18n/locales/zh-TW/query-fields.json'

vi.mock('@/services/queryConfigurationApi', async original => ({ ...await original(), queryConfigurationApi: { approval: vi.fn(), saveApproval: vi.fn() } }))
vi.mock('@/services/profileSyncApi', () => ({ profileSyncApi: { searchAccounts: vi.fn() } }))
const state = () => ({ revision: 'a1', approved: false, valueSources: ['person.nationalId'], requiresNationalIdAlgorithmApproval: true })
let wrapper
async function render(props = {}, locale = 'en-US') {
  wrapper = mount(QueryAccountApproval, { props: { capability: 'native-profile', sourceId: 'records', canRead: true, canUpdate: true, ...props }, global: {
    plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { 'query-fields': en }, 'zh-TW': { 'query-fields': zh } } })]
  } })
  await flushPromises()
}
async function choose() {
  await wrapper.get('[data-test="approval-search"] input').setValue('example')
  await wrapper.get('form').trigger('submit'); await flushPromises()
  await wrapper.get('[data-test="choose-approval-account"]').trigger('click'); await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks()
  profileSyncApi.searchAccounts.mockResolvedValue({ items: [{ id: 'account-a', userName: 'example' }], totalCount: 1 })
  queryConfigurationApi.approval.mockResolvedValue(state())
  queryConfigurationApi.saveApproval.mockResolvedValue({ ...state(), revision: 'a2', approved: true })
})
afterEach(() => wrapper?.unmount())

it.each(['en-US', 'zh-TW'])('requires explicit stored-value and algorithm approval in %s', async locale => {
  await render({}, locale)
  expect(profileSyncApi.searchAccounts).not.toHaveBeenCalled()
  await choose()
  expect(profileSyncApi.searchAccounts).toHaveBeenCalledWith('example', 0)
  expect(queryConfigurationApi.approval).toHaveBeenCalledWith('native-profile', 'records', 'account-a')
  await wrapper.get('[data-test="approval-confirm"]').setValue(true)
  expect(wrapper.get('[data-test="approve-query-account"]').element.disabled).toBe(true)
  await wrapper.get('[data-test="approval-algorithm"]').setValue(true)
  await wrapper.get('[data-test="approve-query-account"]').trigger('click'); await flushPromises()
  expect(queryConfigurationApi.saveApproval).toHaveBeenCalledWith('native-profile', 'records', 'account-a', { expectedRevision: 'a1', approved: true, nationalIdAlgorithmApproved: true })
  expect(wrapper.text()).toContain((locale === 'en-US' ? en : zh).approval.approved)
  expect(wrapper.emitted('busy')).toEqual([[true], [false]])
})
it('permits removing explicit approval without approving an algorithm', async () => {
  queryConfigurationApi.approval.mockResolvedValue({ ...state(), approved: true })
  await render(); await choose()
  await wrapper.get('[data-test="revoke-query-account"]').trigger('click'); await flushPromises()
  expect(queryConfigurationApi.saveApproval.mock.calls[0][3]).toEqual({ expectedRevision: 'a1', approved: false, nationalIdAlgorithmApproved: false })
})
it('requires a fresh approval read after account revision conflict', async () => {
  queryConfigurationApi.saveApproval.mockRejectedValue(new Error('conflict'))
  await render(); await choose()
  await wrapper.get('[data-test="approval-confirm"]').setValue(true)
  await wrapper.get('[data-test="approval-algorithm"]').setValue(true)
  await wrapper.get('[data-test="approve-query-account"]').trigger('click'); await flushPromises()
  expect(wrapper.text()).toContain(en.errors.conflict)
  expect(wrapper.get('[data-test="approve-query-account"]').element.disabled).toBe(true)
  await wrapper.get('[data-test="reload-approval"]').trigger('click'); await flushPromises()
  expect(queryConfigurationApi.approval).toHaveBeenCalledTimes(2)
  expect(wrapper.get('[data-test="approval-confirm"]').element.checked).toBe(false)
})
it('keeps account read permission independent from update permission', async () => {
  await render({ canUpdate: false }); await choose()
  expect(wrapper.text()).toContain(en.approval.writeRequired)
  expect(wrapper.find('[data-test="approve-query-account"]').exists()).toBe(false)
  expect(queryConfigurationApi.saveApproval).not.toHaveBeenCalled()
})
it('does not search or request account approval without account read permission', async () => {
  await render({ canRead: false })
  await wrapper.get('form').trigger('submit'); await flushPromises()
  expect(profileSyncApi.searchAccounts).not.toHaveBeenCalled()
  expect(queryConfigurationApi.approval).not.toHaveBeenCalled()
})
it('does not let an older account response replace the selected account', async () => {
  let first
  profileSyncApi.searchAccounts.mockResolvedValue({ items: [{ id: 'account-a', userName: 'first' }, { id: 'account-b', userName: 'second' }], totalCount: 2 })
  queryConfigurationApi.approval.mockImplementation((capability, sourceId, userId) => userId === 'account-a' ? new Promise(resolve => { first = resolve }) : Promise.resolve({ ...state(), revision: 'b1', requiresNationalIdAlgorithmApproval: false, valueSources: ['user.userName'] }))
  await render()
  await wrapper.get('form').trigger('submit'); await flushPromises()
  const buttons = wrapper.findAll('[data-test="choose-approval-account"]')
  await buttons[0].trigger('click'); await buttons[1].trigger('click'); await flushPromises()
  first(state()); await flushPromises()
  expect(wrapper.find('[data-test="approval-algorithm"]').exists()).toBe(false)
  await wrapper.get('[data-test="approval-confirm"]').setValue(true)
  await wrapper.get('[data-test="approve-query-account"]').trigger('click'); await flushPromises()
  expect(queryConfigurationApi.saveApproval.mock.calls[0].slice(2)).toEqual(['account-b', { expectedRevision: 'b1', approved: true, nationalIdAlgorithmApproved: false }])
})
