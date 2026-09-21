import { initPendingDirectorySettlement } from '../pendingDirectorySettlement.js'

function renderPage() {
  document.body.innerHTML = `
    <main data-pending-directory-settlement
      data-claim-endpoint="/api/account/pending-directory-settlement/claim"
      data-verify-endpoint="/api/account/pending-directory-settlement/verify"
      data-cancel-endpoint="/api/account/pending-directory-settlement/cancel"
      data-message-unavailable="Unavailable"
      data-message-invalid-code="Enter six digits"
      data-message-resolved="Resolved"
      data-message-cancelled="Cancelled">
      <div hidden tabindex="-1" data-pending-settlement-message></div>
      <section data-pending-settlement-claim-step>
        <form data-pending-settlement-claim-form>
          <input name="__RequestVerificationToken" value="csrf-token">
          <input data-pending-settlement-continuation>
          <button type="submit">Continue</button>
        </form>
      </section>
      <section hidden data-pending-settlement-verify-step>
        <form data-pending-settlement-verify-form>
          <input data-pending-settlement-code>
          <input data-pending-settlement-credential>
          <button type="button" data-pending-settlement-cancel>Cancel</button>
          <button type="submit">Verify</button>
        </form>
      </section>
      <section hidden data-pending-settlement-result-step>
        <div tabindex="-1" data-pending-settlement-result></div>
      </section>
    </main>`
}

function response(outcome, ok = true) {
  return { ok, json: vi.fn().mockResolvedValue({ outcome }) }
}

async function submit(element) {
  element.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await vi.waitFor(() => expect(document.querySelector('[aria-busy="true"]')).toBeNull())
}

