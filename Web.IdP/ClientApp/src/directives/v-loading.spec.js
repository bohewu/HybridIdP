import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import vLoading from './v-loading'

it('uses a single overlay, updates the loading state, and removes its mounted loader', async () => {
  const wrapper = mount({
    props: ['loading', 'overlay'],
    template: '<section v-loading="{ loading, overlay, message: \'Please wait\' }">Content</section>'
  }, { props: { loading: true, overlay: true }, global: { directives: { loading: vLoading } } })
  const host = wrapper.element
  const container = wrapper.get('.v-loading-container')
  expect(container.classes()).toContain('bg-white/60')
  expect(wrapper.get('[role="status"]').classes()).not.toContain('bg-white/60')
  expect(wrapper.get('[role="status"]').classes()).not.toContain('absolute')
  expect(wrapper.text()).toContain('Please wait')
  await wrapper.setProps({ loading: false })
  expect(container.element.style.display).toBe('none')
  await wrapper.setProps({ loading: true, overlay: false })
  expect(container.classes()).not.toContain('bg-white/60')
  expect(container.element.style.display).toBe('')
  wrapper.unmount()
  expect(host.querySelector('.v-loading-container')).toBeNull()
})
