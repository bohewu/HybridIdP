import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ChangePasswordForm from '../ChangePasswordForm.vue'

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: key => key }) }))

const response = (data, ok = true, status = ok ? 200 : 400) => ({
  ok, status, json: async () => data
})
const wrappers = []
const render = async () => {
  const wrapper = mount(ChangePasswordForm, {
    props: { allowPasswordChange: true, hasLocalPassword: true, csrfToken: 'csrf-fixture' }
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}
const fill = async (wrapper, password = '${TEST_NEW_PASSWORD_1}', confirmation = password) => {
  await wrapper.get('#currentPassword').setValue('dummy')
  await wrapper.get('#newPassword').setValue(password)
  await wrapper.get('#confirmPassword').setValue(confirmation)
}
const passwordRequests = () => fetch.mock.calls.filter(([url]) => url === '/api/profile/change-password')

describe('ChangePasswordForm', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  })
  afterEach(() => {
    wrappers.splice(0).forEach(wrapper => wrapper.unmount())
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it.each(['unavailable', 'empty'])('submits once with CSRF when the checklist is %s', async mode => {
    fetch.mockResolvedValueOnce(mode === 'unavailable' ? response({}, false, 401) : response({}))
      .mockResolvedValueOnce(response({ message: 'Password changed successfully' }))
    const wrapper = await render()
    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true)
    await fill(wrapper)

    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(false)
    expect(wrapper.text()).toContain('profile.changePassword.passwordsMatch')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(passwordRequests()).toHaveLength(1)
    const [, request] = passwordRequests()[0]
    expect(request.method).toBe('POST')
    expect(request.credentials).toBe('include')
    expect(request.headers['X-XSRF-TOKEN']).toBe('csrf-fixture')
    expect(JSON.parse(request.body)).toEqual({
      currentPassword: 'dummy', newPassword: '${TEST_NEW_PASSWORD_1}', confirmPassword: '${TEST_NEW_PASSWORD_1}'
    })
  })

  it('blocks mismatched confirmation even without a checklist', async () => {
    fetch.mockResolvedValueOnce(response({}, false, 401))
    const wrapper = await render()
    await fill(wrapper, 'NewPassword2!', 'DifferentPassword3!')

    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true)
    await wrapper.get('form').trigger('submit')
    expect(passwordRequests()).toHaveLength(0)
    expect(wrapper.text()).toContain('profile.changePassword.passwordMismatch')
  })

  it('blocks unsatisfied loaded requirements and enables a matching compliant password', async () => {
    fetch.mockResolvedValueOnce(response({ minPasswordLength: 12, requireDigit: true }))
    const wrapper = await render()
    await fill(wrapper, 'short')

    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(true)
    await wrapper.get('form').trigger('submit')
    expect(passwordRequests()).toHaveLength(0)
    expect(wrapper.text()).toContain('profile.changePassword.passwordRequirementsNotMet')
    await fill(wrapper)
    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(false)
  })

  it('displays authoritative server policy errors when the checklist is unavailable', async () => {
    fetch.mockResolvedValueOnce(response({}, false, 401))
      .mockResolvedValueOnce(response({ errors: [{ code: 'PasswordChangeTooSoon', description: 'Wait until the minimum password age is reached.' }] }, false))
    const wrapper = await render()
    await fill(wrapper)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(passwordRequests()).toHaveLength(1)
    expect(wrapper.text()).toContain('Wait until the minimum password age is reached.')
    expect(wrapper.get('#newPassword').element.value).toBe('${TEST_NEW_PASSWORD_1}')
    expect(wrapper.get('button[type="submit"]').element.disabled).toBe(false)
  })
})
