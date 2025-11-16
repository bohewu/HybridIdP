<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { z } from 'zod'
import SecretDisplayModal from './SecretDisplayModal.vue'

const { t } = useI18n()

const props = defineProps({
  client: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['submit', 'cancel'])

const isEdit = computed(() => props.client !== null)
const formData = ref({
  clientId: '',
  displayName: '',
  applicationType: 'web', // Default to web
  clientType: 'public', // Default to public
  consentType: 'explicit', // Default to explicit
  redirectUris: '',
  postLogoutRedirectUris: '',
  permissions: [],
  allowedScopes: []
})

const generatedClientSecret = ref(null)
const showSecretModal = ref(false)

// State for available scopes
const availableScopes = ref([])
const scopesLoading = ref(false)
const scopesError = ref(null)

// All available permissions from OpenIddict
const availablePermissions = computed(() => [
  // Endpoints
  { value: 'ept:authorization', labelKey: 'authorizationEndpoint', category: 'endpoints' },
  { value: 'ept:token', labelKey: 'tokenEndpoint', category: 'endpoints' },
  { value: 'ept:logout', labelKey: 'logoutEndpoint', category: 'endpoints' },
  { value: 'ept:introspection', labelKey: 'introspectionEndpoint', category: 'endpoints' },
  { value: 'ept:revocation', labelKey: 'revocationEndpoint', category: 'endpoints' },
  { value: 'ept:device', labelKey: 'deviceEndpoint', category: 'endpoints' },
  // Grant Types
  { value: 'gt:authorization_code', labelKey: 'authorizationCode', category: 'grantTypes' },
  { value: 'gt:client_credentials', labelKey: 'clientCredentials', category: 'grantTypes' },
  { value: 'gt:refresh_token', labelKey: 'refreshToken', category: 'grantTypes' },
  { value: 'gt:device_code', labelKey: 'deviceCode', category: 'grantTypes' },
  { value: 'gt:password', labelKey: 'password', category: 'grantTypes' },
  { value: 'gt:implicit', labelKey: 'implicit', category: 'grantTypes' },
  // Scopes
  { value: 'scp:openid', labelKey: 'openid', category: 'scopes' },
  { value: 'scp:profile', labelKey: 'profile', category: 'scopes' },
  { value: 'scp:email', labelKey: 'email', category: 'scopes' },
  { value: 'scp:roles', labelKey: 'roles', category: 'scopes' }
])

// Group permissions by category
const permissionsByCategory = computed(() => {
  const grouped = {}
  availablePermissions.value.forEach(perm => {
    if (!grouped[perm.category]) {
      grouped[perm.category] = []
    }
    grouped[perm.category].push(perm)
  })
  return grouped
})

const submitting = ref(false)
const error = ref(null)
const fieldErrors = ref({}) // Per-field validation errors

// Zod schema for client form validation
const schema = computed(() => z.object({
  clientId: z.string().min(1, t('clients.form.clientIdRequired')),
  displayName: z.string().optional(),
  applicationType: z.enum(['web', 'native']),
  clientType: z.enum(['public', 'confidential']),
  consentType: z.enum(['explicit', 'implicit']).optional(),
  redirectUris: z.string().min(1, t('clients.form.redirectUrisRequired')),
  postLogoutRedirectUris: z.string().optional(),
  permissions: z.array(z.string()).min(1, t('clients.form.permissionsRequired')),
  allowedScopes: z.array(z.string()).min(1, t('clients.form.allowedScopesRequired'))
}).superRefine((val, ctx) => {
  // Validate Redirect URIs lines
  const redirectLines = (val.redirectUris || '').split('\n').map(x => x.trim()).filter(x => x.length > 0)
  if (redirectLines.length === 0) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['redirectUris'], message: t('clients.form.redirectUrisRequired') })
  }
  redirectLines.forEach((u, i) => {
    try { new URL(u) } catch { ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['redirectUris'], message: t('clients.form.redirectUrisInvalid', { line: i + 1 }) }) }
  })
  // Validate Post Logout Redirect URIs if present
  const postLines = (val.postLogoutRedirectUris || '').split('\n').map(x => x.trim()).filter(x => x.length > 0)
  postLines.forEach((u, i) => {
    try { new URL(u) } catch { ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['postLogoutRedirectUris'], message: t('clients.form.postLogoutRedirectUrisInvalid', { line: i + 1 }) }) }
  })
  // Validate that openid scope is included for OIDC clients
  if (val.allowedScopes && !val.allowedScopes.includes('openid')) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['allowedScopes'], message: t('clients.form.allowedScopesOpenidRequired') })
  }
}))

