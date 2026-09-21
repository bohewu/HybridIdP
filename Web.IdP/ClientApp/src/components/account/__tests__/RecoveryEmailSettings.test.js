import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

describe('RecoveryEmailSettings', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.stubGlobal('confirm', vi.fn(() => true))
  })

  it('shows only the server-masked independent recovery address', async () => {
    fetch.mockResolvedValue(response({ isConfigured: true, isVerified: true, maskedAddress: 'r***y@example.test' }))
    const wrapper = mount(RecoveryEmailSettings)
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

    const wrapper = mount(RecoveryEmailSettings, { props: { csrfToken: 'csrf-test' } })
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
    const wrapper = mount(RecoveryEmailSettings)
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
})
