import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import MfaSetupApp from './MfaSetupApp.vue'
import { useWebAuthn } from '../composables/useWebAuthn'
import enMfa from '../i18n/locales/en-US/mfa.json'
import zhMfa from '../i18n/locales/zh-TW/mfa.json'

const i18nState = vi.hoisted(() => ({ locale: 'en-US' }))
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key, params) => key === 'mfa.authenticatorOptional'
      ? (i18nState.locale === 'zh-TW' ? zhMfa : enMfa).authenticatorOptional
      : params?.seconds === undefined ? key : `${key}:${params.seconds}`
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
    i18nState.locale = 'en-US'
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

  it.each(['totp', 'email', 'passkey'])('shows first %s account recovery codes and waits for save acknowledgement before returning', async method => {
    vi.useFakeTimers()
    const setHref = vi.spyOn(window.location, 'href', 'set').mockImplementation(() => {})
    document.getElementById('mfa-setup-app').dataset.returnUrl = '/Account/Profile'
    const registration = vi.fn().mockResolvedValue({ success: true, accountRecoveryCodes: ['setup-code-a', 'setup-code-b'] })
    vi.mocked(useWebAuthn).mockReturnValue({ registerPasskey: registration })
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : { enablePasskey: true, success: true, hasPendingCode: true, accountRecoveryCodes: ['setup-code-a', 'setup-code-b'] })))
    const wrapper = mount(MfaSetupApp)
    try {
      await flushPromises()
      if (method === 'totp') {
        wrapper.vm.totpCode = '123456'
        await wrapper.vm.verifyTotp()
      } else if (method === 'email') {
        wrapper.vm.startEmailMfaSetup()
        await wrapper.vm.sendEmailMfaCode()
        await flushPromises()
        await wrapper.get('#setup-email-mfa-code').setValue('123456')
        await wrapper.vm.verifyEmailMfa()
      } else await wrapper.vm.registerPasskey()
      await flushPromises()
      expect(wrapper.findAll('[data-testid="account-recovery-disclosure"]')).toHaveLength(1)
      expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toContain('setup-code-a')
      expect(fetch.mock.calls.some(([url]) => url === '/api/account/recovery-codes')).toBe(false)
      await vi.advanceTimersByTimeAsync(1000)
      expect(setHref).not.toHaveBeenCalled()
      expect(wrapper.get('[data-testid="account-recovery-done"]').element.disabled).toBe(true)
      await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true)
      await wrapper.get('[data-testid="account-recovery-done"]').trigger('click')
      expect(setHref).toHaveBeenCalledExactlyOnceWith('/Account/Profile')
      expect(wrapper.find('[data-testid="account-recovery-disclosure"]').exists()).toBe(false)
    } finally {
      setHref.mockRestore()
      wrapper.unmount()
      vi.useRealTimers()
    }
  })

  it.each(['management', 'email', 'totp', 'passkey'])('returns from %s setup without posting the initial enrollment Skip handler', async state => {
    const mountEl = document.getElementById('mfa-setup-app')
    mountEl.dataset.returnUrl = '/Account/Profile'
    mountEl.dataset.isManagement = String(state === 'management')
    fetch.mockImplementation(endpoint => Promise.resolve(jsonResponse(endpoint.endsWith('/passkeys')
      ? state === 'passkey' ? [{ id: 1 }] : []
      : { emailMfaEnabled: state === 'email', twoFactorEnabled: state === 'totp' })))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.get('[data-testid="mfa-return"]').attributes('href')).toBe('/Account/Profile')
    expect(wrapper.find('form[action*="handler=Skip"]').exists()).toBe(false)
    expect(fetch.mock.calls.every(([, options]) => !options?.method || options.method !== 'POST')).toBe(true)
    wrapper.unmount()
  })

  it.each([false, true])('retains the initial enrollment Skip/grace boundary without a full management session (%s)', async expired => {
    document.getElementById('mfa-setup-app').dataset.gracePeriodExpired = String(expired)
    fetch.mockImplementation(endpoint => Promise.resolve(jsonResponse(endpoint.endsWith('/passkeys') ? [] : {})))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.find('[data-testid="mfa-return"]').exists()).toBe(false)
    expect(wrapper.find('form[action*="handler=Skip"]').exists()).toBe(!expired)
    wrapper.unmount()
  })

  it('does not offer a management exit for an unfulfilled mandatory MFA request', async () => {
    const mountEl = document.getElementById('mfa-setup-app')
    mountEl.dataset.isManagement = 'true'
    mountEl.dataset.isMfaEnforced = 'true'
    mountEl.dataset.gracePeriodExpired = 'true'
    fetch.mockImplementation(endpoint => Promise.resolve(jsonResponse(endpoint.endsWith('/passkeys') ? [] : {})))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.find('[data-testid="mfa-return"]').exists()).toBe(false)
    expect(wrapper.find('form[action*="handler=Skip"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it.each(['totp', 'email', 'passkey'].flatMap(method =>
    ['/', '/connect/authorize?request_uri=urn:ietf:params:oauth:request_uri:test'].map(url => [method, url])
  ))('uses the server-normalized return for %s completion and skip (%s)', async (method, url) => {
    vi.useFakeTimers()
    const originalWindow = window
    const location = { href: '' }
    document.getElementById('mfa-setup-app').dataset.returnUrl = url
    fetch.mockImplementation(endpoint => Promise.resolve(jsonResponse(
      endpoint.endsWith('/passkeys') ? [] : { success: true, hasPendingCode: true, recoveryCodes: ['synthetic-code'] }
    )))
    const wrapper = mount(MfaSetupApp)
    try {
      await flushPromises()
      expect(wrapper.get('input[name="ReturnUrl"]').element.value).toBe(url)
      if (method === 'totp') {
        wrapper.vm.totpCode = '123456'
        await wrapper.vm.verifyTotp()
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

  it.each(['setup', 'verify'])('offers explicit identity renewal for expired TOTP %s without automatic requests', async (stage) => {
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      emailMfaEnabled: true, twoFactorEnabled: false, enableTotpMfa: true,
      sharedKey: 'fixture-key', qrCodeDataUri: 'fixture-qr'
    })))
    const wrapper = mount(MfaSetupApp)
    const assign = vi.spyOn(window.location, 'assign').mockImplementation(() => {})
    try {
      await flushPromises()
      if (stage === 'verify') {
        await wrapper.vm.startTotpSetup()
        wrapper.vm.totpCode = '123456'
      }
      fetch.mockResolvedValueOnce({ ...jsonResponse({ error: 'freshAuthenticationRequired' }, false), status: 403 })
      await (stage === 'setup' ? wrapper.vm.startTotpSetup() : wrapper.vm.verifyTotp())
      await flushPromises()
      expect(wrapper.find('.modal-overlay').exists()).toBe(false)
      expect(wrapper.vm.totpCode).toBe('')
      expect(wrapper.vm.totpSetup).toEqual({ sharedKey: '', qrCodeDataUri: '' })
      expect(wrapper.get('[role="alert"]').text()).toBe('mfa.setupIdentityExpired')
      expect(wrapper.get('[data-testid="totp-reauthentication-retry"] h3').text()).toBe('mfa.addAuthenticator')
      expect(wrapper.get('[data-testid="totp-reauthentication-retry"] p').text()).toBe('mfa.reauthenticationDescription')
      expect(fetch.mock.calls.some(([url]) => url.includes('/reauthenticate'))).toBe(false)
      const beforeCancel = fetch.mock.calls.length
      await wrapper.get('[data-testid="totp-reauthentication-cancel"]').trigger('click')
      expect(fetch).toHaveBeenCalledTimes(beforeCancel)
      expect(wrapper.find('[data-testid="totp-reauthentication-retry"]').exists()).toBe(false)
      expect(assign).not.toHaveBeenCalled()

      fetch.mockResolvedValueOnce({ ...jsonResponse({ error: 'freshAuthenticationRequired' }, false), status: 403 })
      await wrapper.vm.startTotpSetup()
      await flushPromises()
      let release
      fetch.mockImplementationOnce(() => new Promise(resolve => { release = resolve }))
      await wrapper.get('[data-testid="totp-reauthentication-continue"]').trigger('click')
      await wrapper.vm.reauthenticateTotpSetup()
      wrapper.vm.cancelTotpReauthentication()
      expect(wrapper.get('[data-testid="totp-reauthentication-cancel"]').element.disabled).toBe(true)
      expect(fetch.mock.calls.filter(([url]) => url.includes('/reauthenticate'))).toEqual([
        ['/api/account/mfa/reauthenticate?action=totp-add', {
          method: 'POST', headers: { 'X-XSRF-TOKEN': 'test-csrf' }, credentials: 'include'
        }]
      ])
      release(jsonResponse({ loginUrl: '/Account/Login?reauthenticate=true' }))
      await flushPromises()
      expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?reauthenticate=true')
    } finally {
      assign.mockRestore()
      wrapper.unmount()
    }
  })

  it('offers a fixed sign-in path when renewal is denied without changing enrollment authority', async () => {
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      emailMfaEnabled: true, twoFactorEnabled: false, enableTotpMfa: true
    })))
    const wrapper = mount(MfaSetupApp)
    try {
      await flushPromises()
      fetch.mockResolvedValueOnce({ ...jsonResponse({ error: 'freshAuthenticationRequired' }, false), status: 403 })
      await wrapper.vm.startTotpSetup()
      fetch.mockResolvedValueOnce({ ok: false, status: 401, json: () => Promise.reject(new Error('No JSON body')) })
      await wrapper.vm.reauthenticateTotpSetup()
      await flushPromises()
      expect(wrapper.get('[role="alert"]').text()).toBe('mfa.setupSignInRequired')
      expect(wrapper.get('[data-testid="totp-reauthentication-retry"] h3').text()).toBe('mfa.addAuthenticator')
      expect(wrapper.get('[data-testid="totp-sign-in"]').attributes('href')).toBe('/Account/Login?returnUrl=%2FAccount%2FProfile')
      expect(wrapper.find('[data-testid="totp-reauthentication-continue"]').exists()).toBe(false)
      expect(wrapper.vm.totpSetup.sharedKey).toBe('')
      expect(wrapper.find('.modal-overlay').exists()).toBe(false)
      const beforeRetry = fetch.mock.calls.length
      await wrapper.vm.reauthenticateTotpSetup()
      await wrapper.get('[data-testid="totp-reauthentication-cancel"]').trigger('click')
      expect(fetch).toHaveBeenCalledTimes(beforeRetry)
      expect(wrapper.find('[data-testid="totp-sign-in"]').exists()).toBe(false)
    } finally { wrapper.unmount() }
  })

  it('has identity-expiry and sign-in recovery copy in both supported locales', () => {
    for (const locale of [enMfa, zhMfa]) {
      expect(locale.setupIdentityExpired).toEqual(expect.any(String))
      expect(locale.setupSignInRequired).toEqual(expect.any(String))
      expect(locale.setupIdentityExpired.length).toBeGreaterThan(0)
      expect(locale.setupSignInRequired.length).toBeGreaterThan(0)
    }
  })

  it.each(['en-US', 'zh-TW'])('shows that the authenticator is not enabled when Email MFA is already enabled in %s', async locale => {
    i18nState.locale = locale
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      emailMfaEnabled: true, twoFactorEnabled: false, enableTotpMfa: true
    })))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.get('.option-info h3').text()).toBe('mfa.authenticatorTitle')
    expect(wrapper.get('.option-info p').text()).toContain(
      locale === 'zh-TW' ? '驗證器 App 尚未啟用' : 'Authenticator app is not enabled')
    expect(wrapper.get('[data-testid="totp-add"]').text()).toBe('mfa.addAuthenticator')
    expect(wrapper.get('.title').text()).toBe('mfa.manageMethodsTitle')
    expect(wrapper.get('.info-box h3').text()).toBe('mfa.optionalSetupTitle')
    expect(wrapper.get('.info-box p').text()).toBe('mfa.additionalMethodDescription')
    expect(wrapper.text()).not.toContain('mfa.whyRequiredDescription')
    wrapper.unmount()
  })

  it('resumes the selected TOTP setup once without another Add click or duplicate seed request', async () => {
    const mountEl = document.getElementById('mfa-setup-app')
    mountEl.dataset.resumeMethod = 'totp'
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      emailMfaEnabled: true, twoFactorEnabled: false, enableTotpMfa: true,
      sharedKey: 'fixture-key', qrCodeDataUri: 'fixture-qr'
    })))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.get('.modal-content h2').text()).toBe('mfa.setupTitle')
    expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa-setup/totp/setup')).toHaveLength(1)
    expect(mountEl.dataset.resumeMethod).toBeUndefined()
    window.dispatchEvent(new Event('pageshow'))
    await wrapper.vm.startTotpSetup()
    expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa-setup/totp/setup')).toHaveLength(1)
    wrapper.unmount()
    const reloaded = mount(MfaSetupApp)
    await flushPromises()
    expect(reloaded.find('.modal-content').exists()).toBe(false)
    expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa-setup/totp/setup')).toHaveLength(1)
    reloaded.unmount()
  })

  it.each([
    ['email', true, false], ['totp', false, false], ['totp', true, true]
  ])('does not auto-start unavailable or unselected TOTP (%s, %s, %s)', async (method, enabled, enrolled) => {
    document.getElementById('mfa-setup-app').dataset.resumeMethod = method
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      enableTotpMfa: enabled, twoFactorEnabled: enrolled
    })))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(fetch.mock.calls.some(([url]) => url === '/api/account/mfa-setup/totp/setup')).toBe(false)
    wrapper.unmount()
  })

  it.each(['isMfaEnforced', 'showGracePeriod'])('keeps required context when server flag %s is active despite existing Email MFA', async (flag) => {
    document.getElementById('mfa-setup-app').dataset[flag] = 'true'
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {
      emailMfaEnabled: true, twoFactorEnabled: false, enableTotpMfa: true
    })))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    expect(wrapper.get('.title').text()).toBe('mfa.setupPageTitle')
    expect(wrapper.get('.info-box h3').text()).toBe('mfa.whyRequiredTitle')
    expect(wrapper.get('.info-box p').text()).toBe('mfa.whyRequiredDescription')
    expect(wrapper.text()).not.toContain('mfa.additionalMethodDescription')
    wrapper.unmount()
  })

  it.each([true, false, undefined])('uses authoritative pending Email state during send cooldown (%s)', async (hasPendingCode) => {
    vi.useFakeTimers()
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {})))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    wrapper.vm.startEmailMfaSetup()
    await flushPromises()
    fetch.mockResolvedValue(jsonResponse({ remainingSeconds: 12, hasPendingCode }, false))
    await wrapper.vm.sendEmailMfaCode()
    const input = wrapper.get('#setup-email-mfa-code')
    input.element.value = '123456'
    await input.trigger('change')
    expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(hasPendingCode !== true)
    expect(wrapper.get('[data-testid="email-mfa-send"]').element.disabled).toBe(true)
    expect(wrapper.get('[data-testid="email-mfa-code-guidance"]').text()).toBe(hasPendingCode === true ? 'mfa.emailCodeHelp' : 'mfa.emailCodeUnavailable')
    await vi.advanceTimersByTimeAsync(12000)
    expect(wrapper.get('[data-testid="email-mfa-send"]').element.disabled).toBe(false)
    if (hasPendingCode !== true) expect(wrapper.get('[data-testid="email-mfa-code-guidance"]').text()).toBe('mfa.emailCodeNeeded')
    wrapper.unmount()
    vi.useRealTimers()
  })

  it.each([true, false])('keeps wrong-code retry only when Email challenge remains active (%s)', async (hasPendingCode) => {
    vi.useFakeTimers()
    fetch.mockImplementation(url => Promise.resolve(jsonResponse(url.endsWith('/passkeys') ? [] : {})))
    const wrapper = mount(MfaSetupApp)
    await flushPromises()
    wrapper.vm.startEmailMfaSetup()
    await flushPromises()
    fetch.mockResolvedValueOnce(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }))
    await wrapper.vm.sendEmailMfaCode()
    const input = wrapper.get('#setup-email-mfa-code')
    input.element.value = '12 3456'
    await input.trigger('focus')
    expect(input.element.value).toBe('123456')
    input.element.value = '654321'
    window.dispatchEvent(new Event('pageshow'))
    await flushPromises()
    expect(wrapper.vm.emailCode).toBe('654321')
    let release
    fetch.mockImplementation(() => new Promise(resolve => { release = resolve }))
    const request = wrapper.vm.verifyEmailMfa()
    await wrapper.vm.verifyEmailMfa()
    wrapper.vm.closeEmailMfaSetup()
    window.dispatchEvent(new Event('pageshow'))
    await flushPromises()
    expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(true)
    expect(wrapper.get('#setup-email-mfa-code').element.disabled).toBe(true)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/email/verify'))).toHaveLength(1)
    release(jsonResponse({ error: 'invalidOrExpiredCode', hasPendingCode }, false))
    await request
    await flushPromises()
    expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(!hasPendingCode)
    expect(wrapper.get('[data-testid="email-mfa-code-guidance"]').text()).toBe(hasPendingCode ? 'mfa.emailCodeHelp' : 'mfa.emailCodeUnavailable')
    wrapper.unmount()
    vi.useRealTimers()
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
        return Promise.resolve(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }))
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
        return Promise.resolve(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }))
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
