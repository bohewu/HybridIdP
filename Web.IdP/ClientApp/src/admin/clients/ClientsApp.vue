<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ClientList from './components/ClientList.vue'
import ClientForm from './components/ClientForm.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import PageHeader from '@/components/common/PageHeader.vue'
import permissionService, { Permissions } from '@/utils/permissionService'
import SearchInput from '@/components/common/SearchInput.vue'
import Pagination from '@/components/common/Pagination.vue'

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

const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value))

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
    deniedPermission.value = Permissions.Clients.Create
    showAccessDenied.value = true
    return
  }
  selectedClient.value = null
  showForm.value = true
}

const handleEdit = async (client) => {
  if (!canUpdate.value) {
    deniedMessage.value = t('clients.accessDenied.update')
    deniedPermission.value = Permissions.Clients.Update
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
    deniedPermission.value = Permissions.Clients.Delete
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

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

onMounted(async () => {
  // Force reload permissions to ensure fresh data (cache might be stale after login)
  await permissionService.reloadPermissions()
  
  canRead.value = permissionService.hasPermission(Permissions.Clients.Read)
  canCreate.value = permissionService.hasPermission(Permissions.Clients.Create)
  canUpdate.value = permissionService.hasPermission(Permissions.Clients.Update)
  canDelete.value = permissionService.hasPermission(Permissions.Clients.Delete)
  
  if (!canRead.value) {
    deniedMessage.value = 'You do not have permission to view clients.'
    deniedPermission.value = Permissions.Clients.Read
    showAccessDenied.value = true
    return
  }
  
  fetchClients()
})

// Refetch when paging/filter/sort change
watch([page, pageSize, search, typeFilter, sort], () => {
  fetchClients()
})
</script>

<template>
  <!-- Access Denied Dialog -->
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :required-permission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="px-4 py-6">
    <!-- Header -->
    <PageHeader 
      :title="$t('clients.pageTitle')" 
      :subtitle="$t('clients.pageSubtitle')"
    >
      <template #actions>
        <button
          v-if="canCreate"
          @click="handleCreate"
          class="inline-flex items-center justify-center px-4 py-2 bg-google-500 text-white text-sm font-medium rounded-md hover:bg-google-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-google-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          :disabled="loading"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          {{ $t('clients.createButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- Error State -->
    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <!-- Main Content -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200"
         v-loading="{ loading: loading, overlay: true, message: $t('clients.loadingMessage') }">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('clients.searchPlaceholder')" />
          </div>
          
          <!-- Type Filter and Sort -->
          <div class="flex gap-2">
            <select 
              v-model="typeFilter" 
              class="block rounded-md border-gray-300 focus:border-google-500 focus:ring-2 focus:ring-google-500 sm:text-sm transition-colors h-10"
            >
              <option value="">{{ $t('clients.filterOptions.all') }}</option>
              <option value="public">{{ $t('clients.filterOptions.public') }}</option>
              <option value="confidential">{{ $t('clients.filterOptions.confidential') }}</option>
            </select>
            <select 
              v-model="sort" 
              class="block rounded-md border-gray-300 focus:border-google-500 focus:ring-2 focus:ring-google-500 sm:text-sm transition-colors h-10"
            >
              <option value="clientId:asc">{{ $t('clients.sortOptions.idAsc') }}</option>
              <option value="clientId:desc">{{ $t('clients.sortOptions.idDesc') }}</option>
              <option value="displayName:asc">{{ $t('clients.sortOptions.nameAsc') }}</option>
              <option value="displayName:desc">{{ $t('clients.sortOptions.nameDesc') }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- Empty State -->
      <div v-if="!loading && clients.length === 0" class="px-6 py-12">
        <div class="text-center text-gray-500">{{ $t('clients.noClients') }}</div>
      </div>

      <!-- Client List -->
      <template v-if="!loading && clients.length > 0">
        <ClientList
          :clients="clients"
          :can-update="canUpdate"
          :can-delete="canDelete"
          @edit="handleEdit"
          @delete="handleDelete"
        />
      </template>

      <!-- Pagination -->
      <Pagination
        v-if="!loading && totalCount > 0"
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :total-pages="totalPages"
        @page-change="handlePageChange"
        @page-size-change="handlePageSizeChange"
      />
    </div>
  </div>

  <!-- Client Form Modal -->
  <ClientForm
    v-if="showForm"
    :client="selectedClient"
    @submit="handleFormSubmit"
    @cancel="handleFormCancel"
  />
</template>