const resetForm = () => {
  formData.value = {
    clientId: '',
    displayName: '',
    applicationType: 'web',
    clientType: 'public',
    redirectUris: '',
    postLogoutRedirectUris: '',
    permissions: [],
    allowedScopes: []
  }
  error.value = null
  fieldErrors.value = {}
  generatedClientSecret.value = null
  showSecretModal.value = false
}

watch(() => props.client, async (newClient) => {
  if (newClient) {
    formData.value = {
      clientId: newClient.clientId || '',
      displayName: newClient.displayName || '',
      applicationType: newClient.applicationType || 'web',
      clientType: newClient.type === 'confidential' ? 'confidential' : 'public',
      consentType: newClient.consentType || 'explicit',
      redirectUris: newClient.redirectUris?.join('\n') || '',
      postLogoutRedirectUris: newClient.postLogoutRedirectUris?.join('\n') || '',
      permissions: newClient.permissions || [],
      allowedScopes: []
    }
    
    // Fetch allowed scopes for this client in edit mode
    if (newClient.id) {
      try {
        const response = await fetch(`/api/admin/clients/${newClient.id}/scopes`)
        if (response.ok) {
          const data = await response.json()
          formData.value.allowedScopes = data.scopes || []
        }
      } catch (e) {
        console.error('Failed to fetch client allowed scopes:', e)
      }
    }
  } else {
    resetForm()
  }
}, { immediate: true })

const handleSubmit = async () => {
  submitting.value = true
  error.value = null
  fieldErrors.value = {}
  generatedClientSecret.value = null

  try {
    // Validate with Zod schema
    const parsed = schema.value.safeParse(formData.value)
    if (!parsed.success) {
      // Group errors by field path
      parsed.error.issues.forEach(issue => {
        const field = issue.path[0] || 'general'
        if (!fieldErrors.value[field]) {
          fieldErrors.value[field] = []
        }
        fieldErrors.value[field].push(issue.message)
      })
      throw new Error(t('clients.form.validationError'))
    }

    const payload = {
      clientId: formData.value.clientId,
      displayName: formData.value.displayName || null,
      applicationType: formData.value.applicationType,
      type: formData.value.clientType,
      consentType: formData.value.consentType,
      clientSecret: null, // Always send null, backend will generate if needed
      redirectUris: formData.value.redirectUris
        .split('\n')
        .map(uri => uri.trim())
        .filter(uri => uri.length > 0),
      postLogoutRedirectUris: formData.value.postLogoutRedirectUris
        .split('\n')
        .map(uri => uri.trim())
        .filter(uri => uri.length > 0),
      permissions: formData.value.permissions
    }

    const url = isEdit.value
      ? `/api/admin/clients/${props.client.id}`
      : '/api/admin/clients'
    
    const method = isEdit.value ? 'PUT' : 'POST'

    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(`HTTP error! status: ${response.status}, ${errorText}`)
    }

    const responseData = await response.json()

    // Save allowed scopes via dedicated endpoint
    const clientId = isEdit.value ? props.client.id : responseData.id
    if (clientId && formData.value.allowedScopes.length > 0) {
      try {
        const scopesResponse = await fetch(`/api/admin/clients/${clientId}/scopes`, {
          method: 'PUT',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({ scopes: formData.value.allowedScopes })
        })
        
        if (!scopesResponse.ok) {
          throw new Error(`Failed to save allowed scopes: ${scopesResponse.status}`)
        }
      } catch (e) {
        console.error('Error saving allowed scopes:', e)
        throw new Error(`Client saved but failed to update allowed scopes: ${e.message}`)
      }
    }

    if (!isEdit.value && responseData.clientSecret) {
      generatedClientSecret.value = responseData.clientSecret
      showSecretModal.value = true
    } else {
      emit('submit')
    }
  } catch (e) {
    error.value = `Failed to save client: ${e.message}`
    console.error('Error saving client:', e)
  } finally {
    submitting.value = false
  }
}

