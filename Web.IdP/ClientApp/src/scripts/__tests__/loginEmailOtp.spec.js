import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

describe('login email OTP sender', () => {
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
