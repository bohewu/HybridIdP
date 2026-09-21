import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AuditLogViewer from './AuditLogViewer.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key) => key })
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
