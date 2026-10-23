import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

describe('login email OTP sender', () => {
  let documentListeners
  let windowListeners
  beforeEach(() => {
    documentListeners = vi.spyOn(document, 'addEventListener')
    windowListeners = vi.spyOn(window, 'addEventListener')
  })
  afterEach(() => {
    for (const args of documentListeners.mock.calls) document.removeEventListener(...args)
    for (const args of windowListeners.mock.calls) window.removeEventListener(...args)
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  function loadSender(sent = false, remainingSeconds = 0) {
    document.body.innerHTML = `<form data-email-code-sent="${sent}">
      <input name="__RequestVerificationToken" value="antiforgery-token">
      <input name="Input.EmailCode">
      <button type="submit" data-test-id="mfa-submit" disabled>Verify</button>
      <button type="button" id="sendEmailCodeBtn" data-send-code-url="/Account/LoginEmailOtp?handler=SendCode"
        data-remaining-seconds="${remainingSeconds}"
        data-text-send-code="Send" data-text-resend="Resend" data-text-email-code-sent="Sent"
        data-text-please-wait="Wait" data-text-sending="Sending" data-text-send-failed="Failed"
        data-text-code-pending="Active code" data-text-code-needed="Request a new code"
        data-text-code-unavailable="Wait, then request a new code">
        <span class="btn-text">Send</span><span class="countdown hidden"></span>
      </button><p id="emailCodeSentMsg" class="hidden"></p><p id="emailCodeGuidance" role="status"></p></form>`
    window.eval(readFileSync(resolve(process.cwd(), '..', 'wwwroot', 'js', 'login-email-otp.js'), 'utf8'))
    document.dispatchEvent(new Event('DOMContentLoaded'))
    return { input: document.querySelector('[name="Input.EmailCode"]'), verify: document.querySelector('[data-test-id="mfa-submit"]'), send: document.getElementById('sendEmailCodeBtn') }
  }

  function enterCode(input, value) {
    input.value = value
    input.dispatchEvent(new Event('input'))
  }

  it('shows localized retry guidance for an unconfirmed send gate without enabling verification', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, json: async () => ({
      error: 'temporarily_unavailable', message: 'Wait a few seconds, then try again.'
    }) }))
    const { input, verify, send } = loadSender(false)
    enterCode(input, '123456')
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Wait a few seconds, then try again.'))
    expect(verify.disabled).toBe(true)
    expect(send.disabled).toBe(true)
    expect(send.querySelector('.countdown').textContent).toBe('(60)')
    expect(document.getElementById('emailCodeSentMsg').getAttribute('role')).toBe('alert')
  })

  it.each([true, false].flatMap(pending => [0, 12].map(seconds => [pending, seconds])))('hydrates pending=%s and cooldown=%s without sending or conflating verification state', async (pending, seconds) => {
    vi.useFakeTimers()
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const { input, verify, send } = loadSender(pending, seconds)
    enterCode(input, '123456')
    expect(send.disabled).toBe(seconds > 0)
    expect(send.querySelector('.btn-text').textContent).toBe(pending ? 'Resend' : 'Send')
    expect(send.querySelector('.countdown').classList.contains('hidden')).toBe(seconds === 0)
    expect(verify.disabled).toBe(!pending)
    expect(document.getElementById('emailCodeGuidance').textContent).toBe(pending ? 'Active code'
      : seconds > 0 ? 'Wait, then request a new code' : 'Request a new code')
    expect(fetchMock).not.toHaveBeenCalled()
    if (seconds > 0) {
      expect(send.querySelector('.countdown').textContent).toBe('(12)')
      await vi.advanceTimersByTimeAsync(1000)
      expect(send.querySelector('.countdown').textContent).toBe('(11)')
      expect(send.disabled).toBe(true)
      await vi.advanceTimersByTimeAsync(11000)
      expect(send.disabled).toBe(false)
      expect(send.querySelector('.countdown').classList.contains('hidden')).toBe(true)
      expect(verify.disabled).toBe(!pending)
    }
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('requires a pending challenge and six digits, then keeps verification locked during submission', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ success: true, hasPendingCode: true, remainingSeconds: 60 }) }))
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
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 429, json: async () => ({ remainingSeconds: 25, hasPendingCode: true }) }))
    const { input, verify, send } = loadSender(true)
    enterCode(input, '123456')
    expect(verify.disabled).toBe(false)
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Wait'))
    expect(verify.disabled).toBe(false)
    expect(send.disabled).toBe(true)
    expect(document.getElementById('emailCodeGuidance').textContent).toBe('Active code')
    vi.clearAllTimers()
  })

  it.each([false, undefined])('does not unlock an absent or retired challenge from cooldown (%s)', async (hasPendingCode) => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 429, json: async () => ({ remainingSeconds: 12, hasPendingCode }) }))
    const { input, verify, send } = loadSender(true)
    enterCode(input, '123456')
    expect(verify.disabled).toBe(false)
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeGuidance').textContent).toBe('Wait, then request a new code'))
    expect(verify.disabled).toBe(true)
    expect(send.disabled).toBe(true)
    expect(document.querySelector('form').dataset.emailCodeSent).toBe('false')
    await vi.advanceTimersByTimeAsync(12000)
    expect(send.disabled).toBe(false)
    expect(verify.disabled).toBe(true)
    expect(document.getElementById('emailCodeGuidance').textContent).toBe('Request a new code')
    fetch.mockResolvedValue({ ok: true, json: async () => ({ success: true, hasPendingCode: true, remainingSeconds: 60 }) })
    send.click()
    await vi.waitFor(() => expect(verify.disabled).toBe(false))
    expect(send.disabled).toBe(true)
  })

  it.each(['change', 'focus', 'pageshow'])('synchronizes autofill or restored input through %s without unlocking submission', async (event) => {
    const { input, verify, send } = loadSender(true)
    input.value = '12 3456'
    if (event === 'pageshow') window.dispatchEvent(new Event(event))
    else input.dispatchEvent(new Event(event))
    expect(input.value).toBe('123456')
    expect(verify.disabled).toBe(false)
    document.querySelector('form').dataset.submitting = 'true'
    if (event === 'pageshow') window.dispatchEvent(new Event(event))
    else input.dispatchEvent(new Event(event))
    expect(verify.disabled).toBe(true)
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    send.click()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('uses the configured antiforgery header and exposes delivery failures', async () => {
    vi.useFakeTimers()
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
    expect(document.getElementById('sendEmailCodeBtn').disabled).toBe(true)
  })

  it('shows failure instead of sent or rate-limit guidance while retaining the pending code and countdown', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 503, json: async () => ({
      success: false, error: 'email_delivery_unconfirmed', remainingSeconds: 47, hasPendingCode: true,
      message: 'private-smtp-detail'
    }) }))
    const { input, verify, send } = loadSender()
    enterCode(input, '123456')
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Failed'))
    const message = document.getElementById('emailCodeSentMsg')
    expect(message.getAttribute('role')).toBe('alert')
    expect(message.classList.contains('text-green-600')).toBe(false)
    expect(send.querySelector('.countdown').textContent).toBe('(47)')
    expect(send.disabled).toBe(true)
    expect(verify.disabled).toBe(false)
    send.click()
    expect(fetch).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(47000)
    expect(send.disabled).toBe(false)
    expect(message.classList.contains('hidden')).toBe(false)
    expect(fetch).toHaveBeenCalledOnce()
    fetch.mockResolvedValue({ ok: true, status: 200, json: async () => ({ success: true, hasPendingCode: true, remainingSeconds: 60 }) })
    send.click()
    await vi.waitFor(() => expect(message.textContent).toBe('Sent'))
    expect(message.getAttribute('role')).toBe('status')
  })

  it('keeps a conservative cooldown after a lost response without automatically retrying', async () => {
    vi.useFakeTimers()
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Connection lost')))
    const { send } = loadSender()
    send.click()
    await vi.waitFor(() => expect(document.getElementById('emailCodeSentMsg').textContent).toBe('Failed'))
    expect(send.disabled).toBe(true)
    expect(send.querySelector('.countdown').textContent).toBe('(60)')
    await vi.advanceTimersByTimeAsync(59000)
    expect(send.disabled).toBe(true)
    await vi.advanceTimersByTimeAsync(1000)
    expect(send.disabled).toBe(false)
    expect(fetch).toHaveBeenCalledOnce()
  })
})
