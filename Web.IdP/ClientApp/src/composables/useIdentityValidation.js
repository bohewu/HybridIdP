import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * Composable for validating identity documents (Taiwan National ID, Passport, Resident Certificate)
 * Phase 10.6: Client-side validation for identity document fields
 */
export function useIdentityValidation() {
  const { t } = useI18n()

  /**
   * Validate Taiwan National ID (身分證字號)
   * Format: 1 letter + 9 digits (e.g., A123456789)
   * Includes checksum validation algorithm
   * 
   * Algorithm:
   * 1. First letter maps to 2 digits (A=10, B=11, ..., Z=33)
   * 2. First digit of mapping × 1, second digit × 9
   * 3. Following 9 digits × [8,7,6,5,4,3,2,1,1]
   * 4. Sum all results, modulo 10 should equal 0
   */
  const validateTaiwanNationalId = (nationalId) => {
    if (!nationalId) {
      return { valid: true, error: null }
    }

    const value = nationalId.trim().toUpperCase()

    // Format check: 1 letter + 9 digits
    if (!/^[A-Z][0-9]{9}$/.test(value)) {
      return {
        valid: false,
        error: t('persons.validation.nationalIdFormat')
      }
    }

    // Checksum validation
    const letterMapping = {
      A: 10, B: 11, C: 12, D: 13, E: 14, F: 15, G: 16, H: 17, I: 34, J: 18,
      K: 19, L: 20, M: 21, N: 22, O: 35, P: 23, Q: 24, R: 25, S: 26, T: 27,
      U: 28, V: 29, W: 32, X: 30, Y: 31, Z: 33
    }

    const letter = value[0]
    const letterValue = letterMapping[letter]

    if (!letterValue) {
      return {
        valid: false,
        error: t('persons.validation.nationalIdInvalidLetter')
      }
    }

    // Checksum validation
    // Logic must match IdentityDocumentValidator.cs

    // Convert letter to two digits
    const firstDigit = Math.floor(letterValue / 10)
    const secondDigit = letterValue % 10

    const weights = [1, 9, 8, 7, 6, 5, 4, 3, 2, 1] // 10 weights

    // Calculate weighted sum for the letter and first 8 digits
    let sum = firstDigit * weights[0] + secondDigit * weights[1]

    for (let i = 1; i < 9; i++) {
      sum += parseInt(value[i]) * weights[i + 1]
    }

    const lastDigit = parseInt(value[9])
    const checksum = (10 - (sum % 10)) % 10

    if (checksum !== lastDigit) {
      return {
        valid: false,
        error: t('persons.validation.nationalIdChecksum')
      }
    }

    return { valid: true, error: null }
  }

  /**
   * Validate Passport Number
   * Format: 6-12 alphanumeric characters
   * Common formats: 300123456 (Taiwan), AB1234567 (other countries)
   */
  const validatePassportNumber = (passportNumber) => {
    if (!passportNumber) {
      return { valid: true, error: null }
    }

    const value = passportNumber.trim()

    // Format check: 6-12 alphanumeric
    if (!/^[A-Z0-9]{6,12}$/i.test(value)) {
      return {
        valid: false,
        error: t('persons.validation.passportFormat')
      }
    }

    return { valid: true, error: null }
  }

  /**
   * Validate Resident Certificate Number (居留證號碼)
   * Format: 10-12 alphanumeric characters
   * Common format: AA12345678 (2 letters + 8 digits)
   */
  const validateResidentCertificate = (residentCertificate) => {
    if (!residentCertificate) {
      return { valid: true, error: null }
    }

    const value = residentCertificate.trim()

    // Format check: 10-12 alphanumeric
    if (!/^[A-Z0-9]{10,12}$/i.test(value)) {
      return {
        valid: false,
        error: t('persons.validation.residentCertFormat')
      }
    }

    return { valid: true, error: null }
  }

  /**
   * Validate all identity documents based on selected document type
   */
  const validateIdentityDocument = (documentType, formData) => {
    const errors = []

    // Validate based on selected document type
    if (documentType === 'NationalId') {
      if (!formData.nationalId) {
        errors.push(t('persons.validation.nationalIdRequired'))
      } else {
        const result = validateTaiwanNationalId(formData.nationalId)
        if (!result.valid) {
          errors.push(result.error)
        }
      }

      // Clear other document fields if present
      if (formData.passportNumber || formData.residentCertificateNumber) {
        errors.push(t('persons.validation.onlyOneDocumentType'))
      }
    } else if (documentType === 'Passport') {
      if (!formData.passportNumber) {
        errors.push(t('persons.validation.passportRequired'))
      } else {
        const result = validatePassportNumber(formData.passportNumber)
        if (!result.valid) {
          errors.push(result.error)
        }
      }

      // Clear other document fields if present
      if (formData.nationalId || formData.residentCertificateNumber) {
        errors.push(t('persons.validation.onlyOneDocumentType'))
      }
    } else if (documentType === 'ResidentCertificate') {
      if (!formData.residentCertificateNumber) {
        errors.push(t('persons.validation.residentCertRequired'))
      } else {
        const result = validateResidentCertificate(formData.residentCertificateNumber)
        if (!result.valid) {
          errors.push(result.error)
        }
      }

      // Clear other document fields if present
      if (formData.nationalId || formData.passportNumber) {
        errors.push(t('persons.validation.onlyOneDocumentType'))
      }
    }

    return {
      valid: errors.length === 0,
      errors
    }
  }

  return {
    validateTaiwanNationalId,
    validatePassportNumber,
    validateResidentCertificate,
    validateIdentityDocument
  }
}
