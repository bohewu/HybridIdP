<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ScopeList from './components/ScopeList.vue'
import ScopeForm from './components/ScopeForm.vue'
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

const scopes = ref([])
const loading = ref(true)
const error = ref(null)
const selectedScope = ref(null)
const showForm = ref(false)

// Pagination & filtering
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)
const search = ref('')
const sort = ref('name:asc')

const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value))

const fetchScopes = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: ((page.value - 1) * pageSize.value).toString(),
      take: pageSize.value.toString(),
      search: search.value,
      sort: sort.value
    })
    
    const response = await fetch(`/api/admin/scopes?${params}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    scopes.value = data.items || data
    totalCount.value = data.totalCount || data.length
  } catch (e) {
    error.value = `Failed to load scopes: ${e.message}`
    console.error('Error fetching scopes:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  if (!canCreate.value) {
    deniedMessage.value = t('scopes.accessDenied.create')
    deniedPermission.value = Permissions.Scopes.CREATE
    showAccessDenied.value = true
    return
  }
  selectedScope.value = null
  showForm.value = true
}

const handleEdit = (scope) => {
  if (!canUpdate.value) {
    deniedMessage.value = t('scopes.accessDenied.update')
    deniedPermission.value = Permissions.Scopes.UPDATE
    showAccessDenied.value = true
    return
  }
  selectedScope.value = scope
  showForm.value = true
}

const handleDelete = async (scopeName) => {
  if (!canDelete.value) {
    deniedMessage.value = t('scopes.accessDenied.delete')
    deniedPermission.value = Permissions.Scopes.DELETE
    showAccessDenied.value = true
    return
  }
  
  if (!confirm(t('scopes.confirmDelete'))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/scopes/${encodeURIComponent(scopeName)}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchScopes()
  } catch (e) {
    error.value = `Failed to delete scope: ${e.message}`
    console.error('Error deleting scope:', e)
  }
}

const handleFormSubmit = async () => {
  showForm.value = false
  await fetchScopes()
}

const handleFormCancel = () => {
  showForm.value = false
  selectedScope.value = null
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
  
  canRead.value = permissionService.hasPermission(Permissions.Scopes.READ)
  canCreate.value = permissionService.hasPermission(Permissions.Scopes.CREATE)
  canUpdate.value = permissionService.hasPermission(Permissions.Scopes.UPDATE)
  canDelete.value = permissionService.hasPermission(Permissions.Scopes.DELETE)
  
  if (!canRead.value) {
    deniedMessage.value = 'You do not have permission to view scopes.'
    deniedPermission.value = Permissions.Scopes.READ
    showAccessDenied.value = true
    return
  }
  
  fetchScopes()
})

// Refetch when paging/search/sort change
watch([page, pageSize, search, sort], () => {
  fetchScopes()
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
      :title="$t('scopes.pageTitle')" 
      :subtitle="$t('scopes.pageSubtitle')"
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
          {{ $t('scopes.createButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- Error State -->
    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <!-- Main Content -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200"
         v-loading="{ loading: loading, overlay: true, message: $t('scopes.loadingMessage') }">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('scopes.searchPlaceholder')" />
          </div>
          
          <!-- Sort -->
          <div class="flex gap-2">
            <select 
              v-model="sort" 
              class="block rounded-md border-gray-300 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500 sm:text-sm transition-colors h-10"
            >
              <option value="name:asc">{{ $t('scopes.sortOptions.nameAsc') }}</option>
              <option value="name:desc">{{ $t('scopes.sortOptions.nameDesc') }}</option>
              <option value="displayName:asc">{{ $t('scopes.sortOptions.displayNameAsc') }}</option>
              <option value="displayName:desc">{{ $t('scopes.sortOptions.displayNameDesc') }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- Empty State -->
      <div v-if="!loading && scopes.length === 0" class="px-6 py-12">
        <div class="text-center text-gray-500">{{ $t('scopes.noScopesMessage') }}</div>
      </div>

      <!-- Scope List -->
      <template v-if="!loading && scopes.length > 0">
        <ScopeList
          :scopes="scopes"
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

  <!-- Scope Form Modal -->
  <ScopeForm
    v-if="showForm"
    :scope="selectedScope"
    @submit="handleFormSubmit"
    @cancel="handleFormCancel"
  />
</template>
