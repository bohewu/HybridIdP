/**
 * Resource categories for localization.
 * Mirrors Core.Domain.Constants.ResourceCategories
 */
export const ResourceCategories = {
  Consent: 'Consent',
  LoginNotice: 'LoginNotice',
  Email: 'Email',
  Error: 'Error',
  UI: 'UI',
  Validation: 'Validation',
  Navigation: 'Navigation',
  Notification: 'Notification',
  Custom: 'Custom'
}

/**
 * Get all resource categories as an array
 */
export function getAllCategories() {
  return Object.values(ResourceCategories)
}
