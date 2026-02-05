import { mount } from '@vue/test-utils'
import { describe, it, expect, vi } from 'vitest'
import ClaimFormModal from '../ClaimFormModal.vue'

const BaseModal = {
  template: '<div><slot name="body"></slot><slot name="footer"></slot></div>',
  props: ['show', 'title', 'loading']
}

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key) => key
  })
}))

describe('ClaimFormModal.vue', () => {
  const mountModal = (props = {}) => mount(ClaimFormModal, {
    global: {
      stubs: { BaseModal }
    },
    props: {
      show: true,
      claim: null,
      error: null,
      ...props
    }
  })

  it('resets form when opening the create modal', async () => {
    const wrapper = mountModal({
      claim: {
        name: 'custom_name',
        displayName: 'Custom Name',
        description: 'custom description',
        claimType: 'custom_type',
        userPropertyPath: 'Email',
        dataType: 'String',
        isRequired: true,
        isStandard: false
      }
    })

    expect(wrapper.find('[data-test-id="claim-name-input"]').element.value).toBe('custom_name')

    await wrapper.setProps({ show: false })
    await wrapper.setProps({ show: true, claim: null })

    expect(wrapper.find('[data-test-id="claim-name-input"]').element.value).toBe('')
    expect(wrapper.find('[data-test-id="claim-type-input"]').element.value).toBe('')
    expect(wrapper.find('[data-test-id="claim-property-path-select"]').element.value).toBe('')
  })

  it('auto-fills claimType from name only when claimType is empty', async () => {
    const wrapper = mountModal()

    const nameInput = wrapper.find('[data-test-id="claim-name-input"]')
    await nameInput.setValue('given_name')

    const claimTypeInput = wrapper.find('[data-test-id="claim-type-input"]')
    expect(claimTypeInput.element.value).toBe('given_name')

    await claimTypeInput.setValue('manual_type')
    await nameInput.setValue('family_name')

    expect(claimTypeInput.element.value).toBe('manual_type')
  })

  it('sets claimType when selecting a property path if claimType is empty', async () => {
    const wrapper = mountModal()

    const propertyPathSelect = wrapper.find('[data-test-id="claim-property-path-select"]')
    await propertyPathSelect.setValue('Email')

    const claimTypeInput = wrapper.find('[data-test-id="claim-type-input"]')
    expect(claimTypeInput.element.value).toBe('Email')
  })

  it('stops auto-sync after manually changing claimType', async () => {
    const wrapper = mountModal()

    const nameInput = wrapper.find('[data-test-id="claim-name-input"]')
    await nameInput.setValue('national_id')

    const claimTypeInput = wrapper.find('[data-test-id="claim-type-input"]')
    expect(claimTypeInput.element.value).toBe('national_id')

    await claimTypeInput.setValue('custom_claim')
    await nameInput.setValue('updated_name')

    expect(claimTypeInput.element.value).toBe('custom_claim')
  })
})
