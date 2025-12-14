import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import {
    useIsInteractive,
    usePermissionsByCategory,
    useLocalAllowedScopes,
    validateUriList,
    availablePermissions,
    useClientFormLogic
} from '../useClientFormLogic'

describe('useClientFormLogic', () => {
    // Helper to create mock formData
    const createFormData = (overrides = {}) => ref({
        clientId: '',
        displayName: '',
        applicationType: 'web',
        clientType: 'public',
        consentType: 'explicit',
        redirectUris: '',
        postLogoutRedirectUris: '',
        permissions: [],
        requiredScopes: [],
        ...overrides
    })

    describe('useIsInteractive', () => {
        it('returns true for public client type', () => {
            const formData = createFormData({ clientType: 'public', permissions: [] })
            const isInteractive = useIsInteractive(formData)
            expect(isInteractive.value).toBe(true)
        })

        it('returns false for confidential client without interactive grants', () => {
            const formData = createFormData({
                clientType: 'confidential',
                permissions: ['gt:client_credentials']
            })
            const isInteractive = useIsInteractive(formData)
            expect(isInteractive.value).toBe(false)
        })

        it('returns true for confidential client with authorization_code grant', () => {
            const formData = createFormData({
                clientType: 'confidential',
                permissions: ['gt:authorization_code']
            })
            const isInteractive = useIsInteractive(formData)
            expect(isInteractive.value).toBe(true)
        })

        it('returns true for confidential client with implicit grant', () => {
            const formData = createFormData({
                clientType: 'confidential',
                permissions: ['gt:implicit']
            })
            const isInteractive = useIsInteractive(formData)
            expect(isInteractive.value).toBe(true)
        })

        it('returns true when both authorization_code and client_credentials are present', () => {
            const formData = createFormData({
                clientType: 'confidential',
                permissions: ['gt:authorization_code', 'gt:client_credentials']
            })
            const isInteractive = useIsInteractive(formData)
            expect(isInteractive.value).toBe(true)
        })
    })

    describe('usePermissionsByCategory', () => {
        it('includes all endpoints for interactive clients', () => {
            const isInteractive = ref(true)
            const grouped = usePermissionsByCategory(isInteractive)

            expect(grouped.value.endpoints).toBeDefined()
            expect(grouped.value.endpoints.some(p => p.value === 'ept:authorization')).toBe(true)
            expect(grouped.value.endpoints.some(p => p.value === 'ept:end_session')).toBe(true)
            expect(grouped.value.endpoints.some(p => p.value === 'ept:token')).toBe(true)
        })

        it('hides authorization and end_session endpoints for M2M clients', () => {
            const isInteractive = ref(false)
            const grouped = usePermissionsByCategory(isInteractive)

            expect(grouped.value.endpoints).toBeDefined()
            expect(grouped.value.endpoints.some(p => p.value === 'ept:authorization')).toBe(false)
            expect(grouped.value.endpoints.some(p => p.value === 'ept:end_session')).toBe(false)
            // Token endpoint should still be visible
            expect(grouped.value.endpoints.some(p => p.value === 'ept:token')).toBe(true)
        })

        it('always includes grant types regardless of interactivity', () => {
            const isInteractive = ref(false)
            const grouped = usePermissionsByCategory(isInteractive)

            expect(grouped.value.grantTypes).toBeDefined()
            expect(grouped.value.grantTypes.length).toBeGreaterThan(0)
            expect(grouped.value.grantTypes.some(p => p.value === 'gt:client_credentials')).toBe(true)
        })
    })

    describe('useLocalAllowedScopes', () => {
        it('extracts scope names from permissions', () => {
            const formData = createFormData({
                permissions: ['scp:openid', 'scp:profile', 'gt:authorization_code']
            })
            const scopes = useLocalAllowedScopes(formData)

            expect(scopes.value).toEqual(['openid', 'profile'])
        })

        it('returns empty array when no scopes', () => {
            const formData = createFormData({
                permissions: ['gt:authorization_code']
            })
            const scopes = useLocalAllowedScopes(formData)

            expect(scopes.value).toEqual([])
        })

        it('sets scopes correctly (preserving non-scope permissions)', () => {
            const formData = createFormData({
                permissions: ['gt:authorization_code', 'scp:openid']
            })
            const scopes = useLocalAllowedScopes(formData)

            scopes.value = ['email', 'profile']

            expect(formData.value.permissions).toContain('gt:authorization_code')
            expect(formData.value.permissions).toContain('scp:email')
            expect(formData.value.permissions).toContain('scp:profile')
            expect(formData.value.permissions).not.toContain('scp:openid')
        })
    })

    describe('validateUriList', () => {
        it('does not add issues for valid URIs', () => {
            const ctx = { addIssue: vi.fn() }
            validateUriList('https://example.com\nhttps://localhost:5000', ctx, 'redirectUris', 'Invalid URI on line {line}')

            expect(ctx.addIssue).not.toHaveBeenCalled()
        })

        it('adds issue for invalid URI', () => {
            const ctx = { addIssue: vi.fn() }
            validateUriList('not-a-valid-uri', ctx, 'redirectUris', 'Invalid URI on line {line}')

            expect(ctx.addIssue).toHaveBeenCalledWith({
                code: 'custom',
                message: 'Invalid URI on line 1',
                path: ['redirectUris']
            })
        })

        it('handles mixed valid and invalid URIs', () => {
            const ctx = { addIssue: vi.fn() }
            validateUriList('https://example.com\ninvalid\nhttps://localhost', ctx, 'redirectUris', 'Invalid URI on line {line}')

            expect(ctx.addIssue).toHaveBeenCalledTimes(1)
            expect(ctx.addIssue).toHaveBeenCalledWith({
                code: 'custom',
                message: 'Invalid URI on line 2',
                path: ['redirectUris']
            })
        })

        it('ignores empty lines', () => {
            const ctx = { addIssue: vi.fn() }
            validateUriList('https://example.com\n\n\nhttps://localhost', ctx, 'redirectUris', 'Invalid URI on line {line}')

            expect(ctx.addIssue).not.toHaveBeenCalled()
        })
    })

    describe('availablePermissions', () => {
        it('contains endpoint permissions', () => {
            const endpoints = availablePermissions.filter(p => p.category === 'endpoints')
            expect(endpoints.length).toBeGreaterThan(0)
            expect(endpoints.some(p => p.value === 'ept:token')).toBe(true)
        })

        it('contains grant type permissions', () => {
            const grantTypes = availablePermissions.filter(p => p.category === 'grantTypes')
            expect(grantTypes.length).toBeGreaterThan(0)
            expect(grantTypes.some(p => p.value === 'gt:authorization_code')).toBe(true)
            expect(grantTypes.some(p => p.value === 'gt:client_credentials')).toBe(true)
        })
    })

    describe('useClientFormLogic (integration)', () => {
        it('returns all expected properties', () => {
            const formData = createFormData()
            const result = useClientFormLogic(formData)

            expect(result.isNative).toBeDefined()
            expect(result.isInteractive).toBeDefined()
            expect(result.permissionsByCategory).toBeDefined()
            expect(result.localAllowedScopes).toBeDefined()
            expect(result.availablePermissions).toBeDefined()
        })

        it('isNative returns true for native app', () => {
            const formData = createFormData({ applicationType: 'native' })
            const { isNative } = useClientFormLogic(formData)

            expect(isNative.value).toBe(true)
        })

        it('isNative returns false for web app', () => {
            const formData = createFormData({ applicationType: 'web' })
            const { isNative } = useClientFormLogic(formData)

            expect(isNative.value).toBe(false)
        })
    })
})
