import { mount, flushPromises } from '@vue/test-utils'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import ClientForm from '../ClientForm.vue'
import ClientScopeManager from '../ClientScopeManager.vue'
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

const mountForm = (clientOverride = client, scopeManager = true) => mount(ClientForm, {
  props: { client: clientOverride },
  global: {
    stubs: {
      BaseModal,
      ClientScopeManager: scopeManager,
      AuthUrlGenerator: true,
      SecretDisplayModal: true
    },
    mocks: {
      $t: (key) => key
    }
  }
})

describe('ClientForm.vue required scopes', () => {
  const applicationId = '69fe27cf-8cde-4f01-9ea9-f5fc434e8ba9'
  const publicClientId = 'scope-consent-client'
  const editableClient = {
    ...client,
    id: applicationId,
    clientId: publicClientId,
    permissions: [...client.permissions, 'scp:profile']
  }
  let savedRequired

  beforeEach(() => {
    savedRequired = []
    global.fetch = vi.fn(async (url, options = {}) => {
      if (url === `/api/admin/clients/${applicationId}/required-scopes`) {
        if (options.method === 'PUT') savedRequired = JSON.parse(options.body).scopes
        return { ok: true, json: async () => ({ scopes: [...savedRequired] }) }
      }
      if (url.startsWith('/api/admin/scopes?')) {
        return { ok: true, json: async () => ({ items: [{ name: 'openid' }, { name: 'profile' }], totalCount: 2 }) }
      }
      return { ok: true, json: async () => ({ id: applicationId, clientId: publicClientId }) }
    })
  })

  it('saves optional-to-required changes and reloads using the internal application ID', async () => {
    const wrapper = mountForm(editableClient, false)
    await flushPromises()

    const profileRow = wrapper.findAll('[data-test="csm-selected-item"]').find(row => row.text().includes('profile'))
    const requiredToggle = profileRow.find('input[type="checkbox"]')
    expect(requiredToggle.element.checked).toBe(false)
    expect(requiredToggle.element.disabled).toBe(false)
    await requiredToggle.setValue(true)
    await wrapper.find('[data-test-id="client-form-submit"]').trigger('click')
    await flushPromises()

    expect(savedRequired).toEqual(['profile'])
    expect(global.fetch).toHaveBeenCalledWith(`/api/admin/clients/${applicationId}/required-scopes`, expect.objectContaining({
      method: 'PUT', body: JSON.stringify({ scopes: ['profile'] })
    }))
    expect(JSON.parse(global.fetch.mock.calls.find(([url]) => url === `/api/admin/clients/${applicationId}`)[1].body).permissions).toContain('scp:profile')
    wrapper.unmount()

    const reopened = mountForm(editableClient, false)
    await flushPromises()
    const reopenedProfile = reopened.findAll('[data-test="csm-selected-item"]').find(row => row.text().includes('profile'))
    expect(reopenedProfile.find('input[type="checkbox"]').element.checked).toBe(true)
    expect(global.fetch.mock.calls.some(([url]) => url.includes(`/clients/${publicClientId}`))).toBe(false)
    reopened.unmount()
  })

  it('removes Required together with its allowed scope before saving', async () => {
    savedRequired = ['profile']
    const wrapper = mountForm(editableClient, false)
    await flushPromises()
    const profileRow = wrapper.findAll('[data-test="csm-selected-item"]').find(row => row.text().includes('profile'))
    await profileRow.find('button').trigger('click')
    await wrapper.find('[data-test-id="client-form-submit"]').trigger('click')
    await flushPromises()

    expect(savedRequired).toEqual([])
    const updateCall = global.fetch.mock.calls.find(([url]) => url === `/api/admin/clients/${applicationId}`)
    expect(JSON.parse(updateCall[1].body).permissions).not.toContain('scp:profile')
    wrapper.unmount()
  })

  it('saves new-client Required scopes using the returned internal ID', async () => {
    const wrapper = mountForm(null)
    await wrapper.find('#clientId').setValue(publicClientId)
    await wrapper.find('#redirectUris').setValue('https://example.com/callback')
    const scopeManager = wrapper.findComponent(ClientScopeManager)
    scopeManager.vm.$emit('update:modelValue', ['openid', 'profile'])
    scopeManager.vm.$emit('update:requiredScopes', ['profile'])
    await wrapper.find('[data-test-id="client-form-submit"]').trigger('click')
    await flushPromises()

    expect(savedRequired).toEqual(['profile'])
    expect(global.fetch).toHaveBeenCalledWith('/api/admin/clients', expect.objectContaining({ method: 'POST' }))
    expect(global.fetch).toHaveBeenCalledWith(`/api/admin/clients/${applicationId}/required-scopes`, expect.objectContaining({ method: 'PUT' }))
    expect(global.fetch.mock.calls.some(([url]) => url.includes(`/clients/${publicClientId}`))).toBe(false)
    wrapper.unmount()
  })

  it('shows requested-only Required help and provides both localized explanations', () => {
    const wrapper = mountForm()
    expect(wrapper.text()).toContain('clients.form.scopeManager.requiredHelp')
    expect(enUsClients.form.scopeManager.requiredHelp).toContain('when this client requests them')
    expect(enUsClients.form.scopeManager.requiredHelp).toContain('does not automatically add scopes')
    expect(zhTwClients.form.scopeManager.requiredHelp).toContain('本次有請求')
    expect(zhTwClients.form.scopeManager.requiredHelp).toContain('不會自動將範圍加入請求')
    wrapper.unmount()
  })
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
