import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AccessDeniedDialog from '../AccessDeniedDialog.vue'

// Mock vue-i18n
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key) => key
  })
}))

describe('AccessDeniedDialog.vue', () => {
  const defaultProps = {
    show: true,
    message: 'Test Message',
    requiredPermission: 'Test.Permission'
  }

  it('renders correctly when visible', () => {
    const wrapper = mount(AccessDeniedDialog, {
      props: defaultProps,
      global: {
        stubs: {
          BaseModal: {
            template: '<div><slot name="body" /><slot name="footer" /></div>',
            props: ['show', 'title']
          }
        }
      }
    })

    expect(wrapper.text()).toContain('Test Message')
    expect(wrapper.text()).toContain('Test.Permission')
  })

  it('emits close event when close button is clicked', async () => {
    const wrapper = mount(AccessDeniedDialog, {
      props: defaultProps,
      global: {
        stubs: {
          BaseModal: {
            template: '<div><slot name="footer" /></div>',
            emits: ['close']
          }
        }
      }
    })

    // Find the OK button
    const buttons = wrapper.findAll('button')
    await buttons[0].trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('shows cancel button when showCancel is true', () => {
   const wrapper = mount(AccessDeniedDialog, {
      props: { ...defaultProps, showCancel: true },
      global: {
        stubs: {
          BaseModal: {
            template: '<div><slot name="footer" /></div>'
          }
        }
      }
    })
    
    // Should have 2 buttons (OK and Cancel)
    const buttons = wrapper.findAll('button')
    expect(buttons.length).toBe(2)
    expect(wrapper.text()).toContain('common.buttons.cancel')
  })
})
