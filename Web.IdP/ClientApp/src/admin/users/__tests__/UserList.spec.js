import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import UserList from '../components/UserList.vue'
import enUsers from '../../../i18n/locales/en-US/users.json'
import zhUsers from '../../../i18n/locales/zh-TW/users.json'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key) => key
  })
}))

const ActionMenuStub = {
  template: `
    <div>
      <slot name="trigger" />
      <slot name="content" :close="close" />
    </div>
  `,
  methods: {
    close() {}
  }
}

const createWrapper = (canManageRoles, canUpdate = true) => mount(UserList, {
  props: {
    users: [{
      id: 'user-1',
      email: 'user@example.test',
      userName: 'user',
      roles: ['User'],
      isActive: true,
      lastLoginDate: null
    }],
    loading: false,
    page: 1,
    pageSize: 10,
    totalCount: 1,
    canUpdate,
    canManageRoles,
    canDelete: false,
    canRead: true,
    canImpersonate: false
  },
  global: {
    mocks: {
      $t: (key) => key
    },
    stubs: {
      ActionMenu: ActionMenuStub,
      LoadingIndicator: true,
      Pagination: true,
      SearchInput: true
    }
  }
})

describe('UserList role-management authorization', () => {
  it('shows the projected human name above the unchanged username without inventing account name fields', async () => {
    const wrapper = createWrapper(false)
    const user = { id: 'managed-user', displayName: 'Alex Example', userName: 'opaque_abc123', firstName: null, lastName: null, roles: [], isActive: true }
    await wrapper.setProps({ users: [user] })

    const cells = wrapper.get('[data-test-id="user-row-managed-user"]').findAll('td')
    expect(cells[0].findAll('div').map(element => element.text())).toEqual(['Alex Example', 'opaque_abc123'])
    expect(cells[2].text()).toBe('Alex Example')
    expect(user.firstName).toBeNull()
    expect(user.lastName).toBeNull()
    await wrapper.findAll('th')[0].trigger('click')
    expect(wrapper.emitted('sort-change')[0]).toEqual(['userName:asc'])
    wrapper.unmount()
  })

  it('requests effective display-name sorting in both directions', async () => {
    const wrapper = createWrapper(false)
    await wrapper.findAll('th')[2].trigger('click')
    expect(wrapper.emitted('sort-change')[0]).toEqual(['displayName:asc'])
    await wrapper.setProps({ sort: 'displayName:asc' })
    await wrapper.findAll('th')[2].trigger('click')
    expect(wrapper.emitted('sort-change')[1]).toEqual(['displayName:desc'])
    wrapper.unmount()
  })

  it('uses the real username once when the server has no human-name metadata', async () => {
    const wrapper = createWrapper(false)
    await wrapper.setProps({ users: [{ id: 'fallback-user', userName: 'opaque_abc123', displayName: 'opaque_abc123', roles: [], isActive: true }] })
    const cells = wrapper.get('[data-test-id="user-row-fallback-user"]').findAll('td')
    expect(cells[0].findAll('div').map(element => element.text())).toEqual(['opaque_abc123'])
    expect(cells[2].text()).toBe('users.details.noName')
    wrapper.unmount()
  })

  it('hides the role-management action without roles.update', () => {
    const wrapper = createWrapper(false)

    expect(wrapper.find('[data-testid="manage-roles-action"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('users.edit')
  })

  it('shows the role-management action with roles.update', () => {
    const wrapper = createWrapper(true)

    expect(wrapper.find('[data-testid="manage-roles-action"]').exists()).toBe(true)
  })

  it('shows credential recovery assistance only with users.update', () => {
    expect(createWrapper(false, true).find('[data-testid="credential-recovery-action"]').exists()).toBe(true)
    expect(createWrapper(false, false).find('[data-testid="credential-recovery-action"]').exists()).toBe(false)
  })

  it('requires the dedicated MFA-reset permission instead of users.update', async () => {
    const wrapper = createWrapper(false, true)
    await wrapper.setProps({ users: [{ id: 'mfa-user', roles: ['User'], isActive: true, twoFactorEnabled: true }] })
    expect(wrapper.find('[data-testid="reset-mfa-action"]').exists()).toBe(false)
    await wrapper.setProps({ canResetMfa: true, canUpdate: false })
    expect(wrapper.find('[data-testid="reset-mfa-action"]').exists()).toBe(true)
  })

  it('offers reset for a passkey-only user only with the dedicated permission', async () => {
    const wrapper = createWrapper(false, true)
    await wrapper.setProps({ users: [{ id: 'key-user', roles: ['User'], isActive: true, twoFactorEnabled: false, emailMfaEnabled: false, hasPasskey: true }] })
    expect(wrapper.find('[data-testid="reset-mfa-action"]').exists()).toBe(false)
    await wrapper.setProps({ canResetMfa: true, canUpdate: false })
    await wrapper.get('[data-testid="reset-mfa-action"]').trigger('click')
    expect(wrapper.emitted('reset-mfa')[0][0].id).toBe('key-user')
    wrapper.unmount()
  })

  it('explains all retired factors and both invalidated code families in each locale', () => {
    for (const word of ['TOTP', 'email', 'passkeys', 'security keys', 'authenticator recovery codes', 'account backup codes']) {
      expect(enUsers.mfa.resetConfirm).toContain(word)
    }
    for (const word of ['TOTP', '電子郵件', '通行密鑰', '安全金鑰', '驗證器救援碼', '帳號備用復原碼']) {
      expect(zhUsers.mfa.resetConfirm).toContain(word)
    }
  })
})


describe('Manual sync account selection', () => {
  it('hides controls without the combined synchronization permission', () => {
    const wrapper = createWrapper(false)
    expect(wrapper.find('[data-test="select-sync-user"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="manual-sync-action"]').exists()).toBe(false)
    wrapper.unmount()
  })
  it('selects only the current page, caps at 50, and resets on loading or filter/page changes', async () => {
    const wrapper = createWrapper(false)
    const users = Array.from({ length: 60 }, (_, index) => ({ id: `user-${index}`, userName: `example-${index}`, roles: [] }))
    await wrapper.setProps({ users, canSync: true })
    await wrapper.get('[data-test="select-page"]').setValue(true)
    await wrapper.get('[data-test="preview-selected"]').trigger('click')
    expect(wrapper.emitted('profile-sync')[0][0]).toHaveLength(50)
    expect(wrapper.findAll('[data-test="select-sync-user"]')[50].element.disabled).toBe(true)
    for (const props of [{ page: 2 }, { search: 'example' }, { loading: true }, { loading: false }]) {
      await wrapper.setProps(props)
      if (!props.loading) expect(wrapper.get('[data-test="preview-selected"]').element.disabled).toBe(true)
    }
    await wrapper.get('[data-test="manual-sync-action"]').trigger('click')
    expect(wrapper.emitted('profile-sync').at(-1)[0]).toEqual([users[0]])
    wrapper.unmount()
  })
})
