<script setup>
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ClientList from './components/ClientList.vue'
import ClientForm from './components/ClientForm.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()

// Permission state
const canCreate = ref(false)
const canUpdate = ref(false)
const canDelete = ref(false)
const canRead = ref(false)

// Access denied dialog
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')

const clients = ref([])
const loading = ref(true)
const error = ref(null)
const selectedClient = ref(null)
const showForm = ref(false)

// Paging / filtering / sorting state
const pageSize = ref(10)
const page = ref(1)
const totalCount = ref(0)
const search = ref('')
const typeFilter = ref('') // '', 'public', 'confidential'
const sort = ref('clientId:asc')

const fetchClients = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: String((page.value - 1) * pageSize.value),
      take: String(pageSize.value),
      search: search.value || '',
      type: typeFilter.value || '',
      sort: sort.value || ''
    })
    const response = await fetch(`/api/admin/clients?${params.toString()}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    // Support both new shape ({items,totalCount}) and legacy array for safety
    if (Array.isArray(data)) {
      clients.value = data
      totalCount.value = data.length
    } else {
      clients.value = data.items || []
      totalCount.value = data.totalCount ?? clients.value.length
    }
  } catch (e) {
    error.value = `Failed to load clients: ${e.message}`
    console.error('Error fetching clients:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  if (!canCreate.value) {
    deniedMessage.value = t('clients.accessDenied.create')
    deniedPermission.value = Permissions.Clients.CREATE
    showAccessDenied.value = true
    return
  }
  selectedClient.value = null
  showForm.value = true
}

const handleEdit = async (client) => {
  if (!canUpdate.value) {
    deniedMessage.value = t('clients.accessDenied.update')
    deniedPermission.value = Permissions.Clients.UPDATE
    showAccessDenied.value = true
    return
  }
  try {
    // Fetch full client details for editing (redirect URIs, permissions, etc.)
    const response = await fetch(`/api/admin/clients/${client.id}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const full = await response.json()
    selectedClient.value = { ...client, ...full }
  } catch (e) {
    console.error('Failed to fetch client details:', e)
    // Fall back to the partial object so the form still opens
    selectedClient.value = client
  } finally {
    showForm.value = true
  }
}

const handleDelete = async (clientId) => {
  if (!canDelete.value) {
    deniedMessage.value = t('clients.accessDenied.delete')
    deniedPermission.value = Permissions.Clients.DELETE
    showAccessDenied.value = true
    return
  }
  
  if (!confirm(t('clients.confirmDelete'))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/clients/${clientId}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchClients()
  } catch (e) {
    error.value = `Failed to delete client: ${e.message}`
    console.error('Error deleting client:', e)
  }
}

const handleFormSubmit = async () => {
  showForm.value = false
  await fetchClients()
}

const handleFormCancel = () => {
  showForm.value = false
  selectedClient.value = null
}

onMounted(async () => {
  // Load permissions
  await permissionService.loadPermissions()
  
  canRead.value = permissionService.hasPermission(Permissions.Clients.READ)
  canCreate.value = permissionService.hasPermission(Permissions.Clients.CREATE)
  canUpdate.value = permissionService.hasPermission(Permissions.Clients.UPDATE)
  canDelete.value = permissionService.hasPermission(Permissions.Clients.DELETE)
  
  if (!canRead.value) {
    deniedMessage.value = 'You do not have permission to view clients.'
    deniedPermission.value = Permissions.Clients.READ
    showAccessDenied.value = true
    return
  }
  
  fetchClients()
})

// Refetch when paging/filter/sort change
watch([page, pageSize, typeFilter, sort], () => {
  fetchClients()
})

const setPage = (newPage) => {
  const maxPage = Math.max(1, Math.ceil(totalCount.value / pageSize.value))
  page.value = Math.min(Math.max(newPage, 1), maxPage)
}
</script>

