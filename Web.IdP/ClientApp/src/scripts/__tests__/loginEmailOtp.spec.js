import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

describe('login email OTP sender', () => {
  afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); })

  function loadSender(sent = false) {
    document.body.innerHTML = `<form data-email-code-sent="${sent}">
      <input name="__RequestVerificationToken" value="antiforgery-token">
      <input name="Input.EmailCode">
      <button type="submit" data-test-id="mfa-submit" disabled>Verify</button>
      <button type="button" id="sendEmailCodeBtn" data-send-code-url="/Account/LoginEmailOtp?handler=SendCode"
        data-text-send-code="Send" data-text-resend="Resend" data-text-email-code-sent="Sent"
        data-text-please-wait="Wait" data-text-sending="Sending" data-text-send-failed="Failed">
        <span class="btn-text">Send</span><span class="countdown hidden"></span>
      </button><p id="emailCodeSentMsg" class="hidden"></p></form>`
    window.eval(readFileSync(resolve(process.cwd(), '..', 'wwwroot', 'js', 'login-email-otp.js'), 'utf8'))
    document.dispatchEvent(new Event('DOMContentLoaded'))
    return { input: document.querySelector('[name="Input.EmailCode"]'), verify: document.querySelector('[data-test-id="mfa-submit"]'), send: document.getElementById('sendEmailCodeBtn') }
  }

  function enterCode(input, value) {
    input.value = value
    input.dispatchEvent(new Event('input'))
  }

  it('requires a delivered challenge and six digits, then keeps verification locked during submission', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ success: true, remainingSeconds: 60 }) }))
    const { input, verify, send } = loadSender()
    enterCode(input, '123456')
    expect(verify.disabled).toBe(true)
    send.click()
    await vi.waitFor(() => expect(verify.disabled).toBe(false))
    expect(send.disabled).toBe(true)
    enterCode(input, '12abc')
    expect(input.value).toBe('12')
    expect(verify.disabled).toBe(true)
    document.querySelector('form').dataset.submitting = 'true'
    enterCode(input, '123456')
    expect(verify.disabled).toBe(true)
    vi.clearAllTimers()
  })

  it('keeps an existing challenge usable after a validation error or resend cooldown', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: async () => ({ remainingSeconds: 25 }) }))
    const { input, verify, send } = loadSender(true)
    enterCode(input, '123456')
    expect(verify.disabled).toBe(false)
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Wait'))
    expect(verify.disabled).toBe(false)
    expect(send.disabled).toBe(true)
    vi.clearAllTimers()
  })

  it('uses the configured antiforgery header and exposes delivery failures', async () => {
    document.body.innerHTML = `
      <form>
        <input name="__RequestVerificationToken" value="antiforgery-token">
        <button
          id="sendEmailCodeBtn"
          data-text-send-code="Send code"
          data-text-resend="Resend"
          data-text-email-code-sent="Code sent"
          data-text-please-wait="Please wait"
          data-text-sending="Sending..."
          data-text-send-failed="Unable to send code"
          data-send-code-url="/Account/LoginEmailOtp?handler=SendCode">
          <span class="btn-text">Send code</span>
          <span class="countdown hidden"></span>
        </button>
        <p id="emailCodeSentMsg" class="text-green-600 hidden" role="status"></p>
      </form>`

    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: vi.fn().mockResolvedValue({})
    })
    vi.stubGlobal('fetch', fetchMock)

    const scriptPath = resolve(process.cwd(), '..', 'wwwroot', 'js', 'login-email-otp.js')
    window.eval(readFileSync(scriptPath, 'utf8'))
    document.dispatchEvent(new Event('DOMContentLoaded'))

    document.getElementById('sendEmailCodeBtn').click()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    await vi.waitFor(() => {
      expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Unable to send code')
    })

    const [, request] = fetchMock.mock.calls[0]
    expect(request.headers['X-XSRF-TOKEN']).toBe('antiforgery-token')
    expect(request.headers.RequestVerificationToken).toBeUndefined()
    expect(document.getElementById('emailCodeSentMsg').classList.contains('hidden')).toBe(false)
    expect(document.getElementById('emailCodeSentMsg').getAttribute('role')).toBe('alert')
    expect(document.getElementById('sendEmailCodeBtn').disabled).toBe(false)
  })
})
