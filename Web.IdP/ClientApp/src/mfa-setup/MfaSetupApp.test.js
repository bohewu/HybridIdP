import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import MfaSetupApp from './MfaSetupApp.vue'
import { useWebAuthn } from '../composables/useWebAuthn'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key, params) => params?.seconds === undefined ? key : `${key}:${params.seconds}`
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

  it.each(['totp', 'email', 'passkey'].flatMap(method =>
    ['/', '/connect/authorize?request_uri=urn:ietf:params:oauth:request_uri:test'].map(url => [method, url])
  ))('uses the server-normalized return for %s completion and skip (%s)', async (method, url) => {
    vi.useFakeTimers()
    const originalWindow = window
    const location = { href: '' }
    document.getElementById('mfa-setup-app').dataset.returnUrl = url
    fetch.mockImplementation(endpoint => Promise.resolve(jsonResponse(
      endpoint.endsWith('/passkeys') ? [] : { success: true, recoveryCodes: ['synthetic-code'] }
    )))
    const wrapper = mount(MfaSetupApp)
    try {
      await flushPromises()
      expect(wrapper.get('input[name="ReturnUrl"]').element.value).toBe(url)
      if (method === 'totp') {
        wrapper.vm.totpCode = '123456'
        await wrapper.vm.verifyTotp()
        wrapper.vm.finishTotpSetup()
      } else if (method === 'email') {
        await wrapper.vm.startEmailMfaSetup()
        await wrapper.vm.sendEmailMfaCode()
        await flushPromises()
        await wrapper.get('#setup-email-mfa-code').setValue('123456')
        await wrapper.vm.verifyEmailMfa()
      } else {
        await wrapper.vm.registerPasskey()
      }
      vi.stubGlobal('window', { ...originalWindow, location })
      await vi.advanceTimersByTimeAsync(1000)
      expect(location.href).toBe(url)
    } finally {
      vi.stubGlobal('window', originalWindow)
      wrapper.unmount()
      vi.useRealTimers()
    }
  })

  it('blocks duplicate TOTP verification and cancellation while the request is pending', async () => {
    fetch.mockResolvedValue(jsonResponse({ sharedKey: 'fixture-key' }))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    await wrapper.vm.startTotpSetup()
    wrapper.vm.totpCode = '123456'
    let release
    const pending = new Promise(resolve => { release = resolve })
    fetch.mockImplementation(url => url === '/api/account/mfa-setup/totp/verify'
      ? pending : Promise.resolve(jsonResponse({})))
    const first = wrapper.vm.verifyTotp()
    await wrapper.vm.verifyTotp()
    await flushPromises()
    expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa-setup/totp/verify')).toHaveLength(1)
    expect(wrapper.get('.modal-content .btn-primary').element.disabled).toBe(true)
    wrapper.vm.cancelTotpSetup()
    expect(wrapper.find('.modal-content').exists()).toBe(true)
    release(jsonResponse({ success: false }))
    await first
    await flushPromises()
    expect(wrapper.get('.modal-content .btn-primary').element.disabled).toBe(false)
    wrapper.vm.cancelTotpSetup()
    await flushPromises()
    expect(wrapper.find('.modal-content').exists()).toBe(false)
    wrapper.unmount()
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
        return Promise.resolve(jsonResponse({ success: true, remainingSeconds: 60 }))
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

    expect(fetch.mock.calls.some(
      ([url]) => url === '/api/account/mfa-setup/email/send')).toBe(false)

    await wrapper.get('[data-testid="email-mfa-send"]').trigger('click')
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
        return Promise.resolve(jsonResponse({ success: true, remainingSeconds: 60 }))
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
    await wrapper.get('[data-testid="email-mfa-send"]').trigger('click')
    await flushPromises()
    await wrapper.get('#setup-email-mfa-code').setValue('000000')
    await wrapper.vm.verifyEmailMfa()
    await flushPromises()

    expect(wrapper.get('[role="dialog"]').exists()).toBe(true)
    expect(wrapper.get('[role="alert"]').text())
      .toBe('mfa.errors.invalidOrExpiredCode')
    wrapper.unmount()
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
