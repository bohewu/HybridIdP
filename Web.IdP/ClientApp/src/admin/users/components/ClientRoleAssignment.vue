<script setup>
import { ref, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '../../../components/common/BaseModal.vue'

const { t } = useI18n()

const props = defineProps({
  userId: { type: String, required: true },
  show: { type: Boolean, default: false }
})

const emit = defineEmits(['close'])

const clients = ref([])
const selectedClient = ref(null)
const supportedRoles = ref([])
const assignedRoles = ref([])
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const successMessage = ref('')

const fetchClients = async () => {
    loading.value = true
    try {
        const response = await fetch('/api/admin/clients?take=100')
        if (response.ok) {
            const data = await response.json()
            clients.value = data.items || []
        }
    } catch (e) {
        console.error('Failed to fetch clients', e)
    } finally {
        loading.value = false
    }
}

const handleClientChange = async () => {
    if (!selectedClient.value) {
        supportedRoles.value = []
        assignedRoles.value = []
        return
    }

    loading.value = true
    error.value = ''
    successMessage.value = ''
    
    try {
        // 1. Get Client Details for Supported Roles
        const clientRes = await fetch(`/api/admin/clients/${selectedClient.value}`)
        if (clientRes.ok) {
            const clientData = await clientRes.json()
            supportedRoles.value = clientData.supportedRoles || []
        }

        // 2. Get Assigned Roles for this User and Client
        // Note: selectedClient.value is the ID (Guid), but the endpoint needs ClientId string? 
        // Wait, UsersController GetUserAppRoles takes "clientId". Is it the DB ID or the ClientId string?
        // _userManagementService.GetUserAppRolesAsync(Guid userId, string clientId) usage:
        // In UserManagementService: .Where(uar => uar.UserId == userId && uar.ClientId == clientId)
        // The Entity UserAppRole.ClientId is string (the unique ClientId, e.g. "client-id", not Guid PK).
        // ClientsController returns "id" (Guid) and "clientId" (string).
        // I should use the "clientId" string.
        
        // Find the client object from list to get the ClientId string
        const clientObj = clients.value.find(c => c.id === selectedClient.value)
        const clientIdStr = clientObj ? clientObj.clientId : null;

        if (clientIdStr) {
            const rolesRes = await fetch(`/api/admin/users/${props.userId}/app-roles/${clientIdStr}`)
            if (rolesRes.ok) {
                 assignedRoles.value = await rolesRes.json()
            }
        }

    } catch (e) {
        error.value = t('users.errors.loadRolesFailed')
    } finally {
        loading.value = false
    }
}

const toggleRole = (role) => {
    if (assignedRoles.value.includes(role)) {
        assignedRoles.value = assignedRoles.value.filter(r => r !== role)
    } else {
        assignedRoles.value.push(role)
    }
}

const handleSave = async () => {
    if (!selectedClient.value) return
    
    saving.value = true
    error.value = ''
    successMessage.value = ''

    try {
        const clientObj = clients.value.find(c => c.id === selectedClient.value)
        const clientIdStr = clientObj ? clientObj.clientId : null;
        
        if (!clientIdStr) throw new Error("Client ID not found")

        const response = await fetch(`/api/admin/users/${props.userId}/app-roles/${clientIdStr}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(assignedRoles.value)
        })

        if (!response.ok) {
            throw new Error('Failed to save')
        }

        successMessage.value = t('users.rolesSaved')
    } catch (e) {
        error.value = t('users.errors.saveFailed')
    } finally {
        saving.value = false
    }
}

const handleClose = () => {
    emit('close')
    selectedClient.value = null
    supportedRoles.value = []
    assignedRoles.value = []
    error.value = ''
    successMessage.value = ''
}

watch(() => props.show, (newVal) => {
    if (newVal) {
        fetchClients()
    }
})
</script>

<template>
  <BaseModal
    :show="show"
    :title="$t('users.manageAppRoles')"
    size="lg"
    :show-close-icon="true"
    @close="handleClose"
  >
    <template #body>
      <div v-if="loading && !clients.length" class="flex justify-center p-4">
         <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-google-500"></div>
      </div>
      
      <div v-else class="space-y-6">
          <!-- Client Selector -->
          <div>
              <label class="block text-sm font-medium text-gray-700 mb-2">{{ $t('users.selectClient') }}</label>
              <select 
                v-model="selectedClient" 
                @change="handleClientChange"
                class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
              >
                  <option :value="null" disabled>{{ $t('users.selectClientPlaceholder') }}</option>
                  <option v-for="client in clients" :key="client.id" :value="client.id">
                      {{ client.displayName || client.clientId }}
                  </option>
              </select>
          </div>

          <!-- Roles Area -->
          <div v-if="selectedClient" class="border rounded-md p-4 bg-gray-50">
              <div v-if="loading" class="flex justify-center">
                  <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-google-500"></div>
              </div>
              <div v-else>
                  <h4 class="text-sm font-medium text-gray-900 mb-3">{{ $t('users.availableRoles') }}</h4>
                  
                  <div v-if="supportedRoles.length === 0" class="text-sm text-gray-500 italic">
                      {{ $t('users.noSupportedRoles') }}
                  </div>
                  
                  <div v-else class="space-y-2">
                      <div v-for="role in supportedRoles" :key="role" class="flex items-center">
                          <input
                            :id="'role-' + role"
                            type="checkbox"
                            :checked="assignedRoles.includes(role)"
                            @change="toggleRole(role)"
                            class="h-4 w-4 rounded border-gray-300 text-google-600 focus:ring-google-500"
                          />
                          <label :for="'role-' + role" class="ml-2 text-sm text-gray-900 cursor-pointer">
                              {{ role }}
                          </label>
                      </div>
                  </div>
              </div>
          </div>

          <!-- Messages -->
          <div v-if="error" class="text-sm text-red-600 bg-red-50 p-2 rounded">
              {{ error }}
          </div>
          <div v-if="successMessage" class="text-sm text-green-600 bg-green-50 p-2 rounded">
              {{ successMessage }}
          </div>
      </div>
    </template>

    <template #footer>
      <button
        type="button"
        @click="handleSave"
        :disabled="!selectedClient || saving || loading"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
         <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" fill="none" viewBox="0 0 24 24">
             <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
             <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
         </svg>
         {{ saving ? $t('users.saving') : $t('users.saveRoles') }}
      </button>
      <button
        type="button"
        @click="handleClose"
        class="mt-3 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto"
      >
        {{ $t('users.close') }}
      </button>
    </template>
  </BaseModal>
</template>
