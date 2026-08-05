import { mount, flushPromises } from '@vue/test-utils'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import ClientForm from '../ClientForm.vue'
import enUsClients from '@/i18n/locales/en-US/clients.json'
import zhTwClients from '@/i18n/locales/zh-TW/clients.json'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key) => key
  })
}))

const BaseModal = {
  template: '<div><slot name="body"></slot><slot name="footer"></slot></div>'
}

const client = {
  id: 'client-1',
  clientId: 'test-client',
  displayName: 'Test Client',
  applicationType: 'web',
  type: 'confidential',
  consentType: 'explicit',
  redirectUris: ['https://example.com/callback'],
  postLogoutRedirectUris: [],
  permissions: ['ept:authorization', 'ept:token', 'gt:authorization_code', 'scp:openid']
}

const mountForm = (clientOverride = client) => mount(ClientForm, {
  props: { client: clientOverride },
  global: {
    stubs: {
      BaseModal,
      ClientScopeManager: true,
      AuthUrlGenerator: true,
      SecretDisplayModal: true
    },
    mocks: {
      $t: (key) => key
    }
  }
})

describe('ClientForm.vue MFA requirement', () => {
  beforeEach(() => {
    global.fetch = vi.fn((url) => {
      if (url === '/api/admin/clients/client-1/required-scopes') {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ scopes: [] }) })
      }

      return Promise.resolve({ ok: true, json: () => Promise.resolve({ id: 'client-1' }) })
    })
  })

  it('defaults Require MFA to false when the client response omits it', async () => {
    const wrapper = mountForm()
    await flushPromises()

    const toggle = wrapper.find('#require-mfa')
    expect(toggle.element.checked).toBe(false)
    expect(toggle.attributes('aria-describedby')).toBe('require-mfa-help')
  })

  it('initializes Require MFA from the client response and submits its configured value', async () => {
    const wrapper = mountForm({ ...client, requireMfa: true })
    await flushPromises()

    expect(wrapper.find('#require-mfa').element.checked).toBe(true)

    await wrapper.find('#require-mfa').setValue(false)
    await wrapper.find('[data-test-id="client-form-submit"]').trigger('click')
    await flushPromises()

    const updateCall = global.fetch.mock.calls.find(([url]) => url === '/api/admin/clients/client-1')
    expect(updateCall).toBeDefined()
    expect(JSON.parse(updateCall[1].body)).toMatchObject({ requireMfa: false })
  })

  it('provides Require MFA labels and additive-policy help in both client locales', () => {
    for (const locale of [enUsClients, zhTwClients]) {
      expect(locale.form.requireMfa).toBeTruthy()
      expect(locale.form.requireMfaToggle).toBeTruthy()
      expect(locale.form.requireMfaHelp).toBeTruthy()
    }
  })
})
