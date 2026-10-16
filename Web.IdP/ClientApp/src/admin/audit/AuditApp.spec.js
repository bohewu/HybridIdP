import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AuditApp from './AuditApp.vue'
import AuditLogFilters from './components/AuditLogFilters.vue'
import AuditLogViewer from './components/AuditLogViewer.vue'
import zhAudit from '../../i18n/locales/zh-TW/audit.json'

vi.mock('@/utils/permissionService', () => ({
  default: { loadPermissions: vi.fn(), hasPermission: () => true },
  Permissions: { Audit: { Read: 'audit.read' } }
}))
vi.mock('@/utils/exportUtils', () => ({ exportToCsv: vi.fn(), exportToExcel: vi.fn() }))

const response = (data) => ({ ok: true, json: async () => data })
const mountApp = () => mount(AuditApp, { global: {
  plugins: [createI18n({ legacy: false, locale: 'zh-TW', messages: { 'zh-TW': { audit: zhAudit } } })],
  directives: { loading: {} },
  stubs: { PageHeader: true, AuditLogExport: true, Pagination: true, LoadingIndicator: true }
} })

describe('Audit filtering and paging', () => {
  beforeEach(() => vi.stubGlobal('fetch', vi.fn((url) => Promise.resolve(response(
    url.endsWith('event-types') ? ['LoginAttempt', 'UserCreated', 'Account.RequiredPasswordChange', 'AuthorizationGrantedFull'] : { items: [], totalCount: 51 }
  )))))

  it('uses exact stored event names and the API pageNumber parameter', async () => {
    const wrapper = mountApp()
    await flushPromises()
    const filters = wrapper.findComponent(AuditLogFilters)
    const select = filters.get('select')
    expect(select.find('option[value="LoginAttempt"]').text()).toBe('登入嘗試')
    expect(select.find('option[value="UserCreated"]').text()).toBe('使用者已建立 (管理員)')
    expect(select.find('option[value="Account.RequiredPasswordChange"]').text()).toBe('要求變更密碼')
    expect(select.find('option[value="AuthorizationGrantedFull"]').text()).toBe('已授予全部請求權限')
    await select.setValue('LoginAttempt')
    await flushPromises()
    wrapper.findComponent(AuditLogViewer).vm.$emit('page-change', 2)
    await flushPromises()
    const url = fetch.mock.calls.at(-1)[0]
    expect(url).toContain('pageNumber=2')
    expect(url).toContain('eventType=LoginAttempt')
    wrapper.unmount()
  })

  it('does not allow an older response to replace a newer filter result', async () => {
    let resolveOld
    fetch.mockImplementation((url) => url.endsWith('event-types')
      ? Promise.resolve(response(['LoginAttempt', 'EmailMfaDisabled']))
      : new Promise(resolve => { resolveOld = resolve }))
    const wrapper = mountApp()
    await flushPromises()
    const old = resolveOld
    fetch.mockImplementation(() => Promise.resolve(response({
      items: [{ id: 2, eventType: 'EmailMfaDisabled', userId: 'verified-id' }], totalCount: 1
    })))
    await wrapper.findComponent(AuditLogFilters).get('select').setValue('EmailMfaDisabled')
    await flushPromises()
    old(response({ items: [{ id: 1, eventType: 'LoginAttempt' }], totalCount: 51 }))
    await flushPromises()
    const viewer = wrapper.findComponent(AuditLogViewer)
    expect(viewer.props('auditEvents')[0].id).toBe(2)
    expect(viewer.props('totalCount')).toBe(1)
    expect(viewer.props('loading')).toBe(false)
    expect(wrapper.text()).toContain('電子郵件 MFA 已停用')
    expect(wrapper.text()).toContain('verified-id')
    wrapper.unmount()
  })
})
