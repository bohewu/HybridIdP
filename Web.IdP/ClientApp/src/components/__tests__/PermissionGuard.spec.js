import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import PermissionGuard from '../PermissionGuard.vue'
import { permissionService } from '@/utils/permissionService'

// Mock the permission service
vi.mock('@/utils/permissionService', () => ({
  permissionService: {
    hasPermission: vi.fn(),
    hasAllPermissions: vi.fn(),
    hasAnyPermission: vi.fn()
  }
}))

describe('PermissionGuard.vue', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders slot content when permission is granted', () => {
    permissionService.hasPermission.mockReturnValue(true)

    const wrapper = mount(PermissionGuard, {
      props: { permission: 'users.read' },
      slots: { default: '<div id="content">Secure Content</div>' }
    })

    expect(wrapper.find('#content').exists()).toBe(true)
    expect(wrapper.text()).toContain('Secure Content')
    expect(permissionService.hasPermission).toHaveBeenCalledWith('users.read')
  })

  it('does not render slot content when permission is denied', () => {
    permissionService.hasPermission.mockReturnValue(false)

    const wrapper = mount(PermissionGuard, {
      props: { permission: 'users.read' },
      slots: { default: '<div id="content">Secure Content</div>' }
    })

    expect(wrapper.find('#content').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Secure Content')
  })

  it('shows denied message when showDenied is true and access denied', () => {
    permissionService.hasPermission.mockReturnValue(false)

    const wrapper = mount(PermissionGuard, {
      props: { 
        permission: 'users.read',
        showDenied: true 
      }
    })

    expect(wrapper.find('.permission-denied').exists()).toBe(true)
    expect(wrapper.text()).toContain("You don't have permission")
  })

  it('checks multiple permissions (Any logic by default)', () => {
    permissionService.hasAnyPermission.mockReturnValue(true)

    const wrapper = mount(PermissionGuard, {
      props: { permissions: ['users.read', 'users.write'] },
      slots: { default: 'Content' }
    })

    expect(wrapper.text()).toContain('Content')
    expect(permissionService.hasAnyPermission).toHaveBeenCalledWith(['users.read', 'users.write'])
  })

  it('checks multiple permissions (All logic when requireAll is true)', () => {
    permissionService.hasAllPermissions.mockReturnValue(true)

    const wrapper = mount(PermissionGuard, {
      props: { 
        permissions: ['users.read', 'users.write'],
        requireAll: true
      },
      slots: { default: 'Content' }
    })

    expect(wrapper.text()).toContain('Content')
    expect(permissionService.hasAllPermissions).toHaveBeenCalledWith(['users.read', 'users.write'])
  })
  
  it('allows access by default if no permissions specified', () => {
     const wrapper = mount(PermissionGuard, {
      slots: { default: 'Public Content' }
    })
    
    expect(wrapper.text()).toContain('Public Content')
  })
})
