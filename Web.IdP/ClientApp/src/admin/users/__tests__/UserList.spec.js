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
