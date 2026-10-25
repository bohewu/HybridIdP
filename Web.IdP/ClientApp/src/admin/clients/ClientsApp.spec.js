import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ClientsApp from './ClientsApp.vue'
import ClientList from './components/ClientList.vue'
import ClientForm from './components/ClientForm.vue'
import enClients from '@/i18n/locales/en-US/clients.json'
import zhClients from '@/i18n/locales/zh-TW/clients.json'

vi.mock('@/utils/permissionService', () => ({
  default: { reloadPermissions: vi.fn(), hasPermission: () => true },
  Permissions: { Clients: { Read: 'clients.read', Create: 'clients.create', Update: 'clients.update', Delete: 'clients.delete' } }
}))

const summary = { id: 'client-1', clientId: 'test-client', displayName: 'Test Client', type: 'confidential' }
const detail = {
  ...summary,
  applicationType: 'web',
  consentType: 'explicit',
  redirectUris: ['https://example.com/callback'],
  postLogoutRedirectUris: [],
  permissions: ['ept:authorization', 'ept:token', 'gt:authorization_code'],
  pkceMethodPolicy: 'S256',
  tokenEndpointAuthMethod: 'client_secret_basic',
  responseModePolicy: 'form_post'
}
const response = data => ({ ok: true, json: async () => data })

describe('ClientsApp detail loading', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it.each(['en-US', 'zh-TW'])('keeps failed detail loads unsaveable and retries with saved policies (%s)', async locale => {
    let failDetail = true
    vi.stubGlobal('fetch', vi.fn(async (url, options = {}) => {
      if (url.startsWith('/api/admin/clients?')) return response({ items: [summary], totalCount: 1 })
      if (url === '/api/admin/clients/client-1' && !options.method) {
        return failDetail ? { ok: false, status: 503 } : response(detail)
      }
      return response({ id: summary.id, scopes: [] })
    }))
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const wrapper = mount(ClientsApp, { global: {
      plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { clients: enClients }, 'zh-TW': { clients: zhClients } } })],
      directives: { loading: {} },
      stubs: {
        ClientList: true,
        PageHeader: true,
        SearchInput: true,
        Pagination: true,
        AccessDeniedDialog: true,
        BaseModal: { template: '<div><slot name="body"></slot><slot name="footer"></slot></div>' },
        ClientScopeManager: true,
        AuthUrlGenerator: true,
        SecretDisplayModal: true
      }
    } })
    await flushPromises()

    wrapper.findComponent(ClientList).vm.$emit('edit', summary)
    await flushPromises()
    expect(wrapper.findComponent(ClientForm).exists()).toBe(false)
    expect(wrapper.find('[data-test-id="client-form-submit"]').exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toBe((locale === 'en-US' ? enClients : zhClients).detailsLoadFailed)
    expect(fetch.mock.calls.some(([, options]) => options?.method === 'PUT')).toBe(false)

    failDetail = false
    wrapper.findComponent(ClientList).vm.$emit('edit', summary)
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.get('#pkce-method-policy').element.value).toBe(detail.pkceMethodPolicy)
    expect(wrapper.get('#token-endpoint-auth-method').element.value).toBe(detail.tokenEndpointAuthMethod)
    expect(wrapper.get('#response-mode-policy').element.value).toBe(detail.responseModePolicy)
    await wrapper.get('[data-test-id="client-form-submit"]').trigger('click')
    await flushPromises()
    const update = fetch.mock.calls.find(([url, options]) => url === '/api/admin/clients/client-1' && options?.method === 'PUT')
    expect(update).toBeTruthy()
    expect(JSON.parse(update[1].body)).toMatchObject({
      pkceMethodPolicy: detail.pkceMethodPolicy,
      tokenEndpointAuthMethod: detail.tokenEndpointAuthMethod,
      responseModePolicy: detail.responseModePolicy
    })
    wrapper.unmount()
  })
})
