import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PersonsApp from './PersonsApp.vue'
import persons from '@/i18n/locales/en-US/persons.json'
import zhPersons from '@/i18n/locales/zh-TW/persons.json'
import PersonForm from './components/PersonForm.vue'
import LinkedAccountsDialog from './components/LinkedAccountsDialog.vue'

vi.mock('@/utils/permissionService', async importOriginal => {
  const actual = await importOriginal()
  return { ...actual, default: {
    loadPermissions: vi.fn().mockResolvedValue(),
    hasPermission: vi.fn().mockReturnValue(true)
  } }
})

describe.each([['en-US', persons], ['zh-TW', zhPersons]])('Person name presentation and editing in %s', (locale, strings) => {
  let wrapper
  const plugins = () => [createI18n({ legacy: false, locale, missingWarn: false, fallbackWarn: false,
    messages: { [locale]: { persons: strings } } })]
  const modal = { template: '<div><slot name="body"/><slot name="footer"/></div>' }
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => [] }))
  })
  afterEach(() => { wrapper?.unmount(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

  it('uses structured names then native display name and localized missing text', async () => {
    const records = [
      { id: 'structured', firstName: 'Alex', middleName: 'Q', lastName: 'Example', nickname: 'Preferred' },
      { id: 'display-only', firstName: null, lastName: null, nickname: 'Taylor Example' },
      { id: 'missing', firstName: ' ', nickname: ' ', email: 'account@example.test', userName: 'account-id' }
    ]
    fetch.mockResolvedValueOnce({ ok: true, json: async () => ({ persons: records, totalCount: 3 }) })
    wrapper = mount(PersonsApp, { global: { plugins: plugins(), stubs: { PersonForm: true,
      LinkedAccountsDialog: true, TransferAssetsDialog: true, AccessDeniedDialog: true, PageHeader: true } } })
    await flushPromises()
    expect(wrapper.findAll('[data-test-id="person-name"]').map(item => item.text()))
      .toEqual(['Alex Q Example', 'Taylor Example', strings.notProvided])
    expect(wrapper.text().match(/Taylor Example/g)).toHaveLength(1)
    expect(wrapper.text()).not.toContain('account@example.test')
    expect(wrapper.text()).not.toContain('account-id')
  })

  it.each([
    [{ firstName: 'Alex', lastName: 'Example', nickname: 'Preferred' }, 'Alex Example'],
    [{ nickname: 'Taylor Example' }, 'Taylor Example'],
    [{ email: 'account@example.test', userName: 'account-id' }, null]
  ])('uses the effective Person name in linked accounts', async (person, expected) => {
    wrapper = mount(LinkedAccountsDialog, { props: { person: { id: 'person-a', ...person } },
      global: { plugins: plugins(), stubs: { BaseModal: modal } } })
    await flushPromises()
    expect(wrapper.text()).toContain(strings.linkedAccounts.personName.replace('{name}', expected || strings.notProvided))
    expect(wrapper.text()).not.toContain('account@example.test')
    expect(wrapper.text()).not.toContain('account-id')
  })

  it.each([false, true])('saves a full-name-only Person without inventing name parts (edit=%s)', async isEdit => {
    wrapper = mount(PersonForm, { props: { person: isEdit ? { id: 'person-a', nickname: 'Taylor Example' } : null },
      global: { plugins: plugins(), stubs: { BaseModal: modal } } })
    expect(wrapper.get('#firstName').element.required).toBe(false)
    expect(wrapper.get('#lastName').element.required).toBe(false)
    expect(wrapper.get('#nickname').attributes('placeholder')).toBe(strings.form.nicknamePlaceholder)
    expect(wrapper.get('#person-name-help').text()).toBe(strings.form.nameHelp)
    await wrapper.get('#nickname').setValue('Taylor Example')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    const [url, options] = fetch.mock.calls.at(-1)
    expect(url).toBe(isEdit ? '/api/admin/people/person-a' : '/api/admin/people')
    expect(options.method).toBe(isEdit ? 'PUT' : 'POST')
    expect(JSON.parse(options.body)).toMatchObject({ firstName: '', middleName: '', lastName: '', nickname: 'Taylor Example' })
    expect(wrapper.emitted('save')).toHaveLength(1)
  })
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
