import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import RecoveryAssistanceDialog from '../components/RecoveryAssistanceDialog.vue'
import enUsers from '../../../i18n/locales/en-US/users.json'
import zhUsers from '../../../i18n/locales/zh-TW/users.json'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key, params) => params ? `${key}:${JSON.stringify(params)}` : key })
}))

const okResponse = data => ({ ok: true, json: () => Promise.resolve(data) })
const deniedResponse = data => ({ ok: false, json: () => Promise.resolve(data) })
const context = ({
  ordinary = { state: 'available', resendOtp: true, replaceRecoveryEmail: true, approveReset: true },
  temporary = { state: 'available', issueTemporaryCredential: true },
  migration = { state: 'unavailable', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
  pending = { state: 'unavailable', inspect: false, prepareSettlement: false }
} = {}) => ({ ordinary, temporary, migration, pending })

async function mountDialog(
  fetchWithCsrf,
  user = { id: '11111111-1111-1111-1111-111111111111', userName: 'support-target', email: 'target@example.test' },
  options = {}
) {
  const wrapper = mount(RecoveryAssistanceDialog, { props: { user, fetchWithCsrf }, ...options })
  await flushPromises()
  return wrapper
}

describe('RecoveryAssistanceDialog', () => {
  beforeEach(() => vi.stubGlobal('confirm', vi.fn(() => true)))

  it('loads server context and renders exactly three purpose groups without a raw account id', async () => {
    const fetchWithCsrf = vi.fn(() => Promise.resolve(okResponse(context())))
    const wrapper = await mountDialog(fetchWithCsrf)

    expect(fetchWithCsrf).toHaveBeenCalledWith(
      '/api/admin/users/11111111-1111-1111-1111-111111111111/credential-recovery/context',
      { method: 'GET', headers: {} }
    )
    const tabs = wrapper.findAll('[role="tab"]')
    expect(tabs).toHaveLength(3)
    expect(tabs.map(tab => tab.text())).toEqual([
      'users.recoveryAssistance.groups.ordinary.title',
      'users.recoveryAssistance.groups.migration.title',
      'users.recoveryAssistance.groups.pending.title'
    ])
    expect(wrapper.text()).toContain('support-target')
    expect(wrapper.text()).not.toContain('11111111-1111-1111-1111-111111111111')
    expect(wrapper.find('[data-action="ordinary-resend"]').exists()).toBe(true)
    expect(wrapper.find('[data-action="temporary"]').exists()).toBe(true)
  })

  it('uses only server booleans and distinguishes unavailable, disabled, failure, and unknown states', async () => {
    const fetchWithCsrf = vi.fn(() => Promise.resolve(okResponse(context({
      ordinary: { state: 'confirmedFailure', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
      temporary: { state: 'disabled', issueTemporaryCredential: false },
      migration: { state: 'unavailable', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
      pending: { state: 'unexpected-state', inspect: false, prepareSettlement: false }
    }))))
    const wrapper = await mountDialog(fetchWithCsrf)

    expect(wrapper.text()).toContain('users.recoveryAssistance.states.confirmedFailure')
    expect(wrapper.text()).toContain('users.recoveryAssistance.states.disabled')
    expect(wrapper.findAll('[data-action]')).toHaveLength(0)
    await wrapper.get('[data-testid="recovery-group-migration"]').trigger('click')
    expect(wrapper.text()).toContain('users.recoveryAssistance.states.unavailable')
    await wrapper.get('[data-testid="recovery-group-pending"]').trigger('click')
    expect(wrapper.text()).toContain('users.recoveryAssistance.states.unknown')
    expect(wrapper.find('.pending-resolution').exists()).toBe(false)
  })

  it('routes ordinary and migration actions to their separate existing endpoint families', async () => {
    const allActions = context({
      migration: { state: 'available', resendOtp: true, replaceRecoveryEmail: true, approveReset: true }
    })
    const fetchWithCsrf = vi.fn()
      .mockResolvedValueOnce(okResponse(allActions))
      .mockResolvedValueOnce(okResponse({ outcome: 'success' }))
      .mockResolvedValueOnce(okResponse({ outcome: 'success' }))
    const wrapper = await mountDialog(fetchWithCsrf)

    await wrapper.get('[data-action="ordinary-resend"]').trigger('click')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(fetchWithCsrf.mock.calls[1]).toEqual([
      '/api/admin/users/11111111-1111-1111-1111-111111111111/password-recovery/resend-otp',
      { method: 'POST', headers: {} }
    ])

    await wrapper.get('[data-testid="recovery-group-migration"]').trigger('click')
    await wrapper.get('[data-action="migration-resend"]').trigger('click')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(fetchWithCsrf.mock.calls[2]).toEqual([
      '/api/admin/users/11111111-1111-1111-1111-111111111111/credential-recovery/resend-otp',
      { method: 'POST', headers: {} }
    ])
  })

  it('sends only approved fields for replacement and never renders a credential input', async () => {
    const fetchWithCsrf = vi.fn()
      .mockResolvedValueOnce(okResponse(context()))
      .mockResolvedValueOnce(okResponse({ outcome: 'success' }))
    const wrapper = await mountDialog(fetchWithCsrf)

    await wrapper.get('[data-action="ordinary-replace"]').trigger('click')
    await wrapper.get('#assistance-address').setValue('replacement@example.test')
    await wrapper.get('#assistance-evidence').setValue('CASE:20260912-001')
    await wrapper.get('#assistance-reason').setValue('Verified loss of recovery email')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fetchWithCsrf.mock.calls[1][1].body)).toEqual({
      candidateAddress: 'replacement@example.test',
      identityCheckEvidence: 'CASE:20260912-001',
      reason: 'Verified loss of recovery email'
    })
    expect(wrapper.find('input[type="password"]').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('intendedPassword')
    expect(wrapper.html()).not.toContain('currentCredential')
  })

  it('shows a server-generated temporary credential once and clears it across transitions', async () => {
    const fetchWithCsrf = vi.fn()
      .mockResolvedValueOnce(okResponse(context()))
      .mockResolvedValueOnce(okResponse({ outcome: 'success', temporaryPassword: 'one-time-test-value' }))
      .mockResolvedValueOnce(okResponse({ outcome: 'success', temporaryPassword: 'another-one-time-value' }))
      .mockResolvedValueOnce(okResponse(context()))
    const wrapper = await mountDialog(fetchWithCsrf)

    await wrapper.get('[data-action="temporary"]').trigger('click')
    await wrapper.get('#assistance-evidence').setValue('CASE:20260912-002')
    await wrapper.get('#assistance-reason').setValue('Approved temporary access')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.get('#assistance-temporary-password').element.value).toBe('one-time-test-value')

    await wrapper.get('[data-action="ordinary-resend"]').trigger('click')
    expect(wrapper.find('#assistance-temporary-password').exists()).toBe(false)

    await wrapper.get('[data-action="temporary"]').trigger('click')
    await wrapper.get('#assistance-evidence').setValue('CASE:20260912-003')
    await wrapper.get('#assistance-reason').setValue('Approved retry')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.get('#assistance-temporary-password').exists()).toBe(true)

    await wrapper.setProps({ user: { id: '22222222-2222-2222-2222-222222222222', userName: 'next-target' } })
    await flushPromises()
    expect(wrapper.find('#assistance-temporary-password').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('another-one-time-value')
  })

  it('inspects, prepares, and cancels an exact pending operation without rendering raw bindings', async () => {
    const pendingAttempt = {
      outcome: 'available',
      attemptId: '33333333-3333-3333-3333-333333333333',
      version: 7,
      operationKind: 'NativeReset',
      status: 'ReconciliationRequired',
      directoryObjectId: '44444444-4444-4444-4444-444444444444'
    }
    const preparationId = '55555555-5555-5555-5555-555555555555'
    const fetchWithCsrf = vi.fn()
      .mockResolvedValueOnce(okResponse(context({
        ordinary: { state: 'unavailable', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
        temporary: { state: 'unavailable', issueTemporaryCredential: false },
        pending: { state: 'available', inspect: true, prepareSettlement: true }
      })))
      .mockResolvedValueOnce(okResponse(pendingAttempt))
      .mockResolvedValueOnce(okResponse({ outcome: 'prepared', preparationId }))
      .mockResolvedValueOnce(okResponse({ outcome: 'cancelled' }))
    const wrapper = await mountDialog(fetchWithCsrf)

    await wrapper.get('[data-testid="recovery-group-pending"]').trigger('click')
    await wrapper.get('.pending-resolution > button').trigger('click')
    await flushPromises()
    expect(wrapper.text()).not.toContain(pendingAttempt.attemptId)
    expect(wrapper.text()).not.toContain(pendingAttempt.directoryObjectId)
    expect(wrapper.get('[data-operation-kind]').attributes('data-operation-kind')).toBe('NativeReset')
    expect(wrapper.find('input[type="password"]').exists()).toBe(false)

    await wrapper.get('.pending-confirmation input').setValue(true)
    await wrapper.get('#pending-evidence-reference').setValue('CASE:20260912-004')
    await wrapper.get('.pending-resolution form').trigger('submit')
    await flushPromises()
    expect(JSON.parse(fetchWithCsrf.mock.calls[2][1].body)).toEqual({
      attemptId: pendingAttempt.attemptId,
      expectedVersion: pendingAttempt.version,
      operationKind: pendingAttempt.operationKind,
      expectedStatus: pendingAttempt.status,
      directoryObjectId: pendingAttempt.directoryObjectId,
      originalWritersDrained: true,
      disposition: 'OriginalOperationSettled',
      evidenceCategory: 'ApprovedDirectoryOperation',
      evidenceReference: 'CASE:20260912-004'
    })
    expect(wrapper.text()).not.toContain(preparationId)

    await wrapper.get('.pending-state.success button').trigger('click')
    await flushPromises()
    expect(fetchWithCsrf.mock.calls[3]).toEqual([
      `/api/admin/users/credential-recovery/directory-settlement/${preparationId}/cancel`,
      { method: 'POST', headers: {} }
    ])
    expect(wrapper.text()).toContain('users.recoveryAssistance.pending.cancelled')
  })

  it('ignores stale context and mutation responses after the target changes', async () => {
    let resolveFirstContext
    let resolveMutation
    const firstContext = new Promise(resolve => { resolveFirstContext = resolve })
    const mutation = new Promise(resolve => { resolveMutation = resolve })
    const fetchWithCsrf = vi.fn()
      .mockImplementationOnce(() => firstContext)
      .mockResolvedValueOnce(okResponse(context({
        ordinary: { state: 'unavailable', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
        temporary: { state: 'disabled', issueTemporaryCredential: false }
      })))
    const wrapper = mount(RecoveryAssistanceDialog, {
      props: { user: { id: 'first-user', userName: 'first' }, fetchWithCsrf }
    })
    await wrapper.setProps({ user: { id: 'second-user', userName: 'second' } })
    await flushPromises()
    resolveFirstContext(okResponse(context()))
    await flushPromises()
    expect(wrapper.findAll('[data-action]')).toHaveLength(0)
    expect(wrapper.text()).toContain('second')

    fetchWithCsrf.mockReset()
    fetchWithCsrf
      .mockResolvedValueOnce(okResponse(context()))
      .mockImplementationOnce(() => mutation)
      .mockResolvedValueOnce(okResponse(context({
        ordinary: { state: 'unavailable', resendOtp: false, replaceRecoveryEmail: false, approveReset: false },
        temporary: { state: 'unavailable', issueTemporaryCredential: false }
      })))
    const secondWrapper = await mountDialog(fetchWithCsrf, { id: 'third-user', userName: 'third' })
    await secondWrapper.get('[data-action="temporary"]').trigger('click')
    await secondWrapper.get('#assistance-evidence').setValue('CASE:20260912-005')
    await secondWrapper.get('#assistance-reason').setValue('Approved')
    await secondWrapper.get('form').trigger('submit')
    await secondWrapper.setProps({ user: { id: 'fourth-user', userName: 'fourth' } })
    await flushPromises()
    resolveMutation(okResponse({ outcome: 'success', temporaryPassword: 'stale-value' }))
    await flushPromises()
    expect(secondWrapper.text()).not.toContain('stale-value')
    expect(secondWrapper.find('#assistance-temporary-password').exists()).toBe(false)
  })

  it('supports tab keyboard navigation, closes with Escape, and has paired locale keys', async () => {
    const fetchWithCsrf = vi.fn(() => Promise.resolve(okResponse(context())))
    const wrapper = await mountDialog(fetchWithCsrf, undefined, { attachTo: document.body })
    const ordinaryTab = wrapper.get('[data-testid="recovery-group-ordinary"]')
    await ordinaryTab.trigger('keydown', { key: 'ArrowRight' })
    await flushPromises()
    expect(wrapper.get('[data-testid="recovery-group-migration"]').attributes('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(wrapper.get('[data-testid="recovery-group-migration"]').element)

    await wrapper.get('.recovery-assistance-overlay').trigger('keydown', { key: 'Escape' })
    expect(wrapper.emitted('close')).toHaveLength(1)

    for (const locale of [enUsers, zhUsers]) {
      expect(Object.keys(locale.recoveryAssistance.groups)).toEqual(['ordinary', 'migration', 'pending'])
      expect(Object.keys(locale.recoveryAssistance.states).sort()).toEqual(
        ['available', 'confirmedFailure', 'disabled', 'unavailable', 'unknown']
      )
      for (const key of ['ordinary-resend', 'ordinary-replace', 'ordinary-approve', 'temporary', 'migration-resend', 'migration-replace', 'migration-approve']) {
        expect(locale.recoveryAssistance.actions[key]).toBeTruthy()
        expect(locale.recoveryAssistance.descriptions[key]).toBeTruthy()
        expect(locale.recoveryAssistance.submit[key]).toBeTruthy()
        expect(locale.recoveryAssistance.confirmations[key]).toBeTruthy()
        expect(locale.recoveryAssistance.outcomes[`${key}Success`]).toBeTruthy()
      }
      expect(locale.recoveryAssistance.pending.operationKinds.NativeReset).toBeTruthy()
      expect(locale.recoveryAssistance.pending.prepare).toBeTruthy()
      expect(locale.recoveryAssistance.pending.cancelPreparation).toBeTruthy()
    }
    wrapper.unmount()
  })

  it('sanitizes denied and unknown context outcomes and exposes no HIDP13 UI', async () => {
    const fetchWithCsrf = vi.fn()
      .mockResolvedValueOnce(deniedResponse({ outcome: 'highAssuranceRequired', detail: 'raw-provider-detail' }))
      .mockResolvedValueOnce(deniedResponse({ outcome: 'unavailable', detail: 'another-raw-detail' }))
    const wrapper = await mountDialog(fetchWithCsrf)
    expect(wrapper.get('[role="alert"]').text()).toContain('users.recoveryAssistance.outcomes.highAssuranceRequired')
    expect(wrapper.text()).not.toContain('raw-provider-detail')
    expect(wrapper.text().toLowerCase()).not.toContain('hidp13')
    expect(wrapper.text().toLowerCase()).not.toContain('sync')

    await wrapper.get('.dialog-actions button').trigger('click')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('users.recoveryAssistance.contextUnavailable')
    expect(wrapper.text()).not.toContain('another-raw-detail')
  })
})
