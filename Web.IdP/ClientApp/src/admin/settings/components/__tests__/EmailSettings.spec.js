import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import EmailSettings from '../EmailSettings.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key, params) => {
      if (key === 'settings.testErrors.rejected') {
        return `smtp-rejected:${params.status}`
      }
      if (key === 'settings.testError') {
        return `test-error:${params.message}`
      }
      return key
    }
  })
}))

const BaseModal = {
  props: ['show', 'title', 'loading'],
  emits: ['close'],
  template: `
    <div v-if="show" data-testid="email-test-modal">
      <slot name="body"></slot>
      <slot name="footer"></slot>
    </div>
  `
}

const loadedSettings = [
  { key: 'Mail.Host', value: 'smtp.saved.test', isOverridden: false },
  { key: 'Mail.Port', value: '587', isOverridden: false },
  { key: 'Mail.Username', value: 'saved-user', isOverridden: false },
  { key: 'Mail.Password', value: '(set)', isOverridden: false },
  { key: 'Mail.EnableSsl', value: 'true', isOverridden: false },
  { key: 'Mail.FromAddress', value: 'saved-sender@example.test', isOverridden: false },
  { key: 'Mail.FromName', value: 'Saved Sender', isOverridden: false }
]

const jsonResponse = (data, ok = true) => Promise.resolve({
  ok,
  json: () => Promise.resolve(data)
})

describe('EmailSettings test email modal', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn()
      .mockImplementationOnce(() => jsonResponse(loadedSettings)))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  const mountComponent = async () => {
    const wrapper = mount(EmailSettings, {
      props: { canUpdate: true },
      global: {
        stubs: {
          BaseModal,
          LoadingIndicator: true
        }
      }
    })
    await flushPromises()
    return wrapper
  }

  it('uses an isolated SMTP snapshot and never copies the saved password', async () => {
    const wrapper = await mountComponent()

    await wrapper.get('[data-testid="open-email-test"]').trigger('click')

    expect(wrapper.get('[data-testid="test-smtp-host"]').element.value)
      .toBe('smtp.saved.test')
    expect(wrapper.get('[data-testid="test-smtp-password"]').element.value)
      .toBe('')

    await wrapper.get('[data-testid="test-smtp-host"]').setValue('smtp.temporary.test')

    expect(wrapper.get('[data-testid="email-settings-host"]').element.value)
      .toBe('smtp.saved.test')
  })

  it('posts only the modal values and renders an SMTP rejection as an error', async () => {
    const wrapper = await mountComponent()
    fetch.mockImplementationOnce(() => jsonResponse({
      code: 'smtp_rejected',
      smtpStatusCode: 553
    }, false))

    await wrapper.get('[data-testid="open-email-test"]').trigger('click')
    await wrapper.get('[data-testid="test-smtp-host"]').setValue('smtp.temporary.test')
    await wrapper.get('[data-testid="test-smtp-port"]').setValue(25)
    await wrapper.get('[data-testid="test-smtp-username"]').setValue('')
    await wrapper.get('[data-testid="test-smtp-enable-ssl"]').setValue(false)
    await wrapper.get('[data-testid="test-from-address"]').setValue('temporary-sender@example.test')
    await wrapper.get('[data-testid="test-from-name"]').setValue('Temporary Sender')
    await wrapper.get('[data-testid="test-recipient"]').setValue('recipient@example.test')
    await wrapper.get('[data-testid="send-email-test"]').trigger('click')
    await flushPromises()

    const request = JSON.parse(fetch.mock.calls[1][1].body)
    expect(request).toEqual({
      settings: {
        host: 'smtp.temporary.test',
        port: 25,
        username: '',
        password: '',
        enableSsl: false,
        fromAddress: 'temporary-sender@example.test',
        fromName: 'Temporary Sender'
      },
      to: 'recipient@example.test'
    })
    expect(wrapper.get('[role="alert"]').text()).toContain('smtp-rejected:553')
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="email-settings-host"]').element.value)
      .toBe('smtp.saved.test')
  })

  it('shows success only after the API confirms SMTP acceptance', async () => {
    const wrapper = await mountComponent()
    fetch.mockImplementationOnce(() => jsonResponse({
      message: 'SMTP server accepted the test email'
    }))

    await wrapper.get('[data-testid="open-email-test"]').trigger('click')
    await wrapper.get('[data-testid="test-smtp-username"]').setValue('')
    await wrapper.get('[data-testid="test-recipient"]').setValue('recipient@example.test')
    await wrapper.get('[data-testid="send-email-test"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="status"]').text()).toBe('settings.testSuccess')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="email-test-modal"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="send-email-test"]').text()).toBe('settings.sendAgain')

    await wrapper.get('[data-testid="cancel-email-test"]').trigger('click')

    expect(wrapper.find('[data-testid="email-test-modal"]').exists()).toBe(false)
  })

  it('locks the modal and shows progress while SMTP delivery is pending', async () => {
    let completeRequest
    const pendingResponse = new Promise(resolve => {
      completeRequest = resolve
    })
    const wrapper = await mountComponent()
    fetch.mockImplementationOnce(() => pendingResponse)

    await wrapper.get('[data-testid="open-email-test"]').trigger('click')
    await wrapper.get('[data-testid="test-smtp-username"]').setValue('')
    await wrapper.get('[data-testid="test-recipient"]').setValue('recipient@example.test')
    await wrapper.get('[data-testid="send-email-test"]').trigger('click')

    expect(wrapper.get('[data-testid="send-email-test"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="send-email-test"]').attributes('aria-busy')).toBe('true')
    expect(wrapper.find('[data-testid="send-email-spinner"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="cancel-email-test"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="test-smtp-host"]').attributes('disabled')).toBeDefined()

    completeRequest({
      ok: true,
      json: () => Promise.resolve({ message: 'SMTP server accepted the test email' })
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="send-email-spinner"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="send-email-test"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-testid="cancel-email-test"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-testid="test-smtp-host"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="email-test-modal"]').exists()).toBe(true)
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
  })
})
