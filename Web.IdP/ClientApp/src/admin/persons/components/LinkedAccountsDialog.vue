<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  person: {
    type: Object,
    required: true
  },
  canUpdate: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['close', 'updated'])

const linkedAccounts = ref([])
const availableUsers = ref([])
const loading = ref(false)
const error = ref(null)
const searchTerm = ref('')
const showLinkDialog = ref(false)
const selectedUserId = ref(null)
const linking = ref(false)

onMounted(() => {
  fetchLinkedAccounts()
})

const fetchLinkedAccounts = async () => {
  loading.value = true
  error.value = null
  try {
    const response = await fetch(`/api/admin/persons/${props.person.id}/accounts`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    linkedAccounts.value = await response.json()
  } catch (e) {
    error.value = t('admin.persons.linkedAccounts.errors.loadFailed', { message: e.message })
    console.error('Error fetching linked accounts:', e)
  } finally {
    loading.value = false
  }
}

const fetchAvailableUsers = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams()
    if (searchTerm.value) {
      params.append('search', searchTerm.value)
    }
    
    const response = await fetch(`/api/admin/persons/available-users?${params.toString()}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    availableUsers.value = await response.json()
  } catch (e) {
    error.value = t('admin.persons.linkedAccounts.errors.loadAvailableFailed', { message: e.message })
    console.error('Error fetching available users:', e)
  } finally {
    loading.value = false
  }
}

const handleShowLinkDialog = () => {
  showLinkDialog.value = true
  searchTerm.value = ''
  availableUsers.value = []
  fetchAvailableUsers()
}

const handleSearchUsers = () => {
  fetchAvailableUsers()
}

const handleLinkAccount = async () => {
  if (!selectedUserId.value) {
    error.value = t('admin.persons.linkedAccounts.errors.noUserSelected')
    return
  }
  
  linking.value = true
  error.value = null
  
  try {
    const response = await fetch(`/api/admin/persons/${props.person.id}/accounts`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ userId: selectedUserId.value })
    })
    
    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `HTTP error! status: ${response.status}`)
    }
    
    showLinkDialog.value = false
    selectedUserId.value = null
    await fetchLinkedAccounts()
    emit('updated')
    alert(t('admin.persons.linkedAccounts.alerts.linkSuccess'))
  } catch (e) {
    error.value = t('admin.persons.linkedAccounts.errors.linkFailed', { message: e.message })
    console.error('Error linking account:', e)
  } finally {
    linking.value = false
  }
}

const handleUnlinkAccount = async (account) => {
  if (!confirm(t('admin.persons.linkedAccounts.confirmations.unlink', { email: account.email }))) {
    return
  }
  
  loading.value = true
  error.value = null
  
  try {
    const response = await fetch(`/api/admin/persons/accounts/${account.id}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchLinkedAccounts()
    emit('updated')
    alert(t('admin.persons.linkedAccounts.alerts.unlinkSuccess'))
  } catch (e) {
    error.value = t('admin.persons.linkedAccounts.errors.unlinkFailed', { message: e.message })
    console.error('Error unlinking account:', e)
  } finally {
    loading.value = false
  }
}

const handleClose = () => {
  emit('close')
}

const handleCloseLinkDialog = () => {
  showLinkDialog.value = false
  selectedUserId.value = null
  searchTerm.value = ''
}
</script>

