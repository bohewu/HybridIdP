import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import ProfileSyncAccountPicker from './ProfileSyncAccountPicker.vue'
import { profileSyncApi } from '@/services/profileSyncApi'
import en from '@/i18n/locales/en-US/native-fields.json'
vi.mock('@/services/profileSyncApi', () => ({ profileSyncApi: { searchAccounts: vi.fn(), preview: vi.fn(), apply: vi.fn() } }))
let wrapper
async function render(canRead = true) { wrapper = mount(ProfileSyncAccountPicker, { props: { canRead }, global: { plugins: [createI18n({ legacy: false, locale: 'en-US', messages: { 'en-US': { 'native-fields': en } } })], stubs: { BaseModal: { template: '<div><slot name="body"/><slot name="footer"/></div>' } } } }); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); profileSyncApi.searchAccounts.mockResolvedValue({ items: [{ id: 'account-a', userName: 'Example' }], totalCount: 21 }) })
afterEach(() => wrapper?.unmount())
it('requires explicit selection and emits it without previewing or applying', async () => {
  await render()
  expect(profileSyncApi.searchAccounts).toHaveBeenCalledExactlyOnceWith('', 0)
  expect(wrapper.get('[data-test="review-selected"]').attributes('disabled')).toBeDefined()
  await wrapper.get('input[type="checkbox"]').setValue(true)
  await wrapper.get('[data-test="review-selected"]').trigger('click')
  expect(wrapper.emitted('selected')[0][0]).toEqual([{ id: 'account-a', userName: 'Example' }])
  expect(profileSyncApi.preview).not.toHaveBeenCalled(); expect(profileSyncApi.apply).not.toHaveBeenCalled()
})
it('searches a bounded page and keeps an explicit selection across pages', async () => {
  await render(); await wrapper.get('input[type="checkbox"]').setValue(true)
  await wrapper.findAll('button').find(b => b.text() === en.mapping.next).trigger('click'); await flushPromises()
  expect(profileSyncApi.searchAccounts).toHaveBeenLastCalledWith('', 10)
  await wrapper.get('[data-test="account-search"]').setValue('sample')
  await wrapper.get('form').trigger('submit'); await flushPromises()
  expect(profileSyncApi.searchAccounts).toHaveBeenLastCalledWith('sample', 0)
  expect(wrapper.get('input[type="checkbox"]').element.checked).toBe(true)
})
it('does not query accounts without users.read', async () => {
  await render(false)
  expect(profileSyncApi.searchAccounts).not.toHaveBeenCalled()
  expect(wrapper.text()).toContain(en.mapping.readUsersRequired)
})
