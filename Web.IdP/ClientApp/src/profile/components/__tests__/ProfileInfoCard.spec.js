import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import ProfileInfoCard from '../ProfileInfoCard.vue'
import enProfile from '../../../i18n/locales/en-US/profile.json'
import zhProfile from '../../../i18n/locales/zh-TW/profile.json'
import { beginExternalLoginLink } from '@/services/accountApi'

vi.mock('@/services/accountApi', () => ({ beginExternalLoginLink: vi.fn() }))

const wrappers = []
const render = (profile = {}, locale = 'en-US') => {
  const wrapper = mount(ProfileInfoCard, {
    props: { profile: { displayName: 'Alex Example', userName: 'opaque_abc123', externalLogins: [], availableProviders: [], ...profile } },
    global: { plugins: [createI18n({ legacy: false, locale, messages: { 'en-US': { profile: enProfile }, 'zh-TW': { profile: zhProfile } } })] }
  })
  wrappers.push(wrapper)
  return wrapper
}

describe('ProfileInfoCard managed account presentation', () => {
  beforeEach(() => {
    document.body.innerHTML = '<div id="profile-app" data-csrf-token="csrf-fixture"></div>'
    vi.stubGlobal('fetch', vi.fn())
    vi.stubGlobal('confirm', vi.fn(() => true))
    vi.stubGlobal('alert', vi.fn())
    vi.mocked(beginExternalLoginLink).mockReset()
  })

  afterEach(() => {
    wrappers.splice(0).forEach(wrapper => wrapper.unmount())
    document.body.innerHTML = ''
    vi.unstubAllGlobals()
  })

  describe.each([
    ['en-US', enProfile, 'Password is managed by your organization', 'Using external login only'],
    ['zh-TW', zhProfile, '密碼由組織管理', '僅使用外部登入']
  ])('%s', (locale, strings, managedPasswordText, externalOnlyText) => {
    it('renders the nested Person name separately from the account display name', () => {
      const person = { fullName: 'Taylor Person' }
      const wrapper = render({ person, personFullName: 'Obsolete flat value' }, locale)
      const nameLabel = wrapper.findAll('dt').find(label => label.text() === strings.info.fullName)

      expect(strings.info.fullName).toBe(locale === 'zh-TW' ? '姓名' : 'Name')
      expect(nameLabel.element.nextElementSibling.textContent).toBe('Taylor Person')
      expect(wrapper.findAll('dd')[0].text()).toBe('Alex Example')
      expect(wrapper.text()).not.toContain('Obsolete flat value')
      expect(person.fullName).toBe('Taylor Person')
    })

    it.each([
      ['absent', {}],
      ['undefined', { fullName: undefined }],
      ['null', { fullName: null }],
      ['empty', { fullName: '' }],
      ['whitespace', { fullName: ' \t\n ' }]
    ])('renders localized missing-name copy for %s without changing Person data', (_, person) => {
      const original = { ...person }
      const wrapper = render({ person }, locale)
      const nameLabel = wrapper.findAll('dt').find(label => label.text() === strings.info.fullName)

      expect(strings.info.notProvided).toBe(locale === 'zh-TW' ? '未提供' : 'Not provided')
      expect(nameLabel.element.nextElementSibling.textContent).toBe(strings.info.notProvided)
      expect(wrapper.findAll('dd')[0].text()).toBe('Alex Example')
      expect(person).toEqual(original)
    })

    it.each([undefined, null])('keeps Personal Details hidden when Person is %s', person => {
      const wrapper = render({ person }, locale)

      expect(wrapper.text()).not.toContain(strings.info.personalDetails)
      expect(wrapper.findAll('dt').some(label => label.text() === strings.info.fullName)).toBe(false)
      expect(wrapper.findAll('dd')[0].text()).toBe('Alex Example')
    })

    it.each([
      ['legacy-only', [{ loginProvider: 'Legacy', isLegacy: true }]],
      ['managed-only', [{ loginProvider: 'example.organization', isLegacy: false }]],
      ['both', [
        { loginProvider: 'example.organization', isLegacy: false },
        { loginProvider: 'Legacy', isLegacy: true }
      ]]
    ])('renders one generic read-only organization indication for %s', (_, managedCredentials) => {
      const wrapper = render({ managedCredentials, hasLocalPassword: false }, locale)

      expect(wrapper.findAll('[data-testid="managed-credentials"]')).toHaveLength(1)
      const organization = wrapper.get('[data-testid="managed-credentials"]')
      expect(organization.get('h3').text()).toBe(strings.managedCredentials.title)
      expect(organization.findAll('p').map(row => row.text())).toEqual([strings.managedCredentials.description])
      expect(wrapper.get('[data-testid="profile-info-card"]').text()).toContain(managedPasswordText)
      expect(wrapper.get('[data-testid="profile-info-card"]').text()).not.toContain(strings.security.noPassword)
      expect(wrapper.get('[data-testid="profile-info-card"]').text()).not.toContain(externalOnlyText)
      expect(wrapper.text()).not.toContain('example.organization')
      expect(wrapper.text()).not.toContain('Legacy')
      expect(organization.findAll('li, button, a, form')).toHaveLength(0)
      expect(wrapper.find('[data-testid="external-logins"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="available-providers"]').exists()).toBe(false)
    })

    it('retains password-set status for a managed account with a local password', () => {
      const wrapper = render({
        managedCredentials: [{ loginProvider: 'example.organization', isLegacy: false }],
        hasLocalPassword: true
      }, locale)
      const accountInfo = wrapper.get('[data-testid="profile-info-card"]').text()

      expect(accountInfo).toContain(strings.security.passwordSet)
      expect(accountInfo).not.toContain(managedPasswordText)
      expect(accountInfo).not.toContain(strings.security.noPassword)
    })

    it('retains the no-local-password fallback without a managed source', () => {
      const wrapper = render({ managedCredentials: [], hasLocalPassword: false }, locale)
      const accountInfo = wrapper.get('[data-testid="profile-info-card"]').text()

      expect(accountInfo).toContain(strings.security.noPassword)
      expect(accountInfo).not.toContain(managedPasswordText)
      expect(accountInfo).not.toContain(strings.security.passwordSet)
    })
  })

  it('renders the server display name and retains the original account identifier', () => {
    const wrapper = render()
    const values = wrapper.get('[data-testid="profile-info-card"]').findAll('dd')
    expect(values[0].text()).toBe('Alex Example')
    expect(values[1].text()).toBe('opaque_abc123')
  })

  it('renders the server username fallback without synthesizing a human name', () => {
    const wrapper = render({ displayName: 'opaque_abc123' })
    expect(wrapper.get('[data-testid="profile-info-card"]').findAll('dd')[0].text()).toBe('opaque_abc123')
    expect(wrapper.find('[data-testid="managed-credentials"]').exists()).toBe(false)
  })

  it('retains optional Google removal and Microsoft linking beside a managed source', async () => {
    const login = { loginProvider: 'Google', providerKey: 'google-fixture-key' }
    const wrapper = render({
      managedCredentials: [
        { loginProvider: 'example.organization', isLegacy: false },
        { loginProvider: 'Legacy', isLegacy: true }
      ],
      externalLogins: [login],
      availableProviders: [{ scheme: 'Microsoft', displayName: 'Microsoft' }]
    })
    fetch.mockResolvedValueOnce({ ok: true })
    await wrapper.get('[data-testid="external-logins"] button').trigger('click')
    await flushPromises()
    expect(fetch).toHaveBeenCalledWith('/api/profile/remove-login', expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ 'X-XSRF-TOKEN': 'csrf-fixture' }), body: JSON.stringify(login)
    }))
    expect(wrapper.emitted('updated')).toHaveLength(1)

    vi.mocked(beginExternalLoginLink).mockRejectedValueOnce(new Error('freshAuthenticationRequired'))
    await wrapper.get('[data-testid="available-providers"] button').trigger('click')
    await flushPromises()
    expect(beginExternalLoginLink).toHaveBeenCalledWith('Microsoft', 'csrf-fixture')
    expect(alert).toHaveBeenCalledWith(enProfile.common.errors.freshAuthenticationRequired)
    expect(wrapper.get('[data-testid="managed-credentials"]').findAll('button')).toHaveLength(0)
    expect(wrapper.get('[data-testid="external-logins"]').findAll('li')).toHaveLength(1)
    expect(wrapper.get('[data-testid="external-logins"]').text()).toContain('Google')
    expect(wrapper.get('[data-testid="available-providers"]').text()).toContain('Microsoft')
    expect(wrapper.text()).not.toContain('example.organization')
    expect(wrapper.text()).not.toContain('Legacy')
  })
})
