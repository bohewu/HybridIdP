import { mount } from '@vue/test-utils'
import { describe, it, expect, vi } from 'vitest'
import ClaimFormModal from '../ClaimFormModal.vue'
import ClaimConditionEditor from '../ClaimConditionEditor.vue'

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
      profileSchema: { enabled: true, sources: [{ name: 'source', properties: { code: 'String', flag: 'Boolean', categories: 'StringArray' } }] },
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
  it('submits a typed profile condition and rejects malformed JSON', async () => {
    const wrapper = mountModal()
    await wrapper.find('[data-test-id="claim-profile-source-input"]').setValue('source')
    await wrapper.find('[data-test-id="claim-condition-checkbox"]').setValue(true)
    await wrapper.find('[data-test-id="condition-mode-json"]').trigger('click')
    await wrapper.find('[data-test-id="claim-condition-input"]').setValue('{bad')
    await wrapper.find('#claim-form').trigger('submit')
    expect(wrapper.emitted('save')).toBeUndefined()
    const rule = { operator: 'StartsWith', property: 'code', value: '4' }
    await wrapper.find('[data-test-id="claim-condition-input"]').setValue(JSON.stringify(rule))
    await wrapper.find('#claim-form').trigger('submit')
    const saved = wrapper.emitted('save')[0][0]
    expect(saved.providerProfileSource).toBe('source')
    expect(saved.dataType).toBe('Boolean')
    expect(saved.condition).toEqual(rule)
  })

  it('uses deployment-approved source fields and their type for direct arrays', async () => {
    const wrapper = mountModal()
    await wrapper.find('[data-test-id="claim-profile-source-input"]').setValue('source')
    await wrapper.find('[data-test-id="claim-profile-property-select"]').setValue('categories')
    expect(wrapper.find('[data-test-id="claim-data-type-select"]').element.value).toBe('StringArray')
    expect(wrapper.find('[data-test-id="claim-data-type-select"]').element.disabled).toBe(true)
    expect(wrapper.find('[data-test-id="claim-property-path-input"]').exists()).toBe(false)
    await wrapper.find('#claim-form').trigger('submit')
    expect(wrapper.emitted('save')[0][0]).toMatchObject({ providerProfileSource: 'source', userPropertyPath: 'categories', dataType: 'StringArray', condition: null })
  })

  it('builds typed nested AND OR rules and keeps advanced JSON synchronized', async () => {
    const wrapper = mountModal()
    await wrapper.find('[data-test-id="claim-profile-source-input"]').setValue('source')
    await wrapper.find('[data-test-id="claim-condition-checkbox"]').setValue(true)
    await wrapper.find('[data-test-id="condition-property"]').setValue('flag')
    await wrapper.find('[data-test-id="condition-value"]').setValue('true')
    await wrapper.find('[data-test-id="condition-add-group"]').trigger('click')
    await wrapper.findAll('[data-test-id="condition-operator"]')[2].setValue('Any')
    await wrapper.findAll('[data-test-id="condition-property"]')[1].setValue('categories')
    await wrapper.findAll('[data-test-id="condition-value"]')[1].setValue('student')
    await wrapper.find('#claim-form').trigger('submit')
    const rule = wrapper.emitted('save')[0][0].condition
    expect(rule).toEqual({ operator: 'All', children: [
      { operator: 'Equals', property: 'flag', value: true },
      { operator: 'Any', children: [{ operator: 'Contains', property: 'categories', value: 'student' }] }
    ] })
    await wrapper.find('[data-test-id="condition-mode-json"]').trigger('click')
    expect(JSON.parse(wrapper.find('[data-test-id="claim-condition-input"]').element.value)).toEqual(rule)
    await wrapper.find('[data-test-id="claim-condition-input"]').setValue('{bad')
    await wrapper.find('[data-test-id="condition-mode-visual"]').trigger('click')
    expect(wrapper.find('[data-test-id="condition-node"]').exists()).toBe(false)
    await wrapper.find('[data-test-id="condition-mode-json"]').trigger('click')
    expect(wrapper.find('[data-test-id="claim-condition-input"]').element.value).toBe('{bad')
  })

  it('preserves unavailable mappings and displays configuration load failures', async () => {
    const wrapper = mountModal({ claim: {
      name: 'old', displayName: 'Old', claimType: 'old', dataType: 'StringArray', providerProfileSource: 'retired', userPropertyPath: 'old'
    }, profileSchemaError: true })
    expect(wrapper.find('[data-test-id="claim-profile-source-input"]').element.value).toBe('retired')
    expect(wrapper.find('[data-test-id="claim-profile-property-select"]').element.value).toBe('old')
    expect(wrapper.text()).toContain('claims.form.schemaError')
    expect(wrapper.find('[data-test-id="claim-profile-source-input"]').element.disabled).toBe(true)
  })

  it('limits group width, depth and the total node count', async () => {
    const leaf = { operator: 'Equals', property: 'code', value: 'x' }
    const editor = mount(ClaimConditionEditor, { props: {
      modelValue: { operator: 'All', children: Array.from({ length: 8 }, () => ({ ...leaf })) },
      properties: { code: 'String' }, depth: 3, totalNodes: 32
    } })
    expect(editor.find('[data-test-id="condition-add"]').element.disabled).toBe(true)
    expect(editor.find('[data-test-id="condition-add-group"]').element.disabled).toBe(true)
    const child = editor.findAll('[data-test-id="condition-operator"]')[1]
    expect(child.findAll('option').map(option => option.element.value)).not.toContain('All')
  })

})
