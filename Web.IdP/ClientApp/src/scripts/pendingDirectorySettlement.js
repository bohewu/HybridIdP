function setHidden(element, hidden) {
  if (element) element.hidden = hidden
}

export function initPendingDirectorySettlement(root = document, options = {}) {
  const page = root.querySelector('[data-pending-directory-settlement]')
  if (!page) return null

  const fetchImpl = options.fetchImpl || globalThis.fetch
  const browserWindow = options.windowObject || globalThis.window
  const claimForm = page.querySelector('[data-pending-settlement-claim-form]')
  const verifyForm = page.querySelector('[data-pending-settlement-verify-form]')
  const claimStep = page.querySelector('[data-pending-settlement-claim-step]')
  const verifyStep = page.querySelector('[data-pending-settlement-verify-step]')
  const resultStep = page.querySelector('[data-pending-settlement-result-step]')
  const continuationInput = page.querySelector('[data-pending-settlement-continuation]')
  const codeInput = page.querySelector('[data-pending-settlement-code]')
  const credentialInput = page.querySelector('[data-pending-settlement-credential]')
  const cancelButton = page.querySelector('[data-pending-settlement-cancel]')
  const message = page.querySelector('[data-pending-settlement-message]')
  const result = page.querySelector('[data-pending-settlement-result]')
  const antiforgeryToken = page.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
  const buttons = page.querySelectorAll('button')
  let requestGeneration = 0
  let activeController = null
  let disposed = false

  if (!fetchImpl || !claimForm || !verifyForm || !continuationInput || !codeInput || !credentialInput) {
    return null
  }

  if (browserWindow?.location && (browserWindow.location.search || browserWindow.location.hash)) {
    browserWindow.history.replaceState(null, '', browserWindow.location.pathname)
  }

  function clearSecrets() {
    continuationInput.value = ''
    codeInput.value = ''
    credentialInput.value = ''
  }

  function setBusy(busy, allowCancel = false) {
    buttons.forEach(button => {
      const disabled = busy && !(allowCancel && button === cancelButton)
      button.disabled = disabled
      if (disabled) button.setAttribute('aria-busy', 'true')
      else button.removeAttribute('aria-busy')
    })
  }

  function hideMessage() {
    if (!message) return
    message.hidden = true
    message.textContent = ''
  }

  function showMessage(text) {
    if (!message) return
    message.textContent = text
    message.className = 'mb-6 rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 break-words'
    message.hidden = false
    message.focus({ preventScroll: true })
  }

  function showClaim() {
    setHidden(claimStep, false)
    setHidden(verifyStep, true)
    setHidden(resultStep, true)
  }

  function showVerification() {
    setHidden(claimStep, true)
    setHidden(verifyStep, false)
    setHidden(resultStep, true)
    codeInput.focus({ preventScroll: true })
  }

  function showResult(text) {
    hideMessage()
    setHidden(claimStep, true)
    setHidden(verifyStep, true)
    setHidden(resultStep, false)
    if (result) {
      result.textContent = text
      result.focus({ preventScroll: true })
    }
  }

  async function post(endpoint, payload, { allowCancel = false } = {}) {
    activeController?.abort()
    const controller = new AbortController()
    activeController = controller
    const generation = ++requestGeneration
    setBusy(true, allowCancel)

    try {
      const response = await fetchImpl(endpoint, {
        method: 'POST',
        credentials: 'same-origin',
        cache: 'no-store',
        headers: {
          'Content-Type': 'application/json',
          'X-XSRF-TOKEN': antiforgeryToken
        },
        body: JSON.stringify(payload),
        signal: controller.signal
      })
      if (disposed || generation !== requestGeneration) return null
      const body = await response.json().catch(() => ({}))
      if (disposed || generation !== requestGeneration) return null
      return response.ok && typeof body.outcome === 'string' ? body.outcome : 'unavailable'
    } catch (error) {
      if (error?.name === 'AbortError' || disposed || generation !== requestGeneration) return null
      return 'unavailable'
    } finally {
      if (!disposed && generation === requestGeneration) setBusy(false)
    }
  }

  claimForm.addEventListener('submit', async event => {
    event.preventDefault()
    hideMessage()
    let continuation = continuationInput.value
    continuationInput.value = ''
    try {
      const outcome = await post(page.dataset.claimEndpoint, { continuation })
      if (outcome === null) return
      clearSecrets()
      if (outcome === 'verificationRequired') {
        showVerification()
      } else {
        showClaim()
        showMessage(page.dataset.messageUnavailable)
      }
    } finally {
      continuation = ''
    }
  })

  codeInput.addEventListener('input', () => {
    codeInput.value = codeInput.value.replace(/\D/g, '').slice(0, 6)
  })

  verifyForm.addEventListener('submit', async event => {
    event.preventDefault()
    hideMessage()
    let ownershipCode = codeInput.value
    let currentCredential = credentialInput.value
    clearSecrets()
    try {
      if (!/^\d{6}$/.test(ownershipCode)) {
        showVerification()
        showMessage(page.dataset.messageInvalidCode)
        return
      }
      const outcome = await post(
        page.dataset.verifyEndpoint,
        { ownershipCode, currentCredential },
        { allowCancel: true }
      )
      if (outcome === null) return
      clearSecrets()
      if (outcome === 'resolved') {
        showResult(page.dataset.messageResolved)
      } else {
        showClaim()
        showMessage(page.dataset.messageUnavailable)
      }
    } finally {
      ownershipCode = ''
      currentCredential = ''
    }
  })

  cancelButton?.addEventListener('click', async () => {
    hideMessage()
    clearSecrets()
    const outcome = await post(page.dataset.cancelEndpoint, {})
    if (outcome === null) return
    clearSecrets()
    if (outcome === 'cancelled') {
      showResult(page.dataset.messageCancelled)
    } else {
      showClaim()
      showMessage(page.dataset.messageUnavailable)
    }
  })

  const clearForNavigation = () => {
    disposed = true
    requestGeneration += 1
    activeController?.abort()
    clearSecrets()
  }
  const armPageHide = () => browserWindow?.addEventListener('pagehide', clearForNavigation, { once: true })
  armPageHide()
  browserWindow?.addEventListener('beforeunload', clearForNavigation, { once: true })
  browserWindow?.addEventListener('pageshow', event => {
    if (event.persisted) {
      disposed = false
      activeController = null
      clearSecrets()
      setBusy(false)
      showClaim()
      continuationInput.focus({ preventScroll: true })
      armPageHide()
    }
  })

  clearSecrets()
  showClaim()
  continuationInput.focus({ preventScroll: true })

  return { clearSecrets }
}

document.addEventListener('DOMContentLoaded', () => initPendingDirectorySettlement())
