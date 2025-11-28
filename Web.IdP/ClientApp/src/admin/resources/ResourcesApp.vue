<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ResourceList from './components/ResourceList.vue'
import ResourceForm from './components/ResourceForm.vue'
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

const resources = ref([])
const loading = ref(true)
const error = ref(null)
const selectedResource = ref(null)
const showForm = ref(false)

// Pagination & filtering
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)
const search = ref('')
const sort = ref('name:asc')

const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value))

const fetchResources = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      page: page.value.toString(),
      pageSize: pageSize.value.toString(),
      search: search.value,
      sort: sort.value
    })
    
    const response = await fetch(`/api/admin/resources?${params}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    resources.value = data.items || []
    totalCount.value = data.totalCount || 0
  } catch (e) {
    error.value = `Failed to load API resources: ${e.message}`
    console.error('Error fetching resources:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  if (!canCreate.value) {
    deniedMessage.value = t('resources.accessDenied.create')
    deniedPermission.value = Permissions.Scopes.CREATE
    showAccessDenied.value = true
    return
  }
  selectedResource.value = null
  showForm.value = true
}

const handleEdit = (resource) => {
  if (!canUpdate.value) {
    deniedMessage.value = t('resources.accessDenied.update')
    deniedPermission.value = Permissions.Scopes.UPDATE
    showAccessDenied.value = true
    return
  }
  selectedResource.value = resource
  showForm.value = true
}

const handleDelete = async (resourceId) => {
  if (!canDelete.value) {
    deniedMessage.value = t('resources.accessDenied.delete')
    deniedPermission.value = Permissions.Scopes.DELETE
    showAccessDenied.value = true
    return
  }
  
  if (!confirm(t('resources.confirmDelete'))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/resources/${resourceId}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchResources()
  } catch (e) {
    error.value = `Failed to delete API resource: ${e.message}`
    console.error('Error deleting resource:', e)
  }
}

const handleFormSubmit = async () => {
  showForm.value = false
  await fetchResources()
}

const handleFormCancel = () => {
  showForm.value = false
  selectedResource.value = null
}

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

onMounted(async () => {
  // Load permissions (reusing Scopes permissions for now)
  await permissionService.loadPermissions()
  
  canRead.value = permissionService.hasPermission(Permissions.Scopes.READ)
  canCreate.value = permissionService.hasPermission(Permissions.Scopes.CREATE)
  canUpdate.value = permissionService.hasPermission(Permissions.Scopes.UPDATE)
  canDelete.value = permissionService.hasPermission(Permissions.Scopes.DELETE)
  
  if (!canRead.value) {
    deniedMessage.value = t('resources.accessDenied.read')
    deniedPermission.value = Permissions.Scopes.READ
    showAccessDenied.value = true
    return
  }
  
  fetchResources()
})

// Refetch when paging/search/sort change
watch([page, pageSize, search, sort], () => {
  fetchResources()
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
      :title="$t('resources.pageTitle')" 
      :subtitle="$t('resources.pageSubtitle')"
    >
      <template #actions>
        <button
          v-if="canCreate"
          @click="handleCreate"
          class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          :disabled="loading"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          {{ $t('resources.createButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- Error State -->
    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <!-- Main Content -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200"
         v-loading="{ loading: loading, overlay: true, message: $t('resources.loadingMessage') }">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('resources.searchPlaceholder')" />
          </div>
          
          <!-- Sort -->
          <div class="flex gap-2">
            <select 
              v-model="sort" 
              class="block rounded-md border-gray-300 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500 sm:text-sm transition-colors h-10"
            >
              <option value="name:asc">{{ $t('resources.sortOptions.nameAsc') }}</option>
              <option value="name:desc">{{ $t('resources.sortOptions.nameDesc') }}</option>
              <option value="displayName:asc">{{ $t('resources.sortOptions.displayNameAsc') }}</option>
              <option value="displayName:desc">{{ $t('resources.sortOptions.displayNameDesc') }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- Empty State -->
      <div v-if="!loading && resources.length === 0" class="px-6 py-12">
        <div class="text-center text-gray-500">{{ $t('resources.noResourcesMessage') }}</div>
      </div>

      <!-- Resource List -->
      <template v-if="!loading && resources.length > 0">
        <ResourceList
          :resources="resources"
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

  <!-- Resource Form Modal -->
  <ResourceForm
    v-if="showForm"
    :resource="selectedResource"
    @submit="handleFormSubmit"
    @cancel="handleFormCancel"
  />
</template>
