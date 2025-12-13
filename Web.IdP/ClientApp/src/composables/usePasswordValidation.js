
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

export function usePasswordValidation(props) {
  const { t } = useI18n()

  const validatePasswordComplexity = (pwd, policy) => {
    const validationErrors = []
    if (!policy) return []

    if (policy.minPasswordLength && pwd.length < policy.minPasswordLength) {
      validationErrors.push(t('users.passwordErrors.minLength', { n: policy.minPasswordLength }))
    }
    if (policy.requireUppercase && !/[A-Z]/.test(pwd)) {
      validationErrors.push(t('users.passwordErrors.uppercase'))
    }
    if (policy.requireLowercase && !/[a-z]/.test(pwd)) {
      validationErrors.push(t('users.passwordErrors.lowercase'))
    }
    if (policy.requireDigit && !/[0-9]/.test(pwd)) {
      validationErrors.push(t('users.passwordErrors.digit'))
    }
    if (policy.requireNonAlphanumeric && !/[^A-Za-z0-9]/.test(pwd)) {
      validationErrors.push(t('users.passwordErrors.specialChar'))
    }
    
    // Min Character Types Check
    if (policy.minCharacterTypes > 0) {
      const typesCount = [
        /[A-Z]/.test(pwd),
        /[a-z]/.test(pwd),
        /[0-9]/.test(pwd),
        /[^A-Za-z0-9]/.test(pwd)
      ].filter(Boolean).length
      
      if (typesCount < policy.minCharacterTypes) {
        validationErrors.push(t('users.passwordErrors.minCharacterTypes', { n: policy.minCharacterTypes }))
      }
    }

    return validationErrors
  }

  const getPasswordRequirements = (pwd, policy) => {
    if (!policy) return []
    const reqs = []
    
    if (policy.minPasswordLength > 0) {
      reqs.push({
        text: t('users.passwordReqs.minLength', { n: policy.minPasswordLength }),
        valid: pwd.length >= policy.minPasswordLength
      })
    }
    if (policy.requireUppercase) {
      reqs.push({
        text: t('users.passwordReqs.uppercase'),
        valid: /[A-Z]/.test(pwd)
      })
    }
    if (policy.requireLowercase) {
      reqs.push({
        text: t('users.passwordReqs.lowercase'),
        valid: /[a-z]/.test(pwd)
      })
    }
    if (policy.requireDigit) {
      reqs.push({
        text: t('users.passwordReqs.digit'),
        valid: /[0-9]/.test(pwd)
      })
    }
    if (policy.requireNonAlphanumeric) {
      reqs.push({
        text: t('users.passwordReqs.specialChar'),
        valid: /[^A-Za-z0-9]/.test(pwd)
      })
    }
    
    if (policy.minCharacterTypes > 0) {
      const typesCount = [
        /[A-Z]/.test(pwd),
        /[a-z]/.test(pwd),
        /[0-9]/.test(pwd),
        /[^A-Za-z0-9]/.test(pwd)
      ].filter(Boolean).length
      
      reqs.push({
        text: t('users.passwordReqs.minCharacterTypes', { n: policy.minCharacterTypes }),
        valid: typesCount >= policy.minCharacterTypes
      })
    }
    
    return reqs
  }

  return {
    validatePasswordComplexity,
    getPasswordRequirements
  }
}