const togglePermission = (permission) => {
  const index = formData.value.permissions.indexOf(permission)
  if (index > -1) {
    formData.value.permissions.splice(index, 1)
  } else {
    formData.value.permissions.push(permission)
  }
}

const closeSecretModal = () => {
  showSecretModal.value = false
  emit('submit') // Signal to parent that form submission is complete
}

// Fetch available scopes
const fetchAvailableScopes = async () => {
  scopesLoading.value = true
  scopesError.value = null
  
  try {
    const response = await fetch('/api/admin/scopes?skip=0&take=1000')
    if (!response.ok) {
      throw new Error(`Failed to fetch scopes: ${response.status}`)
    }
    const data = await response.json()
    availableScopes.value = data.items || []
  } catch (e) {
    scopesError.value = `Failed to load scopes: ${e.message}`
    console.error('Error fetching scopes:', e)
  } finally {
    scopesLoading.value = false
  }
}

// Categorize scopes based on standard identity scopes and API resources
const categorizedScopes = computed(() => {
  const identityScopes = ['openid', 'profile', 'email', 'address', 'phone', 'offline_access']
  const categories = {
    identity: [],
    apiResource: [],
    custom: []
  }
  
  availableScopes.value.forEach(scope => {
    const scopeName = scope.name
    if (identityScopes.includes(scopeName)) {
      categories.identity.push(scope)
    } else if (scope.resources && scope.resources.length > 0) {
      // Scopes with resources are API Resource scopes
      categories.apiResource.push(scope)
    } else {
      categories.custom.push(scope)
    }
  })
  
  return categories
})

const toggleAllowedScope = (scopeName) => {
  const index = formData.value.allowedScopes.indexOf(scopeName)
  if (index > -1) {
    formData.value.allowedScopes.splice(index, 1)
  } else {
    formData.value.allowedScopes.push(scopeName)
  }
}

// Fetch scopes when component mounts
;(async () => {
  await fetchAvailableScopes()
})()
</script>

