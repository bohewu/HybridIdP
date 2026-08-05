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
