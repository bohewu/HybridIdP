import { describe, it, expect, vi } from 'vitest'
import { useIdentityValidation } from '../useIdentityValidation'

// Mock vue-i18n
vi.mock('vue-i18n', () => ({
    useI18n: () => ({
        t: (key) => key
    })
}))

describe('useIdentityValidation', () => {
    const {
        validateTaiwanNationalId,
        validatePassportNumber,
        validateResidentCertificate
    } = useIdentityValidation()

    describe('validateTaiwanNationalId', () => {
        it('should pass for valid National ID (A123456789)', () => {
            const result = validateTaiwanNationalId('A123456789')
            expect(result.valid).toBe(true)
            expect(result.error).toBeNull()
        })

        it('should fail for invalid length', () => {
            const result = validateTaiwanNationalId('A12345678')
            expect(result.valid).toBe(false)
            expect(result.error).toContain('persons.validation.nationalIdFormat')
        })

        it('should fail for invalid format (no letter start)', () => {
            const result = validateTaiwanNationalId('0123456789')
            expect(result.valid).toBe(false)
            expect(result.error).toContain('persons.validation.nationalIdFormat')
        })

        it('should fail for invalid checksum', () => {
            // A123456788 (last digit changed from 9 to 8)
            // A123456789 sum is 130.
            // Changing last digit 9 -> 8 decreases sum by 1 -> 129. 129 % 10 != 0.
            const result = validateTaiwanNationalId('A123456788')
            expect(result.valid).toBe(false)
            expect(result.error).toContain('persons.validation.nationalIdChecksum')
        })

        it('should return valid/null for empty input', () => {
            const result = validateTaiwanNationalId('')
            expect(result.valid).toBe(true)
        })
    })

    describe('validatePassportNumber', () => {
        it('should pass for valid format', () => {
            expect(validatePassportNumber('300123456').valid).toBe(true)
            expect(validatePassportNumber('AB1234567').valid).toBe(true)
        })

        it('should fail for invalid format (too short)', () => {
            expect(validatePassportNumber('AB123').valid).toBe(false)
        })

        it('should fail for invalid format (non-alphanumeric)', () => {
            expect(validatePassportNumber('AB123!').valid).toBe(false)
        })
    })
})
