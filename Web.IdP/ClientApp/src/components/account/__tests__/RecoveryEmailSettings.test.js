import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import RecoveryEmailSettings from '../RecoveryEmailSettings.vue'
import enMfa from '../../../i18n/locales/en-US/mfa.json'
import zhMfa from '../../../i18n/locales/zh-TW/mfa.json'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key) => key })
}))

const response = (data, ok = true, status = ok ? 200 : 400) => ({
  ok,
  status,
  json: () => Promise.resolve(data)
})

const wrappers = []
const render = options => { const wrapper = mount(RecoveryEmailSettings, { attachTo: document.body, ...options }); wrappers.push(wrapper); return wrapper }
const current = (extra = {}) => ({ enabled: true, mode: 'UseCustom', maskedAddress: 'o***d@example.test', maskedDefaultAddress: 'd***t@example.test', maskedPendingAddress: null, ...extra })
describe('RecoveryEmailSettings', () => {
  afterEach(() => { wrappers.splice(0).forEach(w => w.unmount()); vi.unstubAllGlobals() })
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  it('shows only the server-masked independent recovery address', async () => {
    fetch.mockResolvedValue(response({ isConfigured: true, isVerified: true, maskedAddress: 'r***y@example.test' }))
    const wrapper = render()
    await flushPromises()

    expect(wrapper.get('[data-testid="recovery-email-masked"]').text()).toContain('r***y@example.test')
    expect(wrapper.text()).not.toContain('profile@example.test')
    expect(wrapper.text()).toContain('mfa.recoveryEmail.verified')
  })

  it('requires destination verification after changing the recovery address', async () => {
    fetch
      .mockResolvedValueOnce(response({ isConfigured: false, isVerified: false, maskedAddress: null }))
      .mockResolvedValueOnce(response({ outcome: 'success' }))
      .mockResolvedValueOnce(response({ isConfigured: true, isVerified: false, maskedAddress: 'n***w@example.test' }))
      .mockResolvedValueOnce(response({ outcome: 'success' }))
      .mockResolvedValueOnce(response({ isConfigured: true, isVerified: true, maskedAddress: 'n***w@example.test' }))

    const wrapper = render({ props: { csrfToken: 'csrf-test' } })
    await flushPromises()
    await wrapper.get('.recovery-email-actions .primary').trigger('click')
    await wrapper.get('#recovery-email-candidate').setValue('new@example.test')
    await wrapper.get('.recovery-modal form').trigger('submit')
    await flushPromises()

    const changeRequest = fetch.mock.calls.find(([url]) => url.endsWith('/change'))
    expect(JSON.parse(changeRequest[1].body)).toEqual({ candidateAddress: 'new@example.test' })
    expect(changeRequest[1].headers['X-XSRF-TOKEN']).toBe('csrf-test')
    expect(wrapper.get('#recovery-email-code').exists()).toBe(true)

    await wrapper.get('#recovery-email-code').setValue('123456')
    await wrapper.get('.recovery-modal form').trigger('submit')
    await flushPromises()

    const verifyRequest = fetch.mock.calls.find(([url]) => url.endsWith('/verify'))
    expect(JSON.parse(verifyRequest[1].body)).toEqual({ code: '123456' })
    expect(wrapper.find('.recovery-modal').exists()).toBe(false)
  })

  it('makes the high-assurance outcome actionable without treating password login as proof', async () => {
    fetch.mockResolvedValue(response({ outcome: 'highAssuranceRequired' }, false, 403))
    const wrapper = render()
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe('mfa.recoveryEmail.outcomes.highAssuranceRequired')
    expect(wrapper.text()).toContain('mfa.recoveryEmail.retry')
    expect(wrapper.text()).not.toContain('password')
  })

  it('provides equivalent recovery-email keys in both locales', () => {
    for (const key of ['title', 'description', 'verified', 'pending', 'changeTitle', 'verifyTitle']) {
      expect(enMfa.recoveryEmail[key]).toBeTruthy()
      expect(zhMfa.recoveryEmail[key]).toBeTruthy()
    }
    expect(enMfa.recoveryEmail.outcomes.highAssuranceRequired).toBeTruthy()
    expect(zhMfa.recoveryEmail.outcomes.highAssuranceRequired).toBeTruthy()
  })
  it.each(['Legacy', 'UseDefault', 'UseCustom', 'Disabled'])('recognizes persisted %s with enabled=false as read-only', async mode => {
    fetch.mockResolvedValue(response(current({ mode, enabled: false, maskedAddress: mode === 'Disabled' ? null : 'o***d@example.test' })))
    const w = render(); await flushPromises()
    expect(w.get('[data-testid="recovery-email-mode"]').text()).toContain(`mfa.recoveryEmail.modes.${mode}`)
    expect(w.find('[data-testid="recovery-email-readonly"]').exists()).toBe(true)
    expect(w.findAll('.recovery-email-actions button')).toHaveLength(0)
    expect(w.text()).not.toContain('mfa.recoveryEmail.remove')
  })
  it('retains active custom while pending and restores opener focus after successful verification reloads status', async () => {
    let resolveStatus
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ outcome: 'success' })).mockResolvedValueOnce(response(current({ maskedPendingAddress: 'n***w@example.test', pendingExpiresAtUtc: new Date(Date.now()+60000).toISOString() }))).mockResolvedValueOnce(response({ outcome: 'success' })).mockImplementationOnce(() => new Promise(resolve => { resolveStatus = resolve }))
    const w = render({ props: { csrfToken: 'csrf-test' } }); await flushPromises()
    const opener = w.get('.recovery-email-actions .primary')
    opener.element.focus()
    await opener.trigger('click')
    await w.get('#recovery-email-candidate').setValue('new@example.test')
    await w.get('form').trigger('submit'); await flushPromises()
    expect(w.get('[data-testid="recovery-email-masked"]').text()).toContain('o***d@example.test')
    expect(w.get('[data-testid="recovery-pending"]').text()).toContain('n***w@example.test')
    expect(w.text()).not.toContain('new@example.test')
    await w.get('#recovery-email-code').setValue('123456')
    await w.get('form').trigger('submit'); await flushPromises()
    expect(w.attributes('aria-busy')).toBe('true')
    expect(opener.element.isConnected).toBe(false)
    expect(w.findAll('.recovery-email-actions button')).toHaveLength(0)
    expect(w.get('#recovery-email-code').attributes('disabled')).toBeDefined()
    resolveStatus(response(current({ maskedAddress: 'n***w@example.test' })))
    await flushPromises()
    expect(w.get('[data-testid="recovery-email-masked"]').text()).toContain('n***w@example.test')
    expect(w.find('[role="dialog"]').exists()).toBe(false)
    expect(document.activeElement).toBe(w.get('.recovery-email-actions .primary').element)
  })
  it('displays masked default before committing its protected confirmation and restoring focus after status reload', async () => {
    let resolveStatus
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ maskedAddress: 'd***t@example.test', confirmation: 'opaque-confirmation' })).mockResolvedValueOnce(response({ outcome: 'success' })).mockImplementationOnce(() => new Promise(resolve => { resolveStatus = resolve }))
    const w = render({ props: { csrfToken: 'csrf-test' } }); await flushPromises()
    const opener = w.get('[data-testid="recovery-use-default"]')
    opener.element.focus()
    await opener.trigger('click'); await flushPromises()
    expect(w.get('[data-testid="recovery-default-confirm-mask"]').text()).toBe('d***t@example.test')
    expect(fetch.mock.calls.some(([u]) => u.endsWith('/use-default'))).toBe(false)
    await w.get('[data-testid="recovery-dialog-confirm"]').trigger('click'); await flushPromises()
    expect(w.attributes('aria-busy')).toBe('true')
    expect(opener.element.isConnected).toBe(false)
    expect(w.findAll('.recovery-email-actions button')).toHaveLength(0)
    expect(w.get('[data-testid="recovery-dialog-confirm"]').attributes('disabled')).toBeDefined()
    resolveStatus(response(current({ mode: 'UseDefault' })))
    await flushPromises()
    expect(w.find('[role="dialog"]').exists()).toBe(false)
    expect(document.activeElement).toBe(w.get('[data-testid="recovery-use-default"]').element)
    const [,opts] = fetch.mock.calls.find(([u]) => u.endsWith('/use-default'))
    expect(JSON.parse(opts.body)).toEqual({ confirmation: 'opaque-confirmation' })
    expect(opts.headers['X-XSRF-TOKEN']).toBe('csrf-test')
    expect(opts.credentials).toBe('include')
    expect(fetch.mock.calls.some(([,o]) => o.method === 'DELETE')).toBe(false)
  })
  it('captures the default opener before delayed preparation expires the click event', async () => {
    let resolvePrepare
    fetch
      .mockResolvedValueOnce(response(current()))
      .mockImplementationOnce(() => new Promise(resolve => { resolvePrepare = resolve }))
      .mockResolvedValueOnce(response({ outcome: 'success' }))
      .mockResolvedValueOnce(response(current({ mode: 'UseDefault' })))
    const w = render(); await flushPromises()
    const opener = w.get('[data-testid="recovery-use-default"]')
    opener.element.focus()
    const clickEvent = new MouseEvent('click', { bubbles: true })
    opener.element.dispatchEvent(clickEvent)
    Object.defineProperty(clickEvent, 'currentTarget', { value: null })
    w.element.tabIndex = -1
    w.element.focus()
    await flushPromises()
    expect(clickEvent.currentTarget).toBeNull()
    expect(document.activeElement).toBe(w.element)
    expect(opener.attributes('disabled')).toBeDefined()
    expect(w.find('[role="dialog"]').exists()).toBe(false)

    resolvePrepare(response({ maskedAddress: 'd***t@example.test', confirmation: 'opaque-confirmation' }))
    await flushPromises()
    expect(w.get('[data-testid="recovery-default-confirm-mask"]').text()).toBe('d***t@example.test')
    await w.get('[data-testid="recovery-dialog-confirm"]').trigger('click'); await flushPromises()
    expect(w.get('[data-testid="recovery-email-mode"]').text()).toContain('UseDefault')
    expect(w.find('[role="dialog"]').exists()).toBe(false)
    expect(document.activeElement).toBe(w.get('[data-testid="recovery-use-default"]').element)
  })
  it('does not select or report a default when its prepare response lacks confirmation', async () => {
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ maskedAddress: 'd***t@example.test' }))
    const w = render(); await flushPromises()
    await w.get('[data-testid="recovery-use-default"]').trigger('click'); await flushPromises()
    expect(w.find('[role="dialog"]').exists()).toBe(false)
    expect(w.get('[role="alert"]').text()).toContain('unavailable')
    expect(fetch.mock.calls.some(([u]) => u.endsWith('/use-default'))).toBe(false)
  })
  it('keeps active custom and reports failure if prepared default changed', async () => {
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ maskedAddress: 'd***t@example.test', confirmation: 'opaque-confirmation' })).mockResolvedValueOnce(response({ outcome: 'invalid' }, false)).mockResolvedValueOnce(response(current()))
    const w = render(); await flushPromises()
    await w.get('[data-testid="recovery-use-default"]').trigger('click'); await flushPromises()
    await w.get('[data-testid="recovery-dialog-confirm"]').trigger('click'); await flushPromises()
    expect(w.get('[role="alert"]').text()).toContain('invalid')
    expect(w.text()).not.toContain('mfa.recoveryEmail.defaultSuccess')
    expect(w.text()).toContain('o***d@example.test')
  })
  it('resends and cancels only through empty cookie CSRF mutations and reloads pending', async () => {
    const pending=current({ maskedPendingAddress:'n***w@example.test', pendingExpiresAtUtc:new Date(Date.now()+60000).toISOString() })
    fetch.mockResolvedValueOnce(response(pending)).mockResolvedValueOnce(response({ outcome:'success' })).mockResolvedValueOnce(response(pending)).mockResolvedValueOnce(response({ outcome:'success' })).mockResolvedValueOnce(response(current()))
    const w=render({ props:{ csrfToken:'csrf-test' } }); await flushPromises()
    await w.get('[data-testid="recovery-resend"]').trigger('click'); await flushPromises()
    await w.get('[data-testid="recovery-cancel"]').trigger('click'); await flushPromises()
    for (const path of ['/resend','/cancel']) { const [,o]=fetch.mock.calls.find(([u])=>u.endsWith(path)); expect(o.method).toBe('POST'); expect(o.body).toBeUndefined(); expect(o.headers['X-XSRF-TOKEN']).toBe('csrf-test') }
    expect(w.find('[data-testid="recovery-pending"]').exists()).toBe(false)
    expect(w.text()).toContain('o***d@example.test')
  })
  it('shows cooldown and expiry, blocks expired verify/resend but keeps cancellation', async () => {
    fetch.mockResolvedValue(response(current({ maskedPendingAddress:'n***w@example.test', pendingExpiresAtUtc:new Date(Date.now()-1000).toISOString(), nextSendAllowedAtUtc:new Date(Date.now()+60000).toISOString() })))
    const w=render(); await flushPromises()
    expect(w.get('[data-testid="recovery-resend"]').attributes('disabled')).toBeDefined()
    expect(w.get('.recovery-pending .primary').attributes('disabled')).toBeDefined()
    expect(w.get('[data-testid="recovery-cancel"]').attributes('disabled')).toBeUndefined()
    expect(w.text()).toContain('mfa.recoveryEmail.pendingExpired')
  })
  it('explains hardware-only response before navigation without posting account/provider', async () => {
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ loginUrl:'/Account/Login?returnUrl=%2FAccount%2FProfile', hardwareOnly:true }))
    const w=render({ props:{csrfToken:'csrf-test'} }); await flushPromises()
    await w.get('[data-testid="recovery-reauthenticate"]').trigger('click')
    expect(fetch).toHaveBeenCalledTimes(1)
    await w.get('[data-testid="recovery-dialog-confirm"]').trigger('click'); await flushPromises()
    expect(w.get('[role="dialog"]').text()).toContain('mfa.recoveryEmail.hardwareOnly')
    const [,o]=fetch.mock.calls.find(([u])=>u.endsWith('/reauthenticate')); expect(o.body).toBeUndefined(); expect(o.headers['X-XSRF-TOKEN']).toBe('csrf-test')
  })
  it('offers existing enrollment/admin help when fresh account proof is unavailable', async () => {
    fetch.mockResolvedValueOnce(response(current())).mockResolvedValueOnce(response({ outcome:'hardwareReauthenticationUnavailable' },false,403))
    const w=render(); await flushPromises()
    await w.get('[data-testid="recovery-reauthenticate"]').trigger('click')
    await w.get('[data-testid="recovery-dialog-confirm"]').trigger('click'); await flushPromises()
    expect(w.get('[role="alert"]').text()).toContain('hardwareReauthenticationUnavailable')
    expect(w.get('a').attributes('href')).toBe('/Account/MfaSetup')
    expect(fetch.mock.calls.some(([u])=>u.endsWith('/change'))).toBe(false)
  })
  it('focuses input, traps Tab, restores trigger focus on Escape', async () => {
    fetch.mockResolvedValue(response(current()))
    const w=render(); await flushPromises()
    const trigger=w.get('.recovery-email-actions .primary'); trigger.element.focus()
    await trigger.trigger('click'); await flushPromises()
    expect(document.activeElement.id).toBe('recovery-email-candidate')
    await w.get('#recovery-email-candidate').trigger('keydown',{key:'Tab',shiftKey:true})
    expect(document.activeElement.textContent).toContain('common.cancel')
    await w.get('[role="dialog"]').trigger('keydown',{key:'Escape'}); await flushPromises()
    expect(document.activeElement).toBe(trigger.element)
  })
  it('has equivalent plain-text locale keys and independent-mailbox warning', () => {
    const keys=o=>Object.keys(o).flatMap(k=>typeof o[k]==='object'?keys(o[k]).map(c=>`${k}.${c}`):[k])
    expect(keys(enMfa.recoveryEmail).sort()).toEqual(keys(zhMfa.recoveryEmail).sort())
    for(const r of [enMfa.recoveryEmail,zhMfa.recoveryEmail]) { expect(JSON.stringify(r)).not.toMatch(/<[^>]+>/); expect(r.independentWarning).toBeTruthy(); expect(r.modes.Legacy).toBeTruthy() }
  })

})
