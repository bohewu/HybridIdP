import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import MfaSetupApp from './MfaSetupApp.vue'
import { useWebAuthn } from '../composables/useWebAuthn'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key) => key
  })
}))

vi.mock('../composables/useWebAuthn', () => ({
  useWebAuthn: vi.fn()
}))

vi.stubGlobal('fetch', vi.fn())

describe('MfaSetupApp Email MFA', () => {
  const jsonResponse = (data, ok = true) => ({
    ok,
    json: () => Promise.resolve(data)
  })

  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = `
      <div
        id="mfa-setup-app"
        data-csrf-token="test-csrf"
        data-return-url="/"
      ></div>
    `
    vi.mocked(useWebAuthn).mockReturnValue({
      registerPasskey: vi.fn()
    })
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('sends and verifies an emailed code before completing partial authentication', async () => {
    fetch.mockImplementation((url) => {
      if (url === '/api/account/mfa-setup/status') {
        return Promise.resolve(jsonResponse({
          twoFactorEnabled: false,
          emailMfaEnabled: false,
          enableTotpMfa: true,
          enableEmailMfa: true,
          enablePasskey: false
        }))
      }
      if (url === '/api/account/mfa-setup/policy') {
        return Promise.resolve(jsonResponse({ requireMfaForPasskey: false }))
      }
      if (url === '/api/account/mfa-setup/passkeys') {
        return Promise.resolve(jsonResponse([]))
      }
      if (url === '/api/account/mfa-setup/email/send') {
        return Promise.resolve(jsonResponse({ success: true }))
      }
      if (url === '/api/account/mfa-setup/email/verify') {
        return Promise.resolve(jsonResponse({ success: true }))
      }
      return Promise.resolve(jsonResponse({}))
    })

    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    await wrapper.vm.startEmailMfaSetup()
    await flushPromises()

    expect(fetch).toHaveBeenCalledWith('/api/account/mfa-setup/email/send', {
      method: 'POST',
      headers: {
        'X-XSRF-TOKEN': 'test-csrf'
      },
      credentials: 'include'
    })

    await wrapper.get('#setup-email-mfa-code').setValue('123456')
    await wrapper.vm.verifyEmailMfa()
    await flushPromises()

    const verifyRequest = fetch.mock.calls.find(
      ([url]) => url === '/api/account/mfa-setup/email/verify')
    expect(verifyRequest).toBeTruthy()
    expect(verifyRequest[1].headers).toEqual({
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': 'test-csrf'
    })
    expect(JSON.parse(verifyRequest[1].body)).toEqual({ code: '123456' })
    expect(fetch.mock.calls.some(
      ([url]) => url === '/api/account/mfa-setup/email/enable')).toBe(false)
  })

  it('does not complete setup when the emailed code is invalid', async () => {
    fetch.mockImplementation((url) => {
      if (url === '/api/account/mfa-setup/status') {
        return Promise.resolve(jsonResponse({
          twoFactorEnabled: false,
          emailMfaEnabled: false,
          enableTotpMfa: true,
          enableEmailMfa: true,
          enablePasskey: false
        }))
      }
      if (url === '/api/account/mfa-setup/policy') {
        return Promise.resolve(jsonResponse({ requireMfaForPasskey: false }))
      }
      if (url === '/api/account/mfa-setup/passkeys') {
        return Promise.resolve(jsonResponse([]))
      }
      if (url === '/api/account/mfa-setup/email/send') {
        return Promise.resolve(jsonResponse({ success: true }))
      }
      if (url === '/api/account/mfa-setup/email/verify') {
        return Promise.resolve(jsonResponse({
          success: false,
          error: 'invalidOrExpiredCode'
        }))
      }
      return Promise.resolve(jsonResponse({}))
    })

    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    await wrapper.vm.startEmailMfaSetup()
    await flushPromises()
    await wrapper.get('#setup-email-mfa-code').setValue('000000')
    await wrapper.vm.verifyEmailMfa()
    await flushPromises()

    expect(wrapper.get('[role="dialog"]').exists()).toBe(true)
    expect(wrapper.get('[role="alert"]').text())
      .toBe('mfa.errors.invalidOrExpiredCode')
  })

  it('hides grace-period messaging for voluntary MFA setup', async () => {
    fetch.mockResolvedValue(jsonResponse({}))

    const wrapper = mount(MfaSetupApp)
    await flushPromises()

    expect(wrapper.find('.grace-info').exists()).toBe(false)
    expect(wrapper.find('.grace-expired').exists()).toBe(false)
  })

  it('shows a positive grace period only when mandatory enrollment is active', async () => {
    document.getElementById('mfa-setup-app').dataset.showGracePeriod = 'true'
    document.getElementById('mfa-setup-app').dataset.remainingGraceDays = '1'
    fetch.mockResolvedValue(jsonResponse({}))

    const wrapper = mount(MfaSetupApp)
    await flushPromises()

    expect(wrapper.get('.grace-info').text()).toBe('mfa.gracePeriodMessage')
    expect(wrapper.find('.grace-expired').exists()).toBe(false)
  })
})
