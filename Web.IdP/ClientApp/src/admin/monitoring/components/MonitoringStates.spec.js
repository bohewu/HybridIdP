import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ActivityDashboard from './ActivityDashboard.vue'
import SecurityMetrics from './SecurityMetrics.vue'
import RealTimeAlerts from './RealTimeAlerts.vue'
import en from '@/i18n/locales/en-US/monitoring.json'
import zh from '@/i18n/locales/zh-TW/monitoring.json'

const hubs = vi.hoisted(() => [])
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    build() {
      const handlers = {}
      const hub = { handlers, on: (name, handler) => { handlers[name] = handler }, onclose: vi.fn(), start: vi.fn().mockResolvedValue(), stop: vi.fn().mockResolvedValue() }
      hubs.push(hub)
      return hub
    }
  }
}))
const response = data => ({ ok: true, json: async () => data })
const cases = [
  [ActivityDashboard, { activeSessions: 0, totalLogins: 0, failedLogins: 0, riskScore: 0 }, 'activityDashboard.noActivity', 'ActivityStatsUpdated'],
  [SecurityMetrics, { gauges: {}, counters: {}, histograms: {} }, 'securityMetrics.noGauges', 'ReceiveSystemMetrics'],
  [RealTimeAlerts, [], 'realTimeAlerts.noAlerts', 'ReceiveSecurityAlerts']
]
const read = (obj, path) => path.split('.').reduce((value, key) => value[key], obj)

describe.each(cases)('%s monitoring request states', (component, empty, emptyKey, event) => {
  let wrapper
  beforeEach(() => {
    hubs.length = 0
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.spyOn(console, 'debug').mockImplementation(() => {})
    vi.stubGlobal('fetch', vi.fn())
  })
  afterEach(() => {
    wrapper?.unmount()
    expect(hubs.every(hub => hub.stop.mock.calls.length === 1)).toBe(true)
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })
  const mountWidget = (locale = 'en-US') => mount(component, { global: {
    plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { monitoring: en }, 'zh-TW': { monitoring: zh } } })]
  } })

  it('shows one initial placeholder then successful empty data', async () => {
    let resolve
    fetch.mockReturnValue(new Promise(done => { resolve = done }))
    wrapper = mountWidget()
    expect(wrapper.get('[role="status"]').text()).toBe(en.loading)
    expect(wrapper.find('.animate-spin').exists()).toBe(false)
    expect(wrapper.text()).not.toContain(read(en, emptyKey))
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    resolve(response(empty))
    await flushPromises()
    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(false)
    expect(wrapper.text()).toContain(read(en, emptyKey))
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it.each(['en-US', 'zh-TW'])('renders initial failure separately from empty and retries in %s', async locale => {
    const messages = locale === 'en-US' ? en : zh
    fetch.mockResolvedValue({ ok: false, status: 503 })
    wrapper = mountWidget(locale)
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(messages.loadFailed)
    expect(wrapper.text()).not.toContain(read(messages, emptyKey))
    expect(wrapper.findAll('.stat-value')).toHaveLength(0)
    expect(wrapper.text()).not.toContain(messages.stale)
    fetch.mockResolvedValue(response(empty))
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain(read(messages, emptyKey))
  })

  it('retains refresh data with progress, then labels failed refresh data stale and recovers', async () => {
    fetch.mockResolvedValue(response(empty))
    wrapper = mountWidget()
    await flushPromises()
    let reject
    fetch.mockReturnValue(new Promise((_, fail) => { reject = fail }))
    await wrapper.get('button').trigger('click')
    expect(wrapper.get('[role="status"]').text()).toBe(en.refreshing)
    expect(wrapper.text()).toContain(read(en, emptyKey))
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    expect(fetch).toHaveBeenCalledTimes(2)
    reject(new Error('Network failure'))
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain(en.loadFailed)
    expect(wrapper.get('[role="alert"]').text()).toContain(en.stale)
    expect(wrapper.text()).toContain(read(en, emptyKey))
    hubs[0].handlers[event](empty)
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })
})
