<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { z } from 'zod'
import BaseModal from '@/components/common/BaseModal.vue'
import SecretDisplayModal from './SecretDisplayModal.vue'
import ClientScopeManager from './ClientScopeManager.vue'
import ClientPermissionChecklist from './ClientPermissionChecklist.vue'
import AuthUrlGenerator from './AuthUrlGenerator.vue'
import { 
  useClientFormLogic, 
  validateUriList,
  availablePermissions,
  hasClientCredentialsPublicScopeConflict,
  resolveRequirePkce
} from '../composables/useClientFormLogic'

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
  requiredScopes: [],
  supportedRoles: [],
  requirePkce: true,
  pkceMethodPolicy: 'inherit',
  tokenEndpointAuthMethod: 'inherit',
  responseModePolicy: 'inherit',
  disableExternalProviders: false,
  enableTurnstile: false,
  requireMfa: false
})

const generatedClientSecret = ref(null)
const showSecretModal = ref(false)

// Use composable for computed properties and business logic
const { 
  isNative, 
  isInteractive, 
  permissionsByCategory, 
  localAllowedScopes 
} = useClientFormLogic(formData)

const submitting = ref(false)
const error = ref(null)
const fieldErrors = ref({}) // Per-field validation errors

// --- Validation Schema ---
const schema = computed(() => {
  let baseSchema = z.object({
    clientId: z.string().min(1, t('clients.form.clientIdRequired')),
    displayName: z.string().optional(),
    applicationType: z.string(), // z.enum(['web', 'native']),
    clientType: z.string(), // z.enum(['public', 'confidential']),
    consentType: z.string().optional(), // z.enum(['explicit', 'implicit']).optional(),
    redirectUris: z.string().optional(), // Default optional, validated via refine
    postLogoutRedirectUris: z.string().optional(),
    permissions: z.array(z.string()).min(1, t('clients.form.permissionsRequired'))
  })

  // Add refinements
  return baseSchema.superRefine((data, ctx) => {
    // Redirect URIs required for interactive flows
    if (isInteractive.value) {
       // Check if empty
       const lines = (data.redirectUris || '').split('\n').filter(l => l.trim().length > 0)
       if (lines.length === 0) {
         ctx.addIssue({
           code: z.ZodIssueCode.custom,
           message: t('clients.form.redirectUrisRequired'),
           path: ['redirectUris']
         })
       }
    }
    
    // Validate URI formats (if provided)
    if (data.redirectUris) {
      validateUriList(data.redirectUris, ctx, 'redirectUris', t('clients.form.redirectUrisInvalid'))
    }
    if (data.postLogoutRedirectUris) {
      validateUriList(data.postLogoutRedirectUris, ctx, 'postLogoutRedirectUris', t('clients.form.postLogoutRedirectUrisInvalid'))
    }

    if (hasClientCredentialsPublicScopeConflict(data.permissions)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: t('clients.form.allowedScopesClientCredentialsConflict'),
        path: ['allowedScopes']
      })
    }
    
  })
})



