
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import SearchableCheckboxList from '../SearchableCheckboxList.vue'
import { createI18n } from 'vue-i18n'

// Mock i18n
const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: {
    en: {}
  }
})

describe('SearchableCheckboxList.vue', () => {
  const items = [
    { id: '1', name: 'Apple', code: 'fruit-apple' },
    { id: '2', name: 'Banana', code: 'fruit-banana' },
    { id: '3', name: 'Carrot', code: 'veg-carrot' }
  ]

  const createWrapper = (props = {}) => {
    return mount(SearchableCheckboxList, {
      global: {
        plugins: [i18n]
      },
      props: {
        items,
        labelKey: 'name',
        valueKey: 'id',
        modelValue: [],
        ...props
      }
    })
  }

  it('renders correctly with items', () => {
    const wrapper = createWrapper()
    const labels = wrapper.findAll('label')
    expect(labels.length).toBe(3)
    expect(wrapper.text()).toContain('Apple')
    expect(wrapper.text()).toContain('Banana')
  })

  it('filters items based on search query', async () => {
     const wrapper = createWrapper()
     const input = wrapper.find('input[type="text"]')
     
     await input.setValue('Carrot')
     expect(wrapper.findAll('label').length).toBe(1)
     expect(wrapper.text()).toContain('Carrot')
     expect(wrapper.text()).not.toContain('Apple')
  })

  it('toggles selection when clicked', async () => {
    const wrapper = createWrapper()
    const checkbox = wrapper.find('input[type="checkbox"]')
    
    await checkbox.setValue(true)
    
    // Check if event was emitted with the correct value ID ('1' for Apple)
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')[0]).toEqual([['1']])
  })

  it('handles "Select All Visible" correctly', async () => {
    const wrapper = createWrapper()
    
    // Initially nothing selected. Click the select all button (the square button in header)
    const toggleButton = wrapper.find('button[title="Select all visible"]')
    expect(toggleButton.exists()).toBe(true)
    
    await toggleButton.trigger('click')
    
    // Should select all 3
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    const lastEmit = wrapper.emitted('update:modelValue')
    // Get the last call to the event
    const lastValue = lastEmit[lastEmit.length - 1][0]
    expect(lastValue).toHaveLength(3)
    expect(lastValue).toEqual(expect.arrayContaining(['1', '2', '3']))
  })

  it('handles "Deselect All Visible" correctly', async () => {
    // Start with all selected
    const wrapper = createWrapper({ modelValue: ['1', '2', '3'] })
    
    // Button should now be in "Deselect" state
    const toggleButton = wrapper.find('button') 
    // Note: title might change dynamically, checking logic instead
    await toggleButton.trigger('click')
    
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    const lastEmit = wrapper.emitted('update:modelValue')
    const lastValue = lastEmit[lastEmit.length - 1][0]
    expect(lastValue).toHaveLength(0)
  })

  it('Select All respects search filter', async () => {
    const wrapper = createWrapper()
    const textInput = wrapper.find('input[type="text"]')
    
    // Search for "Apple"
    await textInput.setValue('Apple')
    
    const toggleButton = wrapper.findAll('button')[0] // The toggle button
    await toggleButton.trigger('click')
    
    // Should only select '1' (Apple)
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    const lastEmit = wrapper.emitted('update:modelValue')
    const lastValue = lastEmit[lastEmit.length - 1][0]
    expect(lastValue).toEqual(['1'])
  })

  it('displays loading state', () => {
    const wrapper = createWrapper({ loading: true })
    expect(wrapper.text()).toContain('Loading...')
    expect(wrapper.findAll('label').length).toBe(0)
  })
})