<template>
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-2xl">
          <form @submit.prevent="handleSubmit">
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    {{ $t(isEdit ? 'clients.form.editTitle' : 'clients.form.createTitle') }}
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <div class="flex">
                      <div class="flex-shrink-0">
                        <svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
                        </svg>
                      </div>
                      <div class="ml-3">
                        <p class="text-sm text-red-700 font-medium">{{ error }}</p>
                      </div>
                    </div>
                  </div>

                  <div class="space-y-5">
                    <!-- Client ID -->
                    <div class="mb-5">
                      <label for="clientId" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.clientId') }} <span class="text-red-500">*</span>
                      </label>
                      <input
                        id="clientId"
                        v-model="formData.clientId"
                        type="text"
                        required
                        :disabled="isEdit"
                        :class="[
                          'block w-full rounded-md shadow-sm sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed',
                          fieldErrors.clientId 
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500' 
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        :placeholder="$t('clients.form.clientIdPlaceholder')"
                      />
                      <p v-if="!fieldErrors.clientId" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.clientIdHelp') }}</p>
                      <div v-if="fieldErrors.clientId" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.clientId" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <!-- Display Name -->
                    <div class="mb-5">
                      <label for="displayName" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.displayName') }}
                      </label>
                      <input
                        id="displayName"
                        v-model="formData.displayName"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                        :placeholder="$t('clients.form.displayNamePlaceholder')"
                      />
                    </div>

                    <!-- Application Type -->
                    <div v-if="!isEdit">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.applicationType') }} <span class="text-red-500">*</span>
                      </label>
                      <div class="space-y-2">
                        <div class="flex items-center">
                          <input
                            id="app-type-web"
                            v-model="formData.applicationType"
                            type="radio"
                            value="web"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="app-type-web" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.applicationTypeWeb') }}</span> - {{ $t('clients.form.applicationTypeWebDesc') }}
                          </label>
                        </div>
                        <div class="flex items-center">
                          <input
                            id="app-type-native"
                            v-model="formData.applicationType"
                            type="radio"
                            value="native"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="app-type-native" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.applicationTypeNative') }}</span> - {{ $t('clients.form.applicationTypeNativeDesc') }}
                          </label>
                        </div>
                      </div>
                    </div>

                    <!-- Client Type -->
                    <div v-if="!isEdit">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.clientType') }} <span class="text-red-500">*</span>
                      </label>
                      <div class="space-y-2">
                        <div class="flex items-center">
                          <input
                            id="type-public"
                            v-model="formData.clientType"
                            type="radio"
                            value="public"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="type-public" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.clientTypePublic') }}</span> - {{ $t('clients.form.clientTypePublicDesc') }}
                          </label>
                        </div>
                        <div class="flex items-center">
                          <input
                            id="type-confidential"
                            v-model="formData.clientType"
                            type="radio"
                            value="confidential"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="type-confidential" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.clientTypeConfidential') }}</span> - {{ $t('clients.form.clientTypeConfidentialDesc') }}
                          </label>
                        </div>
                      </div>
                       <p v-if="formData.clientType === 'confidential'" class="mt-2 text-xs text-gray-500">
                        {{ $t('clients.form.clientSecretHelp') }}
                      </p>
                    </div>

                    <!-- Consent Type -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.consentType') }}
                      </label>
                      <div class="space-y-2">
                        <div class="flex items-center">
                          <input
                            id="consent-explicit"
                            v-model="formData.consentType"
                            type="radio"
                            value="explicit"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="consent-explicit" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.consentTypeExplicit') }}</span> - {{ $t('clients.form.consentTypeExplicitDesc') }}
                          </label>
                        </div>
                        <div class="flex items-center">
                          <input
                            id="consent-implicit"
                            v-model="formData.consentType"
                            type="radio"
                            value="implicit"
                            class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                          />
                          <label for="consent-implicit" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.consentTypeImplicit') }}</span> - {{ $t('clients.form.consentTypeImplicitDesc') }}
                          </label>
                        </div>
                      </div>
                      <p class="mt-2 text-xs text-gray-500">
                        {{ $t('clients.form.consentTypeHelp') }}
                      </p>
                    </div>

                    <!-- Redirect URIs -->
                    <div class="mb-5">
                      <label for="redirectUris" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.redirectUris') }} <span class="text-red-500">*</span>
                      </label>
                      <textarea
                        id="redirectUris"
                        v-model="formData.redirectUris"
                        required
                        rows="3"
                        :class="[
                          'block w-full rounded-md shadow-sm sm:text-sm px-3 py-2',
                          fieldErrors.redirectUris
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        :placeholder="$t('clients.form.redirectUrisPlaceholder')"
                      ></textarea>
                      <p v-if="!fieldErrors.redirectUris" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.redirectUrisHelp') }}</p>
                      <div v-if="fieldErrors.redirectUris" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.redirectUris" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <!-- Post Logout Redirect URIs -->
                    <div class="mb-5">
                      <label for="postLogoutRedirectUris" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.postLogoutRedirectUris') }}
                      </label>
                      <textarea
                        id="postLogoutRedirectUris"
                        v-model="formData.postLogoutRedirectUris"
                        rows="2"
                        :class="[
                          'block w-full rounded-md shadow-sm sm:text-sm px-3 py-2',
                          fieldErrors.postLogoutRedirectUris
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        :placeholder="$t('clients.form.postLogoutRedirectUrisPlaceholder')"
                      ></textarea>
                      <p v-if="!fieldErrors.postLogoutRedirectUris" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.postLogoutRedirectUrisHelp') }}</p>
                      <div v-if="fieldErrors.postLogoutRedirectUris" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.postLogoutRedirectUris" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <!-- Permissions -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.permissions') }} <span class="text-red-500">*</span>
                      </label>
                      <div v-if="fieldErrors.permissions" class="mb-2">
                        <p v-for="(err, idx) in fieldErrors.permissions" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                      
                      <!-- Grouped by category -->
                      <div class="space-y-4">
                        <div v-for="(permissions, category) in permissionsByCategory" :key="category">
                          <h4 class="text-xs font-semibold text-gray-600 uppercase tracking-wide mb-2">
                            {{ $t(`clients.form.categories.${category}`) }}
                          </h4>
                          <div class="grid grid-cols-2 gap-2">
                            <div
                              v-for="permission in permissions"
                              :key="permission.value"
                              class="relative flex items-start"
                            >
                              <div class="flex h-5 items-center">
                                <input
                                  :id="permission.value"
                                  type="checkbox"
                                  :checked="formData.permissions.includes(permission.value)"
                                  @change="togglePermission(permission.value)"
                                  class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                                />
                              </div>
                              <div class="ml-3 text-sm">
                                <label :for="permission.value" class="font-medium text-gray-700">
                                  {{ $t(`clients.form.permissionLabels.${permission.labelKey}`) }}
                                </label>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                      
                      <p class="mt-3 text-xs text-gray-500 border-t pt-2" v-html="$t('clients.form.permissionsHelp')"></p>
                    </div>

                    <!-- Allowed Scopes -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.allowedScopes') }} <span class="text-red-500">*</span>
                      </label>
                      <div v-if="fieldErrors.allowedScopes" class="mb-2">
                        <p v-for="(err, idx) in fieldErrors.allowedScopes" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                      
                      <!-- Loading state -->
                      <div v-if="scopesLoading" class="text-sm text-gray-500 py-4">
                        <svg class="animate-spin inline-block h-4 w-4 mr-2" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                        </svg>
                        {{ $t('clients.form.allowedScopesLoading') }}
                      </div>
                      
                      <!-- Error state -->
                      <div v-else-if="scopesError" class="text-sm text-red-600 py-2">
                        {{ scopesError }}
                      </div>
                      
                      <!-- Scopes grouped by category -->
                      <div v-else-if="availableScopes.length > 0" class="space-y-4">
                        <div v-for="(scopes, category) in categorizedScopes" :key="category" v-show="scopes.length > 0">
                          <h4 class="text-xs font-semibold text-gray-600 uppercase tracking-wide mb-2">
                            {{ $t(`clients.form.scopeCategories.${category}`) }}
                          </h4>
                          <div class="grid grid-cols-2 gap-2">
                            <div
                              v-for="scope in scopes"
                              :key="scope.name"
                              class="relative flex items-start"
                            >
                              <div class="flex h-5 items-center">
                                <input
                                  :id="`scope-${scope.name}`"
                                  type="checkbox"
                                  :checked="formData.allowedScopes.includes(scope.name)"
                                  @change="toggleAllowedScope(scope.name)"
                                  class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                                />
                              </div>
                              <div class="ml-3 text-sm">
                                <label :for="`scope-${scope.name}`" class="font-medium text-gray-700">
                                  {{ scope.displayName || scope.name }}
                                </label>
                                <p v-if="scope.description" class="text-xs text-gray-500">{{ scope.description }}</p>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                      
                      <!-- No scopes available -->
                      <div v-else class="text-sm text-gray-500 py-2">
                        {{ $t('clients.form.allowedScopesNone') }}
                      </div>
                      
                      <p class="mt-3 text-xs text-gray-500">{{ $t('clients.form.allowedScopesHelp') }}</p>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                type="submit"
                :disabled="submitting"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <svg v-if="submitting" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                {{ $t(submitting ? 'clients.form.saving' : (isEdit ? 'clients.form.updateButton' : 'clients.form.createButton')) }}
              </button>
              <button
                type="button"
                @click="$emit('cancel')"
                :disabled="submitting"
                class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {{ $t('clients.form.cancel') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>

  <!-- Secret Display Modal -->
  <SecretDisplayModal 
    v-if="showSecretModal"
    :visible="showSecretModal" 
    :secret="generatedClientSecret" 
    @close="closeSecretModal" 
  />
</template>
