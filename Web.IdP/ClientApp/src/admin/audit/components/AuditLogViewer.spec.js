import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AuditLogViewer from './AuditLogViewer.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key) => key, te: (key) => key !== 'audit.eventTypes.CustomEvent' })
}))

describe('AuditLogViewer recovery events', () => {
  it('renders a localized event and fixed outcome without free-form recovery details', () => {
    const wrapper = mount(AuditLogViewer, {
      props: {
        auditEvents: [{
          id: 'event-1',
          timestamp: '2026-09-05T10:00:00Z',
          eventType: 'AdminRecoveryAddressReplaced',
          user: 'operator-id',
          details: 'address=private@example.test; token=secret',
          ipAddress: null
        }],
        loading: false,
        page: 1,
        pageSize: 10,
        totalCount: 1
      },
      global: { stubs: { Pagination: true, LoadingIndicator: true } }
    })

    expect(wrapper.text()).toContain('audit.eventTypes.AdminRecoveryAddressReplaced')
    expect(wrapper.text()).toContain('audit.recovery.recordedOutcome')
    expect(wrapper.text()).not.toContain('private@example.test')
    expect(wrapper.text()).not.toContain('secret')
  })
})


it('owns one loading indicator while retaining and locking prior results', () => {
  const wrapper = mount(AuditLogViewer, {
    props: { auditEvents: [{ id: 'retained', user: 'retained-user' }], loading: true, page: 1, pageSize: 10, totalCount: 1 },
    global: { stubs: { Pagination: true } }
  })
  expect(wrapper.findAll('.animate-spin')).toHaveLength(1)
  expect(wrapper.get('[role="status"]').attributes('aria-label')).toBe('audit.loading')
  expect(wrapper.text()).toContain('retained-user')
  expect(wrapper.get('[inert]').exists()).toBe(true)
  expect(wrapper.attributes('aria-busy')).toBe('true')
  wrapper.unmount()
})
