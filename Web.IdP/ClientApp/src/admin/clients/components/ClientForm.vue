<script setup>
import { ref, computed, watch } from 'vue'

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
  clientSecret: '',
  redirectUris: '',
  postLogoutRedirectUris: '',
  permissions: []
})

const availablePermissions = [
  'ept:authorization',
  'ept:token',
  'ept:logout',
  'gt:authorization_code',
  'gt:refresh_token',
  'scp:openid',
  'scp:profile',
  'scp:email'
]

const submitting = ref(false)
const error = ref(null)

const resetForm = () => {
  formData.value = {
    clientId: '',
    displayName: '',
    clientSecret: '',
    redirectUris: '',
    postLogoutRedirectUris: '',
    permissions: []
  }
  error.value = null
}

watch(() => props.client, (newClient) => {
  if (newClient) {
    formData.value = {
      clientId: newClient.clientId || '',
      displayName: newClient.displayName || '',
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

  try {
    const payload = {
      clientId: formData.value.clientId,
      displayName: formData.value.displayName || null,
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
                    <p class="text-sm text-red-700">{{ error }}</p>
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
                        class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed"
                        placeholder="my-app-client"
                      />
                      <p class="mt-1 text-xs text-gray-500">Unique identifier for this client</p>
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

                    <!-- Client Secret -->
                    <div>
                      <label for="clientSecret" class="block text-sm font-medium text-gray-700">
                        Client Secret {{ isEdit ? '(leave empty to keep current)' : '' }}
                      </label>
                      <input
                        id="clientSecret"
                        v-model="formData.clientSecret"
                        type="password"
                        class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                        placeholder="Enter secret for confidential clients"
                      />
                      <p class="mt-1 text-xs text-gray-500">Leave empty for public clients (SPA, mobile apps)</p>
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
                        class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                        placeholder="https://localhost:7001/signin-oidc&#10;https://myapp.com/callback"
                      ></textarea>
                      <p class="mt-1 text-xs text-gray-500">One URI per line</p>
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
                        class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                        placeholder="https://localhost:7001/signout-callback-oidc&#10;https://myapp.com/logout"
                      ></textarea>
                      <p class="mt-1 text-xs text-gray-500">One URI per line</p>
                    </div>

                    <!-- Permissions -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        Permissions <span class="text-red-500">*</span>
                      </label>
                      <div class="grid grid-cols-2 gap-2">
                        <div
                          v-for="permission in availablePermissions"
                          :key="permission"
                          class="relative flex items-start"
                        >
                          <div class="flex h-5 items-center">
                            <input
                              :id="permission"
                              type="checkbox"
                              :checked="formData.permissions.includes(permission)"
                              @change="togglePermission(permission)"
                              class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                            />
                          </div>
                          <div class="ml-3 text-sm">
                            <label :for="permission" class="font-medium text-gray-700">
                              {{ permission }}
                            </label>
                          </div>
                        </div>
                      </div>
                      <p class="mt-2 text-xs text-gray-500">
                        ept = endpoint, gt = grant type, scp = scope
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
