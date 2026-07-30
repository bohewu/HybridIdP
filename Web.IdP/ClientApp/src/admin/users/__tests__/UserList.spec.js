import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import UserList from '../components/UserList.vue'

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

const createWrapper = (canManageRoles) => mount(UserList, {
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
    canUpdate: true,
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
})
