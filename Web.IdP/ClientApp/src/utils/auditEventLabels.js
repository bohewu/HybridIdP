const eventAliases = {
  'Account.RequiredPasswordChange': 'RequiredPasswordChange',
  UserCreated: 'AdminUserCreated',
  UserUpdated: 'AdminUserUpdated',
  UserDeleted: 'AdminUserDeleted',
  UserRoleChanged: 'AdminRoleChanged'
}

const recoveryEventTypes = new Set([
  'RecoveryAddressChangeStarted', 'RecoveryAddressVerified', 'RecoveryAddressRevoked',
  'AdminRecoveryAddressReplaced', 'AdminMigrationOtpResent',
  'AdminResetApprovalIssued', 'AdminResetApprovalConsumed'
])

export function auditEventTypeLabel(eventType, t, te) {
  const key = `audit.eventTypes.${eventAliases[eventType] || eventType}`
  return te(key) ? t(key) : eventType
}

export function auditEventDetails(event, t) {
  return recoveryEventTypes.has(event.eventType) ? t('audit.recovery.recordedOutcome') : event.details
}
