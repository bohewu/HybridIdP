import { beforeEach, describe, expect, it, vi } from 'vitest'

const authenticateWithPasskey = vi.fn()

vi.mock('../composables/useWebAuthn.js', () => ({
  useWebAuthn: () => ({
    authenticateWithPasskey,
    isSupported: () => true
  })
}))

describe('passkey MFA step-up', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = `
      <button
        id="passkeyLoginBtn"
        data-username="passkey-user@example.test"
        data-return-url="/connect/authorize?client_id=testclient-public">
      </button>
    `
  })

  it('uses the authenticated account when the login input is not present', async () => {
    authenticateWithPasskey.mockResolvedValue({ success: false })
    const { initPasskeyLogin } = await import('./razor.js')

    await initPasskeyLogin()
    document.getElementById('passkeyLoginBtn').click()
    await vi.waitFor(() => {
      expect(authenticateWithPasskey).toHaveBeenCalledWith('passkey-user@example.test')
    })
  })

  it('rejects a backslash-based external return URL', async () => {
    const { getSafePasskeyReturnUrl } = await import('./razor.js')

    expect(getSafePasskeyReturnUrl('/\\evil.example')).toBe('/')
    expect(getSafePasskeyReturnUrl('/connect/authorize?client_id=testclient-public'))
      .toBe('/connect/authorize?client_id=testclient-public')
  })
})

describe('credential migration resend cooldown', () => {
  it('keeps resend disabled until the server cooldown reaches zero', async () => {
    vi.useFakeTimers()
    document.body.innerHTML = `
      <button
        data-credential-migration-resend
        data-retry-after="2"
        data-ready-label="Resend code"
        data-cooldown-label="Resend in {seconds}s">
      </button>
    `
    const { initCredentialMigrationCooldown } = await import('./razor.js')

    const timer = initCredentialMigrationCooldown()
    const button = document.querySelector('[data-credential-migration-resend]')
    expect(button.disabled).toBe(true)
    expect(button.textContent).toBe('Resend in 2s')

    await vi.advanceTimersByTimeAsync(2000)
    expect(button.disabled).toBe(false)
    expect(button.textContent).toBe('Resend code')
    window.clearInterval(timer)
    vi.useRealTimers()
  })
})

describe('native recovery form', () => {
  it('normalizes the OTP and prevents duplicate form submission', async () => {
    document.body.innerHTML = `
      <main data-native-recovery>
        <form data-native-recovery-form>
          <input data-native-recovery-code data-native-recovery-autofocus>
          <button type="submit">Verify</button>
        </form>
      </main>
    `
    const { initNativeRecoveryForm } = await import('./razor.js')

    initNativeRecoveryForm()
    const input = document.querySelector('[data-native-recovery-code]')
    input.value = '1a2 34567'
    input.dispatchEvent(new Event('input'))
    expect(input.value).toBe('123456')
    expect(document.activeElement).toBe(input)

    const form = document.querySelector('[data-native-recovery-form]')
    form.dispatchEvent(new Event('submit'))
    expect(form.querySelector('button').disabled).toBe(true)
  })
})
