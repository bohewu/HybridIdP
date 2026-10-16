import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SettingsApp from './SettingsApp.vue'
import permissionService from '@/utils/permissionService'
import settings from '@/i18n/locales/en-US/settings.json'

vi.mock('@/utils/permissionService', () => ({
  default: { loadPermissions: vi.fn(), hasPermission: vi.fn() },
  Permissions: { Settings: { Read: 'settings.read', Update: 'settings.update' } }
}))
const response = data => ({ ok: true, json: async () => data })
const mountApp = () => mount(SettingsApp, { global: {
  plugins: [createI18n({ legacy: false, locale: 'en-US', messages: { 'en-US': { settings } }, missingWarn: false, fallbackWarn: false })],
  stubs: { AccessDeniedDialog: { template: '<div data-denied>Denied</div>' } }
} })

describe('Settings loading ownership', () => {
  let wrapper
  beforeEach(() => {
    permissionService.loadPermissions.mockResolvedValue()
    permissionService.hasPermission.mockReturnValue(true)
    vi.stubGlobal('fetch', vi.fn().mockImplementation(url => Promise.resolve(response(url.includes('/logging/') ? { level: 'Information' } : []))))
  })
  afterEach(() => {
    wrapper?.unmount()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('uses one permission placeholder before mounting any settings requests', async () => {
    let resolvePermissions
    permissionService.loadPermissions.mockReturnValue(new Promise(resolve => { resolvePermissions = resolve }))
    wrapper = mountApp()
    expect(wrapper.findAll('[role="status"]')).toHaveLength(1)
    expect(fetch).not.toHaveBeenCalled()
    expect(wrapper.find('[data-denied]').exists()).toBe(false)
    resolvePermissions()
    await flushPromises()
    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
  })

  it('keeps completed regions usable while other regions show static placeholders', async () => {
    let resolveBranding
    let resolveMail
    fetch.mockImplementation(url => {
      if (url.includes('prefix=branding.')) return new Promise(resolve => { resolveBranding = resolve })
      if (url.includes('prefix=Mail.')) return new Promise(resolve => { resolveMail = resolve })
      return Promise.resolve(response(url.includes('/logging/') ? { level: 'Information' } : []))
    })
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.findAll('[aria-busy="true"]')).toHaveLength(2)
    expect(wrapper.find('.animate-spin').exists()).toBe(false)
    expect(wrapper.get('#globalLevel').attributes('disabled')).toBeUndefined()
    await wrapper.get('#globalLevel').setValue('Warning')
    expect(wrapper.get('#globalLevel').element.value).toBe('Warning')
    resolveBranding(response([]))
    await flushPromises()
    expect(wrapper.findAll('[aria-busy="true"]')).toHaveLength(1)
    expect(wrapper.get('#appName').attributes('disabled')).toBeUndefined()
    resolveMail(response([]))
    await flushPromises()
    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
  })

  it('does not mount settings regions without read permission', async () => {
    permissionService.hasPermission.mockReturnValue(false)
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.find('[data-denied]').exists()).toBe(true)
    expect(fetch).not.toHaveBeenCalled()
    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
  })

  it('preserves read-only inputs and hides update actions', async () => {
    permissionService.hasPermission.mockImplementation(key => key === 'settings.read')
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.get('#appName').attributes('disabled')).toBeDefined()
    expect(wrapper.get('#globalLevel').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="open-email-test"]').exists()).toBe(false)
    expect(wrapper.findAll('button')).toHaveLength(0)
  })
})
