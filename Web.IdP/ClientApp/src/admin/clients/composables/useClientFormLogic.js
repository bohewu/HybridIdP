import { computed, watch } from 'vue'

/**
 * All available permissions from OpenIddict
 * @returns {Array} Permission definitions with value, labelKey, and category
 */
export const availablePermissions = [
    // Endpoints
    { value: 'ept:authorization', labelKey: 'authorizationEndpoint', category: 'endpoints' },
    { value: 'ept:token', labelKey: 'tokenEndpoint', category: 'endpoints' },
    { value: 'ept:end_session', labelKey: 'endSessionEndpoint', category: 'endpoints' },
    { value: 'ept:introspection', labelKey: 'introspectionEndpoint', category: 'endpoints' },
    { value: 'ept:revocation', labelKey: 'revocationEndpoint', category: 'endpoints' },
    { value: 'ept:device_authorization', labelKey: 'deviceAuthorizationEndpoint', category: 'endpoints' },
    { value: 'ept:pushed_authorization', labelKey: 'pushedAuthorizationEndpoint', category: 'endpoints' },

    // Grant Types
    { value: 'gt:authorization_code', labelKey: 'authorizationCode', category: 'grantTypes' },
    { value: 'gt:client_credentials', labelKey: 'clientCredentials', category: 'grantTypes' },
    { value: 'gt:refresh_token', labelKey: 'refreshToken', category: 'grantTypes' },
    { value: 'gt:urn:ietf:params:oauth:grant-type:device_code', labelKey: 'deviceCode', category: 'grantTypes' },
    { value: 'gt:password', labelKey: 'password', category: 'grantTypes' },
    { value: 'gt:implicit', labelKey: 'implicit', category: 'grantTypes' },
    { value: 'gt:urn:ietf:params:oauth:grant-type:token-exchange', labelKey: 'tokenExchange', category: 'grantTypes' },
]

/**
 * Determines if the client configuration is "interactive" (requires redirect URIs).
 * A client is interactive if:
 * - It uses Authorization Code flow
 * - It uses Implicit flow
 * - It is a Public client (Public clients are always interactive)
 * 
 * @param {Object} formData - Reactive form data object
 * @returns {ComputedRef<boolean>} Whether the client is interactive
 */
export function useIsInteractive(formData) {
    return computed(() => {
        return formData.value.permissions.includes('gt:authorization_code') ||
            formData.value.permissions.includes('gt:implicit') ||
            formData.value.clientType === 'public'
    })
}

/**
 * Groups permissions by category, filtering out irrelevant endpoints for M2M clients.
 * 
 * For non-interactive (M2M) clients:
 * - Hides 'ept:authorization' (Authorization Endpoint)
 * - Hides 'ept:end_session' (End Session/Logout Endpoint)
 * 
 * @param {ComputedRef<boolean>} isInteractive - Whether the client is interactive
 * @returns {ComputedRef<Object>} Permissions grouped by category
 */
export function usePermissionsByCategory(isInteractive) {
    return computed(() => {
        const grouped = {}
        availablePermissions.forEach(perm => {
            // Filter implicit endpoints for M2M (hide Authorization and EndSession if not interactive)
            if (!isInteractive.value) {
                if (perm.value === 'ept:authorization' || perm.value === 'ept:end_session') {
                    return
                }
            }

            if (!grouped[perm.category]) {
                grouped[perm.category] = []
            }
            grouped[perm.category].push(perm)
        })
        return grouped
    })
}

/**
 * Creates a computed property that bridges localAllowedScopes <-> permissions.
 * Converts between 'scp:scope' format (stored in permissions) and plain scope names.
 * 
 * @param {Ref} formData - Reactive form data object
 * @returns {WritableComputedRef<string[]>} Array of scope names (without 'scp:' prefix)
 */
export function useLocalAllowedScopes(formData) {
    return computed({
        get: () => {
            return formData.value.permissions
                .filter(p => p.startsWith('scp:'))
                .map(p => p.substring(4))
        },
        set: (newScopes) => {
            // 1. Remove all existing scope permissions
            const nonScopePermissions = formData.value.permissions.filter(p => !p.startsWith('scp:'))

            // 2. Add new scope permissions
            const newScopePermissions = newScopes.map(s => `scp:${s}`)

            // 3. Update formData
            formData.value.permissions = [...nonScopePermissions, ...newScopePermissions]
        }
    })
}

/**
 * Sets up a watcher that enforces Native apps must be Public clients.
 * When applicationType changes to 'native', automatically sets clientType to 'public'.
 * 
 * @param {Ref} formData - Reactive form data object
 */
export function useNativeAppEnforcement(formData) {
    watch(() => formData.value.applicationType, (newType) => {
        if (newType === 'native') {
            formData.value.clientType = 'public'
            formData.value.clientSecret = null
        }
    })
}

/**
 * Validates a list of URIs (one per line).
 * Used for redirectUris and postLogoutRedirectUris validation.
 * 
 * @param {string} text - Newline-separated list of URIs
 * @param {Object} ctx - Zod refinement context
 * @param {string} path - Field path for error reporting
 * @param {string} messageTemplate - Error message template (uses {line} placeholder)
 */
export function validateUriList(text, ctx, path, messageTemplate) {
    const lines = text.split('\n').map(l => l.trim()).filter(l => l)
    lines.forEach((line, index) => {
        try {
            new URL(line)
        } catch {
            ctx.addIssue({
                code: 'custom',
                message: messageTemplate.replace('{line}', String(index + 1)),
                path: [path]
            })
        }
    })
}

/**
 * Main composable that combines all client form logic.
 * 
 * @param {Ref} formData - Reactive form data object
 * @returns {Object} All computed properties and helpers
 */
export function useClientFormLogic(formData) {
    const isNative = computed(() => formData.value.applicationType === 'native')
    const isInteractive = useIsInteractive(formData)
    const permissionsByCategory = usePermissionsByCategory(isInteractive)
    const localAllowedScopes = useLocalAllowedScopes(formData)

    // Setup watchers
    useNativeAppEnforcement(formData)

    return {
        isNative,
        isInteractive,
        permissionsByCategory,
        localAllowedScopes,
        availablePermissions
    }
}