const resetForm = () => {
  formData.value = {
    clientId: '',
    displayName: '',
    applicationType: 'web',
    clientType: 'public',
    consentType: 'explicit',
    redirectUris: '',
    postLogoutRedirectUris: '',
    permissions: [],
    requiredScopes: [],
    supportedRoles: [],
    requirePkce: true,
    pkceMethodPolicy: 'inherit',
    tokenEndpointAuthMethod: 'inherit',
    responseModePolicy: 'inherit',
    disableExternalProviders: false,
    enableTurnstile: false,
    requireMfa: false
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
      requiredScopes: [],
      supportedRoles: newClient.supportedRoles || [],
      requirePkce: resolveRequirePkce(newClient.type === 'confidential' ? 'confidential' : 'public', newClient.requirePkce),
      pkceMethodPolicy: newClient.pkceMethodPolicy ?? 'inherit',
      tokenEndpointAuthMethod: newClient.tokenEndpointAuthMethod ?? 'inherit',
      responseModePolicy: newClient.responseModePolicy ?? 'inherit',
      disableExternalProviders: newClient.disableExternalProviders === true,
      enableTurnstile: newClient.enableTurnstile === true,
      requireMfa: newClient.requireMfa === true
    }
    
    // Fetch required scopes (still separate table)
    if (newClient.id) {
      try {
        const responseRequired = await fetch(`/api/admin/clients/${newClient.id}/required-scopes`)
        if (responseRequired.ok) {
          const data = await responseRequired.json()
          formData.value.requiredScopes = data.scopes || []
        }
      } catch (e) {
        console.error('Failed to fetch client required scopes:', e)
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
      permissions: formData.value.permissions,
      supportedRoles: formData.value.supportedRoles,
      requirePkce: resolveRequirePkce(formData.value.clientType, formData.value.requirePkce),
      pkceMethodPolicy: formData.value.pkceMethodPolicy,
      tokenEndpointAuthMethod: formData.value.clientType === 'public' ? 'inherit' : formData.value.tokenEndpointAuthMethod,
      responseModePolicy: formData.value.responseModePolicy,
      disableExternalProviders: formData.value.disableExternalProviders,
      enableTurnstile: formData.value.enableTurnstile,
      requireMfa: formData.value.requireMfa
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

    // Save REQUIRED scopes via dedicated endpoint (Allowed scopes are handled via permissions now)
    const clientId = isEdit.value ? props.client.id : responseData.id
    if (clientId) {
      try {
        // Save Required Scopes (always send explicit state to avoid race conditions)
        {
          const requiredResponse = await fetch(`/api/admin/clients/${clientId}/required-scopes`, {
            method: 'PUT',
            headers: {
              'Content-Type': 'application/json'
            },
            body: JSON.stringify({ scopes: formData.value.requiredScopes })
          })

          if (!requiredResponse.ok) {
             throw new Error(`Failed to save required scopes: ${requiredResponse.status}`)
          }
        }
      } catch (e) {
        console.error('Error saving client scopes:', e)
        throw new Error(`Client saved but failed to update required scopes: ${e.message}`)
      }
    }

    if (!isEdit.value && responseData.clientSecret) {
      generatedClientSecret.value = responseData.clientSecret
      showSecretModal.value = true
    } else {
      emit('submit')
    }
  } catch (e) {
    error.value = t('clients.form.saveFailed', { message: e.message })
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

const newSupportedRole = ref('')

const addSupportedRole = () => {
  const role = newSupportedRole.value.trim()
  if (role && !formData.value.supportedRoles.includes(role)) {
    formData.value.supportedRoles.push(role)
  }
  newSupportedRole.value = ''
}

const removeSupportedRole = (role) => {
  formData.value.supportedRoles = formData.value.supportedRoles.filter(r => r !== role)
}

watch(() => formData.value.clientType, (newType) => {
  if (newType === 'public') {
    formData.value.requirePkce = true
  }
})

// -----------------------------
// -----------------------------

const closeSecretModal = () => {
  showSecretModal.value = false
  emit('submit') // Signal to parent that form submission is complete
}
</script>

<template>
  <BaseModal
    :show="true"
    :title="$t(isEdit ? 'clients.form.editTitle' : 'clients.form.createTitle')"
    size="2xl"
    :loading="submitting"
    :close-on-backdrop="false"
    @close="$emit('cancel')"
  >
    <template #body>
      <form @submit.prevent="handleSubmit">
        <div class="max-h-[calc(100vh-300px)] overflow-y-auto">

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <div class="flex">
                      <div class="shrink-0">
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
                            : 'border-gray-300 focus:border-google-500 focus:ring-google-500'
                        ]"
                        :placeholder="$t('clients.form.clientIdPlaceholder')"
                      />
                      <p v-if="!fieldErrors.clientId" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.clientIdHelp') }}</p>
                      <div v-if="fieldErrors.clientId" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.clientId" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
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
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
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
                            class="h-4 w-4 text-google-500 focus:ring-google-500 border-gray-300"
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
                            class="h-4 w-4 text-google-500 focus:ring-google-500 border-gray-300"
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
                            class="h-4 w-4 text-google-500 focus:ring-google-500 border-gray-300"
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
                            :disabled="isNative"
                            :class="[
                              'h-4 w-4 focus:ring-google-500 border-gray-300',
                              isNative ? 'text-gray-300 cursor-not-allowed' : 'text-google-500'
                            ]"
                          />
                          <label for="type-confidential" class="ml-3 block text-sm text-gray-700">
                            <span class="font-medium">{{ $t('clients.form.clientTypeConfidential') }}</span> - {{ $t('clients.form.clientTypeConfidentialDesc') }}
                          </label>
                        </div>
                      </div>
                       <p v-if="formData.clientType === 'confidential'" class="mt-2 text-xs text-gray-500">
                        {{ $t('clients.form.clientSecretHelp') }}
                      </p>
                      
                      <!-- Auto-Gen Secret Placeholder -->
                      <div v-if="formData.clientType === 'confidential' && !isEdit" class="mt-3">
                        <label class="block text-sm font-medium text-gray-700 mb-1">
                            {{ $t('clients.form.clientSecret') }}
                        </label>
                        <input
                            type="text"
                            disabled
                            class="block w-full rounded-md border-gray-300 bg-gray-50 text-gray-500 shadow-sm sm:text-xs h-8 px-3 cursor-not-allowed italic"
                            :value="$t('clients.form.clientSecretAutoGenPlaceholder')"
                        />
                      </div>
                    </div>

                    <!-- Choose the flow before entering its callback addresses. -->
                    <div>
                      <ClientPermissionChecklist
                        id="client-grant-types"
                        :label="$t('clients.form.categories.grantTypes')"
                        :permissions="permissionsByCategory.grantTypes"
                        :selected="formData.permissions"
                        @toggle="togglePermission"
                      />
                      <div v-if="fieldErrors.permissions" class="mt-2">
                        <p v-for="(err, idx) in fieldErrors.permissions" :key="idx" class="text-sm text-red-600">{{ err }}</p>
                      </div>
                    </div>

                    <!-- Redirect URIs -->
                    <div class="mb-5" v-if="isInteractive">
                      <label for="redirectUris" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.redirectUris') }} <span v-if="isInteractive" class="text-red-500">*</span>
                      </label>
                      <textarea
                        id="redirectUris"
                        v-model="formData.redirectUris"
                        :required="isInteractive"
                        rows="3"
                        :class="[
                          'block w-full rounded-md shadow-sm sm:text-sm px-3 py-2',
                          fieldErrors.redirectUris
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-google-500 focus:ring-google-500'
                        ]"
                        :placeholder="$t('clients.form.redirectUrisPlaceholder')"
                      ></textarea>
                      <p v-if="!fieldErrors.redirectUris" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.redirectUrisHelp') }}</p>
                      <div v-if="fieldErrors.redirectUris" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.redirectUris" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <!-- Post Logout Redirect URIs -->
                    <div class="mb-5" v-if="isInteractive">
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
                            : 'border-gray-300 focus:border-google-500 focus:ring-google-500'
                        ]"
                        :placeholder="$t('clients.form.postLogoutRedirectUrisPlaceholder')"
                      ></textarea>
                      <p v-if="!fieldErrors.postLogoutRedirectUris" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.postLogoutRedirectUrisHelp') }}</p>
                      <div v-if="fieldErrors.postLogoutRedirectUris" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.postLogoutRedirectUris" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <div v-if="formData.clientType === 'confidential'" class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.requirePkce') }}
                      </label>
                      <label class="inline-flex items-center gap-2 text-sm text-gray-700">
                        <input
                          id="require-pkce"
                          v-model="formData.requirePkce"
                          type="checkbox"
                          aria-describedby="require-pkce-help"
                          class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
                        />
                        <span>{{ $t('clients.form.requirePkceToggle') }}</span>
                      </label>
                      <p id="require-pkce-help" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.requirePkceHelp') }}</p>
                    </div>

                    <details class="rounded-md border border-gray-200" data-test-id="client-protocol-policies">
                      <summary class="cursor-pointer px-3 py-3 text-sm font-medium text-gray-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-google-500">
                        {{ $t('clients.form.protocolPolicies.title') }}
                      </summary>
                      <div class="space-y-4 border-t border-gray-200 p-3">
                        <p class="text-xs text-gray-500">{{ $t('clients.form.protocolPolicies.inheritHelp') }}</p>
                        <div>
                          <label for="pkce-method-policy" class="mb-1.5 block text-sm font-medium text-gray-700">{{ $t('clients.form.protocolPolicies.pkceMethod') }}</label>
                          <select id="pkce-method-policy" v-model="formData.pkceMethodPolicy" aria-describedby="pkce-method-policy-help" class="block h-10 w-full rounded-md border-gray-300 px-3 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm">
                            <option value="inherit">{{ $t('clients.form.protocolPolicies.inherit') }}</option>
                            <option value="S256">{{ $t('clients.form.protocolPolicies.s256Only') }}</option>
                          </select>
                          <p id="pkce-method-policy-help" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.protocolPolicies.pkceHelp') }}</p>
                        </div>
                        <div>
                          <label for="token-endpoint-auth-method" class="mb-1.5 block text-sm font-medium text-gray-700">{{ $t('clients.form.protocolPolicies.tokenAuthMethod') }}</label>
                          <select id="token-endpoint-auth-method" :value="formData.clientType === 'public' ? 'inherit' : formData.tokenEndpointAuthMethod" @change="formData.tokenEndpointAuthMethod = $event.target.value" :disabled="formData.clientType === 'public'" aria-describedby="token-endpoint-auth-method-help" class="block h-10 w-full rounded-md border-gray-300 px-3 shadow-sm focus:border-google-500 focus:ring-google-500 disabled:cursor-not-allowed disabled:bg-gray-100 sm:text-sm">
                            <option value="inherit">{{ $t('clients.form.protocolPolicies.inherit') }}</option>
                            <option value="client_secret_basic">client_secret_basic</option>
                            <option value="client_secret_post">client_secret_post</option>
                          </select>
                          <p id="token-endpoint-auth-method-help" class="mt-1 text-xs text-gray-500">{{ $t(formData.clientType === 'public' ? 'clients.form.protocolPolicies.publicAuthHelp' : 'clients.form.protocolPolicies.tokenAuthHelp') }}</p>
                        </div>
                        <div>
                          <label for="response-mode-policy" class="mb-1.5 block text-sm font-medium text-gray-700">{{ $t('clients.form.protocolPolicies.responseMode') }}</label>
                          <select id="response-mode-policy" v-model="formData.responseModePolicy" aria-describedby="response-mode-policy-help" class="block h-10 w-full rounded-md border-gray-300 px-3 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm">
                            <option value="inherit">{{ $t('clients.form.protocolPolicies.inherit') }}</option>
                            <option value="query">query</option>
                            <option value="form_post">form_post</option>
                          </select>
                          <p id="response-mode-policy-help" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.protocolPolicies.responseModeHelp') }}</p>
                        </div>
                      </div>
                    </details>

                    <div class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.externalProviders') }}
                      </label>
                      <label class="inline-flex items-center gap-2 text-sm text-gray-700">
                        <input
                          v-model="formData.disableExternalProviders"
                          type="checkbox"
                          class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
                        />
                        <span>{{ $t('clients.form.disableExternalProvidersToggle') }}</span>
                      </label>
                      <p class="mt-1 text-xs text-gray-500">{{ $t('clients.form.disableExternalProvidersHelp') }}</p>
                    </div>

                    <div class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.turnstile') }}
                      </label>
                      <label class="inline-flex items-center gap-2 text-sm text-gray-700">
                        <input
                          v-model="formData.enableTurnstile"
                          type="checkbox"
                          class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
                        />
                        <span>{{ $t('clients.form.enableTurnstileToggle') }}</span>
                      </label>
                      <p class="mt-1 text-xs text-gray-500">{{ $t('clients.form.enableTurnstileHelp') }}</p>
                    </div>

                    <div class="mb-5">
                      <label for="require-mfa" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('clients.form.requireMfa') }}
                      </label>
                      <label for="require-mfa" class="inline-flex items-center gap-2 text-sm text-gray-700">
                        <input
                          id="require-mfa"
                          v-model="formData.requireMfa"
                          type="checkbox"
                          aria-describedby="require-mfa-help"
                          class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
                        />
                        <span>{{ $t('clients.form.requireMfaToggle') }}</span>
                      </label>
                      <p id="require-mfa-help" class="mt-1 text-xs text-gray-500">{{ $t('clients.form.requireMfaHelp') }}</p>
                    </div>

                    <!-- Consent Type -->
                    <div v-if="isInteractive">
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
                            class="h-4 w-4 text-google-500 focus:ring-google-500 border-gray-300"
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
                            class="h-4 w-4 text-google-500 focus:ring-google-500 border-gray-300"
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

                    <!-- Endpoint permissions -->
                    <div>
                      <ClientPermissionChecklist
                        id="client-endpoint-permissions"
                        :label="$t('clients.form.categories.endpoints')"
                        :permissions="permissionsByCategory.endpoints"
                        :selected="formData.permissions"
                        @toggle="togglePermission"
                      />
                      <p class="mt-3 text-xs text-gray-500 border-t pt-2" v-html="$t('clients.form.permissionsHelp')"></p>
                    </div>

                    <!-- Allowed Scopes -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.allowedScopes') }} <span class="text-red-500">*</span>
                      </label>
                      
                      <!-- Help text moved to top for visibility -->
                      <p class="mb-3 text-xs text-gray-500">{{ $t('clients.form.allowedScopesHelp') }}</p>
                      <p class="mb-3 text-xs text-gray-500">{{ $t('clients.form.scopeManager.requiredHelp') }}</p>
                      <p v-if="!localAllowedScopes.includes('openid')" class="mb-3 text-xs text-yellow-600">
                        {{ $t('clients.form.allowedScopesOpenidRequired') }}
                      </p>
                      
                      <div v-if="fieldErrors.allowedScopes" class="mb-2">
                        <p v-for="(err, idx) in fieldErrors.allowedScopes" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                      
                      <div class="mb-8"> <!-- Increased margin to prevent overlap -->
                        <ClientScopeManager 
                          v-model="localAllowedScopes" 
                          v-model:requiredScopes="formData.requiredScopes" 
                        />
                      </div>

                    <!-- Supported Roles -->
                    <div class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('clients.form.supportedRoles') }}
                      </label>
                      <div class="flex gap-2 mb-2">
                        <input
                          v-model="newSupportedRole"
                          type="text"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
                          :placeholder="$t('clients.form.supportedRolesPlaceholder')"
                          @keydown.enter.prevent="addSupportedRole"
                        />
                        <button
                          type="button"
                          @click="addSupportedRole"
                          class="h-10 inline-flex items-center px-3 py-2 border border-transparent text-sm leading-4 font-medium rounded-md shadow-sm text-white bg-google-600 hover:bg-google-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-google-500"
                        >
                          {{ $t('clients.form.scopeManager.add') }}
                        </button>
                      </div>
                      <p class="text-xs text-gray-500 mb-2">{{ $t('clients.form.supportedRolesHelp') }}</p>

                      <div class="flex flex-wrap gap-2">
                        <span
                          v-for="role in formData.supportedRoles"
                          :key="role"
                          class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800"
                        >
                          {{ role }}
                          <button
                            type="button"
                            class="ml-1.5 inline-flex items-center justify-center text-blue-400 hover:text-blue-600 focus:outline-none"
                            @click="removeSupportedRole(role)"
                          >
                            <span class="sr-only">Remove</span>
                            <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                              <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd" />
                            </svg>
                          </button>
                        </span>
                      </div>
                    </div>
                    </div>

                    <!-- TEST URL GENERATOR -->
                    <AuthUrlGenerator 
                      v-if="isInteractive"
                      :client-id="formData.clientId"
                      :redirect-uri="formData.redirectUris"
                      :scopes="localAllowedScopes"
                    />

                  </div>
        </div>
      </form>
    </template>

    <template #footer>
      <button
        data-test-id="client-form-submit"
        type="submit"
        @click="handleSubmit"
        :disabled="submitting"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
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
    </template>
  </BaseModal>

  <!-- Secret Display Modal -->
  <SecretDisplayModal 
    v-if="showSecretModal"
    :visible="showSecretModal" 
    :secret="generatedClientSecret" 
    @close="closeSecretModal" 
  />
</template>