describe('pending directory settlement page', () => {
  afterEach(() => {
    window.dispatchEvent(new Event('pagehide'))
    window.history.replaceState(null, '', '/')
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('claims the form-entered continuation with the standard antiforgery header', async () => {
    renderPage()
    const fetchMock = vi.fn().mockResolvedValue(response('verificationRequired'))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    const continuation = document.querySelector('[data-pending-settlement-continuation]')
    continuation.value = 'opaque-continuation'

    await submit(document.querySelector('[data-pending-settlement-claim-form]'))

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/account/pending-directory-settlement/claim')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': 'csrf-token'
      },
      body: JSON.stringify({ continuation: 'opaque-continuation' })
    })
    expect(continuation.value).toBe('')
    expect(document.querySelector('[data-pending-settlement-verify-step]').hidden).toBe(false)
    expect(document.activeElement).toBe(document.querySelector('[data-pending-settlement-code]'))
  })

  it('sends only the six-digit code and user-entered current credential to verification', async () => {
    renderPage()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response('verificationRequired'))
      .mockResolvedValueOnce(response('resolved'))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    document.querySelector('[data-pending-settlement-continuation]').value = 'opaque-continuation'
    await submit(document.querySelector('[data-pending-settlement-claim-form]'))
    document.querySelector('[data-pending-settlement-code]').value = '123456'
    document.querySelector('[data-pending-settlement-credential]').value = 'user-current-credential'

    await submit(document.querySelector('[data-pending-settlement-verify-form]'))

    const [endpoint, request] = fetchMock.mock.calls[1]
    expect(endpoint).toBe('/api/account/pending-directory-settlement/verify')
    expect(JSON.parse(request.body)).toEqual({
      ownershipCode: '123456',
      currentCredential: 'user-current-credential'
    })
    expect(request.body).not.toContain('continuation')
    expect(request.body).not.toContain('preparation')
    expect(document.querySelector('[data-pending-settlement-code]').value).toBe('')
    expect(document.querySelector('[data-pending-settlement-credential]').value).toBe('')
    expect(document.querySelector('[data-pending-settlement-result]').textContent).toBe('Resolved')
  })

  it('explicitly cancels a pending verification and clears every secret field', async () => {
    renderPage()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response('verificationRequired'))
      .mockResolvedValueOnce(response('cancelled'))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    document.querySelector('[data-pending-settlement-continuation]').value = 'opaque-continuation'
    await submit(document.querySelector('[data-pending-settlement-claim-form]'))
    document.querySelector('[data-pending-settlement-code]').value = '123456'
    document.querySelector('[data-pending-settlement-credential]').value = 'secret'

    document.querySelector('[data-pending-settlement-cancel]').click()
    await vi.waitFor(() => expect(document.querySelector('[aria-busy="true"]')).toBeNull())

    expect(fetchMock.mock.calls[1][0]).toBe('/api/account/pending-directory-settlement/cancel')
    expect(fetchMock.mock.calls[1][1].body).toBe('{}')
    expect(document.querySelector('[data-pending-settlement-code]').value).toBe('')
    expect(document.querySelector('[data-pending-settlement-credential]').value).toBe('')
    expect(document.querySelector('[data-pending-settlement-result]').textContent).toBe('Cancelled')
  })

  it('clears denied state and ignores an older out-of-order response', async () => {
    renderPage()
    let resolveFirst
    const firstResponse = new Promise(resolve => { resolveFirst = resolve })
    const fetchMock = vi.fn()
      .mockReturnValueOnce(firstResponse)
      .mockResolvedValueOnce(response('verificationRequired'))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    const form = document.querySelector('[data-pending-settlement-claim-form]')
    const continuation = document.querySelector('[data-pending-settlement-continuation]')
    continuation.value = 'older-continuation'
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    continuation.value = 'current-continuation'
    await submit(form)

    resolveFirst(response('unavailable', false))
    await Promise.resolve()
    await Promise.resolve()

    expect(document.querySelector('[data-pending-settlement-verify-step]').hidden).toBe(false)
    expect(document.querySelector('[data-pending-settlement-message]').hidden).toBe(true)
    expect(continuation.value).toBe('')
  })

  it.each(['resolve', 'reject'])(
    'ignores a verification body that finishes after cancellation by %s',
    async completion => {
      renderPage()
      let resolveBody
      let rejectBody
      const pendingBody = new Promise((resolve, reject) => {
        resolveBody = resolve
        rejectBody = reject
      })
      const bodyReader = vi.fn(() => pendingBody)
      const fetchMock = vi.fn()
        .mockResolvedValueOnce(response('verificationRequired'))
        .mockResolvedValueOnce({ ok: true, json: bodyReader })
        .mockResolvedValueOnce(response('cancelled'))
      initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
      document.querySelector('[data-pending-settlement-continuation]').value = 'opaque-continuation'
      await submit(document.querySelector('[data-pending-settlement-claim-form]'))
      document.querySelector('[data-pending-settlement-code]').value = '123456'
      document.querySelector('[data-pending-settlement-credential]').value = 'user-current-credential'
      document.querySelector('[data-pending-settlement-verify-form]')
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
      await vi.waitFor(() => expect(bodyReader).toHaveBeenCalledOnce())

      document.querySelector('[data-pending-settlement-cancel]').click()
      await vi.waitFor(() => {
        expect(document.querySelector('[data-pending-settlement-result]').textContent).toBe('Cancelled')
      })

      if (completion === 'resolve') resolveBody({ outcome: 'resolved' })
      else rejectBody(new Error('body read failed'))
      await Promise.resolve()
      await Promise.resolve()

      expect(document.querySelector('[data-pending-settlement-result-step]').hidden).toBe(false)
      expect(document.querySelector('[data-pending-settlement-result]').textContent).toBe('Cancelled')
      expect(document.querySelector('[data-pending-settlement-message]').hidden).toBe(true)
    }
  )

  it.each([
    ['denied', 'unavailable', false],
    ['unknown', 'unexpected-outcome', true]
  ])('clears all fields when verification is %s', async (_label, outcome, ok) => {
    renderPage()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response('verificationRequired'))
      .mockResolvedValueOnce(response(outcome, ok))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    document.querySelector('[data-pending-settlement-continuation]').value = 'opaque-continuation'
    await submit(document.querySelector('[data-pending-settlement-claim-form]'))
    const code = document.querySelector('[data-pending-settlement-code]')
    const credential = document.querySelector('[data-pending-settlement-credential]')
    code.value = '123456'
    credential.value = 'user-current-credential'

    await submit(document.querySelector('[data-pending-settlement-verify-form]'))

    expect(document.querySelector('[data-pending-settlement-claim-step]').hidden).toBe(false)
    expect(document.querySelector('[data-pending-settlement-verify-step]').hidden).toBe(true)
    expect(document.querySelector('[data-pending-settlement-continuation]').value).toBe('')
    expect(code.value).toBe('')
    expect(credential.value).toBe('')
    expect(document.querySelector('[data-pending-settlement-message]').textContent).toBe('Unavailable')
  })

  it('removes query and fragment input and clears fields when navigating away', () => {
    renderPage()
    window.history.replaceState(null, '', '/Account/PendingDirectorySettlement?continuation=secret#123456')
    initPendingDirectorySettlement(document, { fetchImpl: vi.fn() })
    const continuation = document.querySelector('[data-pending-settlement-continuation]')
    const credential = document.querySelector('[data-pending-settlement-credential]')
    continuation.value = 'secret'
    credential.value = 'credential'

    window.dispatchEvent(new Event('pagehide'))

    expect(window.location.pathname).toBe('/Account/PendingDirectorySettlement')
    expect(window.location.search).toBe('')
    expect(window.location.hash).toBe('')
    expect(continuation.value).toBe('')
    expect(credential.value).toBe('')
  })

  it('restores enabled claim controls when a persisted page returns from navigation', async () => {
    renderPage()
    const fetchMock = vi.fn((_endpoint, request) => new Promise((_resolve, reject) => {
      request.signal.addEventListener('abort', () => {
        const error = new Error('aborted')
        error.name = 'AbortError'
        reject(error)
      }, { once: true })
    }))
    initPendingDirectorySettlement(document, { fetchImpl: fetchMock })
    const form = document.querySelector('[data-pending-settlement-claim-form]')
    const continuation = document.querySelector('[data-pending-settlement-continuation]')
    const continueButton = form.querySelector('button')
    continuation.value = 'opaque-continuation'

    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    expect(continueButton.disabled).toBe(true)
    expect(continueButton.getAttribute('aria-busy')).toBe('true')

    window.dispatchEvent(new Event('pagehide'))
    const pageShow = new Event('pageshow')
    Object.defineProperty(pageShow, 'persisted', { value: true })
    window.dispatchEvent(pageShow)

    expect(continueButton.disabled).toBe(false)
    expect(continueButton.hasAttribute('aria-busy')).toBe(false)
    expect(document.querySelector('[data-pending-settlement-claim-step]').hidden).toBe(false)
    expect(continuation.value).toBe('')
    expect(document.activeElement).toBe(continuation)
  })
})
