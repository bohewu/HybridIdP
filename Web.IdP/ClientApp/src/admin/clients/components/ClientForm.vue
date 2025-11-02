<script setup>
import { ref, computed, watch } from 'vue'
import { z } from 'zod'

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
  clientSecret: '',
  redirectUris: '',
  postLogoutRedirectUris: '',
  permissions: []
})

// All available permissions from OpenIddict
const availablePermissions = [
  // Endpoints
  { value: 'ept:authorization', label: 'Authorization Endpoint', category: 'Endpoints' },
  { value: 'ept:token', label: 'Token Endpoint', category: 'Endpoints' },
  { value: 'ept:logout', label: 'Logout Endpoint', category: 'Endpoints' },
  { value: 'ept:introspection', label: 'Introspection Endpoint', category: 'Endpoints' },
  { value: 'ept:revocation', label: 'Revocation Endpoint', category: 'Endpoints' },
  { value: 'ept:device', label: 'Device Authorization Endpoint', category: 'Endpoints' },
  // Grant Types
  { value: 'gt:authorization_code', label: 'Authorization Code', category: 'Grant Types' },
  { value: 'gt:client_credentials', label: 'Client Credentials', category: 'Grant Types' },
  { value: 'gt:refresh_token', label: 'Refresh Token', category: 'Grant Types' },
  { value: 'gt:device_code', label: 'Device Code', category: 'Grant Types' },
  { value: 'gt:password', label: 'Password (Resource Owner)', category: 'Grant Types' },
  { value: 'gt:implicit', label: 'Implicit', category: 'Grant Types' },
  // Scopes
  { value: 'scp:openid', label: 'OpenID', category: 'Scopes' },
  { value: 'scp:profile', label: 'Profile', category: 'Scopes' },
  { value: 'scp:email', label: 'Email', category: 'Scopes' },
  { value: 'scp:roles', label: 'Roles', category: 'Scopes' }
]

// Group permissions by category
const permissionsByCategory = computed(() => {
  const grouped = {}
  availablePermissions.forEach(perm => {
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
const schema = z.object({
  clientId: z.string().min(1, 'Client ID is required'),
  displayName: z.string().optional(),
  applicationType: z.enum(['web', 'native']),
  clientType: z.enum(['public', 'confidential']),
  clientSecret: z.string().optional(),
  redirectUris: z.string().min(1, 'At least one Redirect URI is required'),
  postLogoutRedirectUris: z.string().optional(),
  permissions: z.array(z.string()).min(1, 'Select at least one permission')
}).superRefine((val, ctx) => {
  // Confidential requires secret on create; on edit it's optional
  if (!isEdit.value && val.clientType === 'confidential' && (!val.clientSecret || val.clientSecret.trim() === '')) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['clientSecret'], message: 'Client Secret is required for confidential clients' })
  }
  // Public must not have secret
  if (val.clientType === 'public' && val.clientSecret && val.clientSecret.trim() !== '') {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['clientSecret'], message: 'Public clients cannot have a Client Secret' })
  }
  // Validate Redirect URIs lines
  const redirectLines = (val.redirectUris || '').split('\n').map(x => x.trim()).filter(x => x.length > 0)
  if (redirectLines.length === 0) {
    ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['redirectUris'], message: 'At least one Redirect URI is required' })
  }
  redirectLines.forEach((u, i) => {
    try { new URL(u) } catch { ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['redirectUris'], message: `Redirect URI line ${i + 1} is not a valid absolute URL` }) }
  })
  // Validate Post Logout Redirect URIs if present
  const postLines = (val.postLogoutRedirectUris || '').split('\n').map(x => x.trim()).filter(x => x.length > 0)
  postLines.forEach((u, i) => {
    try { new URL(u) } catch { ctx.addIssue({ code: z.ZodIssueCode.custom, path: ['postLogoutRedirectUris'], message: `Post Logout URI line ${i + 1} is not a valid absolute URL` }) }
  })
})

const resetForm = () => {
  formData.value = {
    clientId: '',
    displayName: '',
    applicationType: 'web',
    clientType: 'public',
    clientSecret: '',
    redirectUris: '',
    postLogoutRedirectUris: '',
    permissions: []
  }
  error.value = null
  fieldErrors.value = {}
}

watch(() => props.client, (newClient) => {
  if (newClient) {
    formData.value = {
      clientId: newClient.clientId || '',
      displayName: newClient.displayName || '',
      applicationType: newClient.applicationType || 'web',
      clientType: newClient.type === 'confidential' ? 'confidential' : 'public',
      clientSecret: '',
      redirectUris: newClient.redirectUris?.join('\n') || '',
      postLogoutRedirectUris: newClient.postLogoutRedirectUris?.join('\n') || '',
      permissions: newClient.permissions || []
    }
  } else {
    resetForm()
  }
}, { immediate: true })

