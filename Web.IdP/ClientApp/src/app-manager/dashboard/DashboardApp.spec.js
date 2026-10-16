import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { expect, it, vi } from 'vitest'
import DashboardApp from './DashboardApp.vue'
import appManager from '@/i18n/locales/en-US/app-manager.json'

it('finishes the dashboard dataset loader without calling monitoring or health APIs', async () => {
  const appEl = document.createElement('div')
  appEl.id = 'app'
  Object.assign(appEl.dataset, { displayName: 'Application owner', clientCount: '3', scopeCount: '4', resourceCount: '5' })
  document.body.appendChild(appEl)
  vi.stubGlobal('fetch', vi.fn())
  const wrapper = mount(DashboardApp, { global: {
    plugins: [createI18n({ legacy: false, locale: 'en-US', messages: { 'en-US': { 'app-manager': appManager } } })]
  } })
  await flushPromises()
  expect(wrapper.text()).toContain('Application owner')
  expect(wrapper.get('a[href="/Admin/Clients"]').text()).toContain('3')
  expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
  expect(fetch).not.toHaveBeenCalled()
  wrapper.unmount()
  appEl.remove()
  vi.unstubAllGlobals()
})
