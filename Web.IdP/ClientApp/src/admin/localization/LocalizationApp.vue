<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import LocalizationList from './components/LocalizationList.vue'
import LocalizationForm from './components/LocalizationForm.vue'
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
const sort = ref('key:asc')

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
    
    // Note: LocalizationController is at api/admin/localization
    const response = await fetch(`/api/admin/localization?${params}`)
    if (!response.ok) {
        if (response.status === 403) {
            deniedMessage.value = t('localization.accessDenied.read')
            deniedPermission.value = Permissions.Localization.READ // Need to ensure utils/permissionService has this
            showAccessDenied.value = true
            return
        }
        throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    resources.value = data.items || []
    totalCount.value = data.totalCount || 0
  } catch (e) {
    error.value = `Failed to load localization resources: ${e.message}`
    console.error('Error fetching resources:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  if (!canCreate.value) {
    deniedMessage.value = t('localization.accessDenied.create')
    deniedPermission.value = Permissions.Localization.Create
    showAccessDenied.value = true
    return
  }
  selectedResource.value = null
  showForm.value = true
}

const handleEdit = (resource) => {
  if (!canUpdate.value) {
    deniedMessage.value = t('localization.accessDenied.update')
    deniedPermission.value = Permissions.Localization.Update
    showAccessDenied.value = true
    return
  }
  selectedResource.value = resource
  showForm.value = true
}

const handleDelete = async (id) => {
  if (!canDelete.value) {
    deniedMessage.value = t('localization.accessDenied.delete')
    deniedPermission.value = Permissions.Localization.Delete
    showAccessDenied.value = true
    return
  }
  
  if (!confirm(t('localization.confirmDelete'))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/localization/${id}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchResources()
  } catch (e) {
    error.value = `Failed to delete resource: ${e.message}`
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
  await permissionService.loadPermissions()
  
  // Permissions
  const P = Permissions.Localization

  canRead.value = permissionService.hasPermission(P.Read)
  canCreate.value = permissionService.hasPermission(P.Create)
  canUpdate.value = permissionService.hasPermission(P.Update)
  canDelete.value = permissionService.hasPermission(P.Delete)
  
  if (!canRead.value) {
    deniedMessage.value = t('localization.accessDenied.read')
    deniedPermission.value = P.Read
    showAccessDenied.value = true
    return
  }
  
  fetchResources()
})

watch([page, pageSize, search, sort], () => {
  fetchResources()
})
</script>

<template>
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :required-permission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="px-4 py-6">
    <PageHeader 
      :title="$t('localization.pageTitle')" 
      :subtitle="$t('localization.pageSubtitle')"
    >
      <template #actions>
        <button
          v-if="canCreate"
          @click="handleCreate"
          data-test-id="create-resource-btn"
          class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          :disabled="loading"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          {{ $t('localization.createButton') }}
        </button>
      </template>
    </PageHeader>

    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <div class="bg-white shadow-sm rounded-lg border border-gray-200"
         v-loading="{ loading: loading, overlay: true, message: $t('localization.loadingMessage') }">
      
      <!-- Filter -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('localization.searchPlaceholder')" />
          </div>
          <div class="flex gap-2">
            <select v-model="sort" data-test-id="sort-select" class="block rounded-md border-gray-300 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500 sm:text-sm transition-colors h-10">
              <option value="key:asc">{{ $t('localization.sort.keyAsc') }}</option>
              <option value="key:desc">{{ $t('localization.sort.keyDesc') }}</option>
              <option value="category:asc">{{ $t('localization.sort.categoryAsc') }}</option>
              <option value="category:desc">{{ $t('localization.sort.categoryDesc') }}</option>
            </select>
          </div>
        </div>
      </div>

      <div v-if="!loading && resources.length === 0" class="px-6 py-12">
        <div class="text-center text-gray-500">{{ $t('localization.noResources') }}</div>
      </div>

      <template v-if="!loading && resources.length > 0">
        <LocalizationList
          :resources="resources"
          :can-update="canUpdate"
          :can-delete="canDelete"
          @edit="handleEdit"
          @delete="handleDelete"
        />
      </template>

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

  <LocalizationForm
    v-if="showForm"
    :resource="selectedResource"
    @submit="handleFormSubmit"
    @cancel="handleFormCancel"
  />
</template>
