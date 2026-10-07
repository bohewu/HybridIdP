import { mount, flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ResourceForm from './ResourceForm.vue'

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: key => key }) }))

const applicationId = '318859be-f583-48bc-82ae-65661d827dd5'
const resource = { id: 9, name: 'orders', isUsageOpen: false, isCatalogVisible: true }
const modal = { template: '<div><slot name="body" /><slot name="footer" /></div>' }
let wrapper
afterEach(() => { wrapper?.unmount(); vi.unstubAllGlobals() })

function render(approvalOk = true) {
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    if (options?.method === 'POST') return { ok: approvalOk }
    if (options?.method === 'PUT') return { ok: true }
    return { ok: true, json: async () => url.includes('/resources/')
      ? { ...resource, scopes: [{ scopeId: 's1', name: 'orders.read' }] }
      : { items: [] } }
  }))
  wrapper = mount(ResourceForm, { props: { resource }, global: { stubs: { BaseModal: modal }, mocks: { $t: key => key } } })
}

describe('API resource usage controls', () => {
  it('saves independent usage and catalog policies without replacing scope associations', async () => {
    render()
    await flushPromises()
    expect(wrapper.get('[data-testid="usage-open"]').element.checked).toBe(false)
    expect(wrapper.get('[data-testid="catalog-visible"]').element.checked).toBe(true)
    await wrapper.get('[data-test-id="resources-save-btn"]').trigger('click')
    await flushPromises()
    const [, request] = fetch.mock.calls.find(([, options]) => options?.method === 'PUT')
    expect(JSON.parse(request.body)).toMatchObject({ isUsageOpen: false, isCatalogVisible: true })
    expect(JSON.parse(request.body)).not.toHaveProperty('scopeIds')
  })

  it('approves only the selected saved scope and client application', async () => {
    render()
    await flushPromises()
    await wrapper.get('#approval-application').setValue(applicationId)
    await wrapper.get('#approval-scope').setValue('s1')
    await wrapper.get('[data-testid="approve-usage"]').trigger('click')
    await flushPromises()
    const [url, request] = fetch.mock.calls.find(([, options]) => options?.method === 'POST')
    expect(url).toBe('/api/admin/resources/9/usage-approvals')
    expect(JSON.parse(request.body)).toEqual({ applicationId, scopeId: 's1' })
    expect(wrapper.get('[role="status"]').text()).toBe('resources.form.usageApprovalSaved')
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('keeps the editor open and reports a denied approval', async () => {
    render(false)
    await flushPromises()
    await wrapper.get('#approval-application').setValue(applicationId)
    await wrapper.get('#approval-scope').setValue('s1')
    await wrapper.get('[data-testid="approve-usage"]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('resources.form.usageApprovalFailed')
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.emitted('submit')).toBeUndefined()
  })
})
