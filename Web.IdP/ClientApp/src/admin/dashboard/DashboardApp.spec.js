import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardApp from './DashboardApp.vue'
import dashboard from '@/i18n/locales/en-US/dashboard.json'

const response = (data, status = 200) => ({ ok: status === 200, status, json: async () => data })
const health = (status = 'Healthy') => ({
  status,
  checkedAtUtc: '2026-01-02T03:04:05Z',
  entries: { sqlserver: { status }, redis: { status: 'Healthy' } }
})
const mountApp = () => mount(DashboardApp, { global: {
  plugins: [createI18n({ legacy: false, locale: 'en-US', missingWarn: false, fallbackWarn: false,
    messages: { 'en-US': { dashboard } } })],
  stubs: { ActivityDashboard: true, SecurityMetrics: true, RealTimeAlerts: true }
} })

describe('Dashboard health', () => {
  let wrapper
  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.stubGlobal('fetch', vi.fn(url => Promise.resolve(response(url.endsWith('/stats')
      ? { totalClients: 3, totalScopes: 4, totalUsers: 5 }
      : health()))))
  })
  afterEach(() => {
    wrapper?.unmount()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('loads protected health independently and displays the server check time', async () => {
    let resolveStats
    fetch.mockImplementation(url => url.endsWith('/stats')
      ? new Promise(resolve => { resolveStats = resolve })
      : Promise.resolve(response(health())))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.element.dataset.loading).toBeUndefined()
    expect(wrapper.get('[aria-busy="true"]').text()).toContain(dashboard.loading)
    expect(wrapper.text()).toContain(new Date(health().checkedAtUtc).toLocaleString('en-US'))
    expect(fetch).toHaveBeenCalledWith('/api/admin/dashboard/health', {
      credentials: 'include', cache: 'no-store'
    })
    expect(fetch).not.toHaveBeenCalledWith('/health')
    resolveStats(response({ totalClients: 3, totalScopes: 4, totalUsers: 5 }))
    await flushPromises()
    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
    expect(wrapper.get('[data-test-id="dashboard-stats-clients"]').text()).toContain('3')
  })

  it('keeps independent health and monitoring regions visible when stats fail', async () => {
    fetch.mockImplementation(url => Promise.resolve(response(url.endsWith('/stats') ? {} : health(), url.endsWith('/stats') ? 503 : 200)))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.text()).toContain(dashboard.errors.loadFailed)
    expect(wrapper.text()).toContain('Healthy')
    expect(wrapper.find('activity-dashboard-stub').exists()).toBe(true)
  })

  it.each(['Degraded', 'Unhealthy'])('shows a completed %s result instead of treating it as an HTTP failure', async status => {
    fetch.mockImplementation(url => Promise.resolve(response(url.endsWith('/stats') ? {} : health(status))))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.text()).toContain(status)
    expect(wrapper.text()).not.toContain(dashboard.errors.healthFailed)
  })

  it('clears previous success while refreshing, shows failure, and can recover', async () => {
    wrapper = mountApp()
    await flushPromises()
    const checkedTime = new Date(health().checkedAtUtc).toLocaleString('en-US')
    let resolveRefresh
    fetch.mockImplementation(() => new Promise(resolve => { resolveRefresh = resolve }))
    await wrapper.get('[data-test-id="dashboard-health-refresh"]').trigger('click')
    expect(wrapper.text()).toContain(dashboard.health.checking)
    expect(wrapper.findAll('[role="status"]').length).toBe(1)
    expect(wrapper.find('.animate-spin').exists()).toBe(false)
    expect(wrapper.text()).not.toContain(checkedTime)
    expect(wrapper.get('[data-test-id="dashboard-health-refresh"]').attributes('disabled')).toBeDefined()
    resolveRefresh(response({}, 503))
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(dashboard.errors.healthFailed)
    expect(wrapper.text()).not.toContain(checkedTime)
    fetch.mockResolvedValue(response(health('Unhealthy')))
    await wrapper.get('[data-test-id="dashboard-health-refresh"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Unhealthy')
  })

  it.each([401, 403])('does not display healthy data when the protected endpoint returns %s', async status => {
    fetch.mockImplementation(url => Promise.resolve(response(url.endsWith('/stats') ? {} : health(),
      url.endsWith('/stats') ? 200 : status)))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(dashboard.errors.healthFailed)
    expect(wrapper.text()).not.toContain('Last updated:')
  })

  it('rejects malformed check data and only renders the known sanitized checks', async () => {
    fetch.mockImplementation(url => Promise.resolve(response(url.endsWith('/stats') ? {} : {
      ...health(), entries: { sqlserver: { status: 'unknown' } }
    })))
    wrapper = mountApp()
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(dashboard.errors.healthFailed)
    fetch.mockResolvedValue(response({ ...health(), entries: {
      sqlserver: { status: 'Healthy', description: 'private details' },
      privateService: { status: 'Healthy' }
    } }))
    await wrapper.get('[data-test-id="dashboard-health-refresh"]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('SQL Server')
    expect(wrapper.text()).not.toContain('privateService')
    expect(wrapper.text()).not.toContain('private details')
  })
})