<template>
  <!-- Access Denied Dialog -->
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :required-permission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="min-h-screen bg-gray-50">
    <div class="max-w-7xl mx-auto py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="px-4 sm:px-0">
        <div class="flex justify-between items-center mb-6">
          <div>
            <h1 class="text-3xl font-bold text-gray-900">{{ $t('clients.pageTitle') }}</h1>
            <p class="mt-2 text-sm text-gray-600">
              {{ $t('clients.pageSubtitle') }}
            </p>
          </div>
          <button
            v-if="canCreate"
            @click="handleCreate"
            class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            <svg class="-ml-1 mr-2 h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clip-rule="evenodd" />
            </svg>
            {{ $t('clients.createButton') }}
          </button>
        </div>

        <!-- Filters & sorting -->
        <div class="mb-5 grid grid-cols-1 md:grid-cols-4 gap-3">
          <div class="md:col-span-2">
            <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('clients.searchButton') }}</label>
            <div class="flex">
              <input
                v-model="search"
                type="text"
                :placeholder="$t('clients.searchPlaceholder')"
                class="flex-1 rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
              />
              <button
                @click="page = 1; fetchClients()"
                class="ml-2 inline-flex items-center px-3 py-2 border border-gray-300 rounded-md bg-white text-sm text-gray-700 hover:bg-gray-50"
              >
                {{ $t('clients.searchButton') }}
              </button>
            </div>
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('clients.filterLabel') }}</label>
            <select
              v-model="typeFilter"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
            >
              <option value="">{{ $t('clients.filterOptions.all') }}</option>
              <option value="public">{{ $t('clients.filterOptions.public') }}</option>
              <option value="confidential">{{ $t('clients.filterOptions.confidential') }}</option>
            </select>
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('clients.sortLabel') }}</label>
            <select
              v-model="sort"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
            >
              <option value="clientId:asc">{{ $t('clients.sortOptions.idAsc') }}</option>
              <option value="clientId:desc">{{ $t('clients.sortOptions.idDesc') }}</option>
              <option value="displayName:asc">{{ $t('clients.sortOptions.nameAsc') }}</option>
              <option value="displayName:desc">{{ $t('clients.sortOptions.nameDesc') }}</option>
              <option value="redirectUriCount:asc">Redirect URI Count ↑</option>
              <option value="redirectUriCount:desc">Redirect URI Count ↓</option>
              <option value="type:asc">{{ $t('clients.sortOptions.typeAsc') }}</option>
              <option value="type:desc">{{ $t('clients.sortOptions.typeDesc') }}</option>
            </select>
          </div>
        </div>

        <!-- Error Alert -->
        <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
          <div class="flex">
            <div class="flex-shrink-0">
              <svg class="h-5 w-5 text-red-400" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
                <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
              </svg>
            </div>
            <div class="ml-3">
              <p class="text-sm text-red-700">{{ error }}</p>
            </div>
          </div>
        </div>

        <!-- Loading State -->
        <div v-if="loading" class="text-center py-12">
          <svg class="animate-spin h-8 w-8 text-indigo-600 mx-auto" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          <p class="mt-2 text-sm text-gray-600">{{ $t('clients.loadingMessage') }}</p>
        </div>

        <!-- Client Form Modal -->
        <ClientForm
          v-if="showForm"
          :client="selectedClient"
          @submit="handleFormSubmit"
          @cancel="handleFormCancel"
        />

        <!-- Client List -->
        <ClientList
          v-if="!loading && !showForm"
          :clients="clients"
          :can-update="canUpdate"
          :can-delete="canDelete"
          @edit="handleEdit"
          @delete="handleDelete"
        />

        <!-- Pagination -->
        <div v-if="!loading && !showForm && totalCount > 0" class="mt-4 flex items-center justify-between">
          <div class="text-sm text-gray-600">
            {{ $t('pagination.showing', { from: (page - 1) * pageSize + 1, to: Math.min(page * pageSize, totalCount), total: totalCount }) }}
          </div>
          <div class="inline-flex rounded-md shadow-sm" role="group">
            <button @click="setPage(page - 1)" class="px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-l-lg hover:bg-gray-50 disabled:opacity-50" :disabled="page === 1">{{ $t('pagination.previous') }}</button>
            <button @click="setPage(page + 1)" class="px-3 py-2 text-sm font-medium bg-white border-t border-b border-gray-300 hover:bg-gray-50 disabled:opacity-50" :disabled="page * pageSize >= totalCount">{{ $t('pagination.next') }}</button>
            <select v-model.number="pageSize" class="px-2 py-2 text-sm font-medium bg-white border border-gray-300 rounded-r-lg hover:bg-gray-50 ml-2">
              <option :value="10">{{ $t('pagination.perPage', { count: 10 }) }}</option>
              <option :value="25">{{ $t('pagination.perPage', { count: 25 }) }}</option>
              <option :value="50">{{ $t('pagination.perPage', { count: 50 }) }}</option>
            </select>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
