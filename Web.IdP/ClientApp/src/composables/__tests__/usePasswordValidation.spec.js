import { describe, it, expect, vi } from 'vitest'
import { usePasswordValidation } from '../usePasswordValidation'

// Mock vue-i18n
vi.mock('vue-i18n', () => ({
    useI18n: () => ({
        t: (key) => key
    })
}))

describe('usePasswordValidation', () => {
    const { validatePasswordComplexity, getPasswordRequirements } = usePasswordValidation()

    const strictPolicy = {
        minPasswordLength: 8,
        requireUppercase: true,
        requireLowercase: true,
        requireDigit: true,
        requireNonAlphanumeric: true,
        minCharacterTypes: 3
    }

    describe('validatePasswordComplexity', () => {
        it('should return errors for short password', () => {
            const errors = validatePasswordComplexity('Short1!', strictPolicy)
            expect(errors).toContain('users.passwordErrors.minLength')
        })

        it('should return error for missing uppercase', () => {
            const errors = validatePasswordComplexity('lowercase1!', strictPolicy)
            expect(errors).toContain('users.passwordErrors.uppercase')
        })

        it('should return error for missing lowercase', () => {
            const errors = validatePasswordComplexity('UPPERCASE1!', strictPolicy)
            expect(errors).toContain('users.passwordErrors.lowercase')
        })

        it('should return error for missing digit', () => {
            const errors = validatePasswordComplexity('NoDigit!!', strictPolicy)
            expect(errors).toContain('users.passwordErrors.digit')
        })

        it('should return error for missing special char', () => {
            const errors = validatePasswordComplexity('NoSpecial1', strictPolicy)
            expect(errors).toContain('users.passwordErrors.specialChar')
        })

        it('should pass for valid password', () => {
            const errors = validatePasswordComplexity('Valid1!Password', strictPolicy)
            expect(errors).toHaveLength(0)
        })
    })

    describe('getPasswordRequirements', () => {
        it('should return requirement objects', () => {
            const reqs = getPasswordRequirements('test', strictPolicy)
            expect(reqs.length).toBeGreaterThan(0)
            expect(reqs[0]).toHaveProperty('text')
            expect(reqs[0]).toHaveProperty('valid')
        })

        it('should mark requirements as valid/invalid correctly', () => {
            // 'Valid1!Password' meets all requirements
            const reqs = getPasswordRequirements('Valid1!Password', strictPolicy)
            const invalidReqs = reqs.filter(r => !r.valid)
            expect(invalidReqs).toHaveLength(0)

            // 'weak' fails most
            const weakReqs = getPasswordRequirements('weak', strictPolicy)
            const validReqs = weakReqs.filter(r => r.valid)
            // only lowercase is met
            expect(validReqs).not.toHaveLength(weakReqs.length)
        })
    })
})
