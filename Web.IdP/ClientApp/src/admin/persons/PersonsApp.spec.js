import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PersonsApp from './PersonsApp.vue'
import persons from '@/i18n/locales/en-US/persons.json'

vi.mock('@/utils/permissionService', async importOriginal => {
  const actual = await importOriginal()
  return { ...actual, default: {
    loadPermissions: vi.fn().mockResolvedValue(),
    hasPermission: vi.fn().mockReturnValue(true)
  } }
})

describe('Person search normalization', () => {
  let wrapper
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true, json: async () => ({ persons: [], totalCount: 0 })
    }))
  })
  afterEach(() => {
    wrapper?.unmount()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it.each([' ', '   ', '\t', '\u3000', '  Example  '])('normalizes %j before choosing the list endpoint', async input => {
    wrapper = mount(PersonsApp, { global: {
      plugins: [createI18n({ legacy: false, locale: 'en-US', missingWarn: false,
        fallbackWarn: false, messages: { 'en-US': { persons } } })],
      stubs: { PersonForm: true, LinkedAccountsDialog: true, TransferAssetsDialog: true,
        AccessDeniedDialog: true, PageHeader: true }
    } })
    await flushPromises()
    fetch.mockClear()
    await wrapper.get('[data-test-id="person-search"]').setValue(input)
    await flushPromises()
    const request = new URL(fetch.mock.calls.at(-1)[0], 'http://localhost')
    expect(request.pathname).toBe(input.trim() ? '/api/admin/people/search' : '/api/admin/people')
    expect(request.searchParams.get('term')).toBe(input.trim() || null)
    expect(wrapper.text()).not.toContain('HTTP error!')
  })
})
