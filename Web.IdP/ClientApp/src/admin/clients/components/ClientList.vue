<script setup>
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import SecretDisplayModal from './SecretDisplayModal.vue'

const { t } = useI18n()

defineProps({
  clients: {
    type: Array,
    required: true
  },
  canUpdate: {
    type: Boolean,
    default: false
  },
  canDelete: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['edit', 'delete'])

const showSecretModal = ref(false)
const newSecret = ref(null)
const regeneratingId = ref(null)
const error = ref(null)

const regenerateSecret = async (client) => {
  if (!confirm(t('clients.actions.regenerateSecretConfirm', { clientId: client.clientId }))) {
    return
  }

  regeneratingId.value = client.id
  error.value = null
  
  try {
    const response = await fetch(`/api/admin/clients/${client.id}/regenerate-secret`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      const errorData = await response.json()
      throw new Error(errorData.message || `HTTP error! status: ${response.status}`)
    }

    const responseData = await response.json()
    newSecret.value = responseData.clientSecret
    showSecretModal.value = true
  } catch (e) {
    error.value = `Failed to regenerate secret: ${e.message}`
    // You might want to show this error in a more prominent way
    console.error(error.value)
    alert(error.value)
  } finally {
    regeneratingId.value = null
  }
}

const handleModalClose = () => {
  showSecretModal.value = false
  newSecret.value = null
}
</script>

<template>
  <div>
    <div class="bg-white shadow overflow-hidden sm:rounded-md">
      <ul role="list" class="divide-y divide-gray-200">
        <li v-if="clients.length === 0" class="px-6 py-8 text-center text-gray-500">
          {{ $t('clients.noClients') }}
        </li>
        <li v-for="client in clients" :key="client.id" class="px-6 py-4 hover:bg-gray-50">
          <div class="flex items-center justify-between">
            <div class="flex-1 min-w-0">
              <div class="flex items-center space-x-3">
                <div class="shrink-0">
                  <div class="h-10 w-10 rounded-full bg-indigo-100 flex items-center justify-center">
                    <svg class="h-6 w-6 text-indigo-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                    </svg>
                  </div>
                </div>
                <div class="flex-1 min-w-0">
                  <p class="text-sm font-medium text-gray-900 truncate">
                    {{ client.clientId }}
                  </p>
                  <p class="text-sm text-gray-500 truncate">
                    {{ client.displayName || $t('clients.noDisplayName') }}
                  </p>
                </div>
              </div>
              <div class="mt-2 flex items-center text-sm text-gray-500 space-x-4">
                <span class="flex items-center">
                  <svg class="shrink-0 mr-1.5 h-4 w-4 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                  </svg>
                  {{ $t('clients.redirectUris', { count: client.redirectUrisCount ?? (client.redirectUris?.length || 0) }) }}
                </span>
                <span class="flex items-center">
                  <svg class="shrink-0 mr-1.5 h-4 w-4 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                  </svg>
                  {{ $t(`clients.clientType.${client.type === 'confidential' ? 'confidential' : 'public'}`) }}
                </span>
              </div>
            </div>
            <div class="flex space-x-2">
              <div class="inline-flex gap-1">
                <button
                  v-if="canUpdate && client.type === 'confidential'"
                  @click="regenerateSecret(client)"
                  :disabled="regeneratingId === client.id"
                  class="inline-flex items-center px-3 py-1.5 border border-yellow-300 text-yellow-700 text-sm font-medium rounded-md hover:bg-yellow-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-yellow-500"
                    :title="$t('clients.actions.regenerateSecret')"
                >
                  <svg v-if="regeneratingId === client.id" class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  <svg v-else class="h-4 w-4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 24 24">
                    <path d="M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4" />
                    <path d="M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4" />
                  </svg>
                </button>
                <button
                  v-if="canUpdate"
                  @click="$emit('edit', client)"
                  class="inline-flex items-center px-3 py-1.5 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                  :title="$t('clients.actions.edit')"
                >
                  <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                  </svg>
                </button>
                <button
                  v-if="canDelete"
                  @click="$emit('delete', client.id)"
                  class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-sm font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                  :title="$t('clients.actions.delete')"
                >
                  <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </button>
                <span v-if="!canUpdate && !canDelete" class="text-xs text-gray-400 italic">{{ $t('clients.actions.noActions') }}</span>
              </div>
            </div>
          </div>
        </li>
      </ul>
    </div>
    <SecretDisplayModal 
      v-if="showSecretModal"
      :visible="showSecretModal" 
      :secret="newSecret" 
      @close="handleModalClose" 
    />
  </div>
</template>