<template>
  <div class="fixed inset-0 z-50 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
    <div class="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
      <!-- Background overlay -->
      <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" aria-hidden="true" @click="handleClose"></div>

      <!-- Center modal -->
      <span class="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>

      <div class="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-2xl sm:w-full">
        <div class="bg-white px-4 pt-5 pb-4 sm:p-6">
          <div class="sm:flex sm:items-start">
            <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
              <h3 class="text-lg leading-6 font-medium text-gray-900" id="modal-title">
                {{ t('admin.persons.linkedAccounts.title') }}
              </h3>
              <p class="mt-1 text-sm text-gray-500">
                {{ t('admin.persons.linkedAccounts.personName', { name: `${person.firstName} ${person.lastName}` }) }}
              </p>

              <!-- Error Message -->
              <div v-if="error" class="mt-4 bg-red-50 border-l-4 border-red-400 p-4">
                <p class="text-sm text-red-700">{{ error }}</p>
              </div>

              <!-- Link Account Button -->
              <div v-if="canUpdate" class="mt-4">
                <button
                  @click="handleShowLinkDialog"
                  class="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                >
                  <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
                  </svg>
                  {{ t('admin.persons.linkedAccounts.linkAccount') }}
                </button>
              </div>

              <!-- Loading State -->
              <div v-if="loading && !showLinkDialog" class="mt-4 flex justify-center py-8">
                <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
              </div>

              <!-- Linked Accounts List -->
              <div v-else-if="linkedAccounts.length === 0" class="mt-4 text-center py-8">
                <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                </svg>
                <p class="mt-2 text-sm text-gray-500">{{ t('admin.persons.linkedAccounts.noAccounts') }}</p>
              </div>

              <div v-else class="mt-4">
                <ul role="list" class="divide-y divide-gray-200 border border-gray-200 rounded-md">
                  <li v-for="account in linkedAccounts" :key="account.id" class="px-4 py-3 hover:bg-gray-50">
                    <div class="flex items-center justify-between">
                      <div class="flex-1 min-w-0">
                        <p class="text-sm font-medium text-gray-900">{{ account.email }}</p>
                        <p class="text-sm text-gray-500">{{ account.userName }}</p>
                        <div class="mt-1 flex items-center space-x-2 text-xs text-gray-500">
                          <span :class="account.isActive ? 'text-green-600' : 'text-red-600'">
                            {{ account.isActive ? t('admin.persons.linkedAccounts.active') : t('admin.persons.linkedAccounts.inactive') }}
                          </span>
                          <span v-if="account.lastLoginDate">
                            • {{ t('admin.persons.linkedAccounts.lastLogin') }}: {{ new Date(account.lastLoginDate).toLocaleDateString() }}
                          </span>
                        </div>
                      </div>
                      <button
                        v-if="canUpdate"
                        @click="handleUnlinkAccount(account)"
                        class="ml-4 inline-flex items-center px-3 py-1.5 border border-transparent shadow-sm text-xs font-medium rounded text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                      >
                        {{ t('admin.persons.linkedAccounts.unlink') }}
                      </button>
                    </div>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
        
        <div class="bg-gray-50 px-4 py-3 sm:px-6 sm:flex sm:flex-row-reverse">
          <button
            type="button"
            @click="handleClose"
            class="w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:ml-3 sm:w-auto sm:text-sm"
          >
            {{ t('admin.persons.close') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Link Account Dialog (nested) -->
    <div v-if="showLinkDialog" class="fixed inset-0 z-60 overflow-y-auto" aria-labelledby="link-modal-title" role="dialog" aria-modal="true">
      <div class="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
        <!-- Background overlay -->
        <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" aria-hidden="true" @click="handleCloseLinkDialog"></div>

        <!-- Center modal -->
        <span class="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>

        <div class="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-lg sm:w-full">
          <div class="bg-white px-4 pt-5 pb-4 sm:p-6">
            <h3 class="text-lg leading-6 font-medium text-gray-900" id="link-modal-title">
              {{ t('admin.persons.linkedAccounts.selectUser') }}
            </h3>

            <!-- Search -->
            <div class="mt-4">
              <div class="flex space-x-2">
                <input
                  v-model="searchTerm"
                  type="text"
                  :placeholder="t('admin.persons.linkedAccounts.searchUsers')"
                  class="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                  @keyup.enter="handleSearchUsers"
                />
                <button
                  @click="handleSearchUsers"
                  class="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                >
                  {{ t('admin.persons.linkedAccounts.search') }}
                </button>
              </div>
            </div>

            <!-- Available Users List -->
            <div v-if="loading" class="mt-4 flex justify-center py-8">
              <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
            </div>

            <div v-else-if="availableUsers.length === 0" class="mt-4 text-center py-8">
              <p class="text-sm text-gray-500">{{ t('admin.persons.linkedAccounts.noAvailableUsers') }}</p>
            </div>

            <div v-else class="mt-4 max-h-64 overflow-y-auto">
              <ul role="list" class="divide-y divide-gray-200 border border-gray-200 rounded-md">
                <li
                  v-for="user in availableUsers"
                  :key="user.id"
                  @click="selectedUserId = user.id"
                  class="px-4 py-3 hover:bg-gray-50 cursor-pointer"
                  :class="{ 'bg-indigo-50': selectedUserId === user.id }"
                >
                  <div class="flex items-center">
                    <input
                      type="radio"
                      :checked="selectedUserId === user.id"
                      class="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300"
                    />
                    <div class="ml-3">
                      <p class="text-sm font-medium text-gray-900">{{ user.email }}</p>
                      <p class="text-sm text-gray-500">{{ user.userName }}</p>
                    </div>
                  </div>
                </li>
              </ul>
            </div>
          </div>

          <div class="bg-gray-50 px-4 py-3 sm:px-6 sm:flex sm:flex-row-reverse">
            <button
              type="button"
              @click="handleLinkAccount"
              :disabled="!selectedUserId || linking"
              class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-indigo-600 text-base font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <svg v-if="linking" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              {{ linking ? t('admin.persons.linkedAccounts.linking') : t('admin.persons.linkedAccounts.link') }}
            </button>
            <button
              type="button"
              @click="handleCloseLinkDialog"
              :disabled="linking"
              class="mt-3 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {{ t('admin.persons.cancel') }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