const handleSubmit = async () => {
  submitting.value = true
  error.value = null
  fieldErrors.value = {}

  try {
    // Validate with Zod schema (mirrors backend rules)
    const parsed = schema.safeParse(formData.value)
    if (!parsed.success) {
      // Group errors by field path
      parsed.error.issues.forEach(issue => {
        const field = issue.path[0] || 'general'
        if (!fieldErrors.value[field]) {
          fieldErrors.value[field] = []
        }
        fieldErrors.value[field].push(issue.message)
      })
      throw new Error('Please correct the validation errors below.')
    }

    const payload = {
      clientId: formData.value.clientId,
      displayName: formData.value.displayName || null,
      applicationType: formData.value.applicationType,
      type: formData.value.clientType,
      clientSecret: formData.value.clientSecret || null,
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

    emit('submit')
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
                    {{ isEdit ? 'Edit Client' : 'Create New Client' }}
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

                  <div class="space-y-4">
                    <!-- Client ID -->
                    <div>
                      <label for="clientId" class="block text-sm font-medium text-gray-700">
                        Client ID <span class="text-red-500">*</span>
                      </label>
                      <input
                        id="clientId"
                        v-model="formData.clientId"
                        type="text"
                        required
                        :disabled="isEdit"
                        :class="[
                          'mt-1 block w-full rounded-md shadow-sm sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed',
                          fieldErrors.clientId 
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500' 
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        placeholder="my-app-client"
                      />
                      <p v-if="!fieldErrors.clientId" class="mt-1 text-xs text-gray-500">Unique identifier for this client</p>
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
                    <div>
                      <label for="displayName" class="block text-sm font-medium text-gray-700">
                        Display Name
                      </label>
                      <input
                        id="displayName"
                        v-model="formData.displayName"
                        type="text"
                        class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                        placeholder="My Application"
                      />
                    </div>

                    <!-- Application Type -->
                    <div v-if="!isEdit">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        Application Type <span class="text-red-500">*</span>
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
                            <span class="font-medium">Web</span> - Traditional web applications (default)
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
                            <span class="font-medium">Native</span> - Desktop/mobile apps with custom URI schemes
                          </label>
                        </div>
                      </div>
                    </div>

                    <!-- Client Type -->
                    <div v-if="!isEdit">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        Client Type <span class="text-red-500">*</span>
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
                            <span class="font-medium">Public</span> - For SPAs, mobile apps, and desktop apps that cannot securely store secrets
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
                            <span class="font-medium">Confidential</span> - For server-side apps that can securely store secrets (requires Client Secret)
                          </label>
                        </div>
                      </div>
                    </div>

                    <!-- Client Secret -->
                    <div>
                      <label for="clientSecret" class="block text-sm font-medium text-gray-700">
                        Client Secret {{ isEdit ? '(leave empty to keep current)' : formData.clientType === 'confidential' ? '' : '(not required for public clients)' }}
                        <span v-if="!isEdit && formData.clientType === 'confidential'" class="text-red-500">*</span>
                      </label>
                      <input
                        id="clientSecret"
                        v-model="formData.clientSecret"
                        type="password"
                        :required="!isEdit && formData.clientType === 'confidential'"
                        :disabled="!isEdit && formData.clientType === 'public'"
                        :class="[
                          'mt-1 block w-full rounded-md shadow-sm sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed',
                          fieldErrors.clientSecret
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        :placeholder="formData.clientType === 'confidential' ? 'Enter a secure secret' : 'Not needed for public clients'"
                      />
                      <p v-if="!fieldErrors.clientSecret" class="mt-1 text-xs text-gray-500">
                        {{ formData.clientType === 'confidential' ? 'Required for confidential clients' : 'Public clients cannot have secrets' }}
                      </p>
                      <div v-if="fieldErrors.clientSecret" class="mt-1">
                        <p v-for="(err, idx) in fieldErrors.clientSecret" :key="idx" class="text-sm text-red-600 flex items-start">
                          <svg class="h-4 w-4 mr-1 mt-0.5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                          </svg>
                          {{ err }}
                        </p>
                      </div>
                    </div>

                    <!-- Redirect URIs -->
                    <div>
                      <label for="redirectUris" class="block text-sm font-medium text-gray-700">
                        Redirect URIs <span class="text-red-500">*</span>
                      </label>
                      <textarea
                        id="redirectUris"
                        v-model="formData.redirectUris"
                        required
                        rows="3"
                        :class="[
                          'mt-1 block w-full rounded-md shadow-sm sm:text-sm',
                          fieldErrors.redirectUris
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        placeholder="https://localhost:7001/signin-oidc&#10;https://myapp.com/callback"
                      ></textarea>
                      <p v-if="!fieldErrors.redirectUris" class="mt-1 text-xs text-gray-500">One URI per line</p>
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
                    <div>
                      <label for="postLogoutRedirectUris" class="block text-sm font-medium text-gray-700">
                        Post Logout Redirect URIs
                      </label>
                      <textarea
                        id="postLogoutRedirectUris"
                        v-model="formData.postLogoutRedirectUris"
                        rows="2"
                        :class="[
                          'mt-1 block w-full rounded-md shadow-sm sm:text-sm',
                          fieldErrors.postLogoutRedirectUris
                            ? 'border-red-300 text-red-900 placeholder-red-300 focus:border-red-500 focus:ring-red-500'
                            : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
                        ]"
                        placeholder="https://localhost:7001/signout-callback-oidc&#10;https://myapp.com/logout"
                      ></textarea>
                      <p v-if="!fieldErrors.postLogoutRedirectUris" class="mt-1 text-xs text-gray-500">One URI per line</p>
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
                        Permissions <span class="text-red-500">*</span>
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
                            {{ category }}
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
                                  {{ permission.label }}
                                </label>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                      
                      <p class="mt-3 text-xs text-gray-500 border-t pt-2">
                        💡 <strong>Tip:</strong> Most OAuth/OIDC flows need at minimum: Authorization Endpoint, Token Endpoint, Authorization Code grant, and OpenID scope.
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                type="submit"
                :disabled="submitting"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <svg v-if="submitting" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                {{ submitting ? 'Saving...' : (isEdit ? 'Update Client' : 'Create Client') }}
              </button>
              <button
                type="button"
                @click="$emit('cancel')"
                :disabled="submitting"
                class="mt-3 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Cancel
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>
