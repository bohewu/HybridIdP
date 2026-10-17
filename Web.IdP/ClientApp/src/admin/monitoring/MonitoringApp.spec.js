import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import MonitoringApp from './MonitoringApp.vue'
import permissionService from '@/utils/permissionService'
import monitoring from '@/i18n/locales/en-US/monitoring.json'

vi.mock('@/utils/permissionService', () => ({
  default: { loadPermissions: vi.fn(), hasPermission: vi.fn() },
  Permissions: { Monitoring: { Read: 'monitoring.read' } }
}))
const mountApp = () => mount(MonitoringApp, { global: {
  plugins: [createI18n({ legacy: false, locale: 'en-US', messages: { 'en-US': { monitoring } } })],
  stubs: { ActivityDashboard: true, SecurityMetrics: true, RealTimeAlerts: true, AccessDeniedDialog: true }
} })

describe('Monitoring permission loading', () => {
  let wrapper
  beforeEach(() => {
    permissionService.loadPermissions.mockResolvedValue()
    permissionService.hasPermission.mockReturnValue(true)
  })
  afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks() })
  it('waits for permission before mounting the independent data regions', async () => {
    let resolve
    permissionService.loadPermissions.mockReturnValue(new Promise(done => { resolve = done }))
    wrapper = mountApp()
    expect(wrapper.get('[role="status"]').text()).toBe(monitoring.loading)
    expect(wrapper.find('activity-dashboard-stub').exists()).toBe(false)
    resolve()
    await flushPromises()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.find('activity-dashboard-stub').exists()).toBe(true)
  })
  it('finishes denied permission loading without mounting widgets', async () => {
    permissionService.hasPermission.mockReturnValue(false)
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.get('access-denied-dialog-stub').attributes('show')).toBe('true')
    expect(wrapper.find('activity-dashboard-stub').exists()).toBe(false)
  })
  it('renders failed permission loading separately from data', async () => {
    permissionService.loadPermissions.mockRejectedValue(new Error('Unavailable'))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(monitoring.loadFailed)
    expect(wrapper.find('activity-dashboard-stub').exists()).toBe(false)
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })
})
