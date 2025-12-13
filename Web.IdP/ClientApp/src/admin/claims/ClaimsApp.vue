<template>
  <div class="px-4 py-6">
    <!-- Header -->
    <PageHeader 
      :title="$t('claims.pageTitle')" 
      :subtitle="$t('claims.pageSubtitle')"
    >
      <template #actions>
        <button
          @click="openCreateModal"
          class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          :disabled="loading"
          data-test-id="claims-create-btn"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          {{ $t('claims.createButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- Error State -->
    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <!-- Main Content -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200"
         v-loading="{ loading: loading, overlay: true, message: $t('claims.loadingMessage') }">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('claims.searchPlaceholder')" data-test-id="claims-search-input" />
          </div>
          
          <!-- Sort and Apply -->
          <div class="flex gap-2">
            <select 
              v-model="sortBy" 
              class="block rounded-md border-gray-300 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500 sm:text-sm transition-colors h-10"
            >
              <option value="name">{{ $t('claims.sortOptions.name') }}</option>
              <option value="displayname">{{ $t('claims.sortOptions.displayName') }}</option>
              <option value="claimtype">{{ $t('claims.sortOptions.claimType') }}</option>
              <option value="type">{{ $t('claims.sortOptions.type') }}</option>
            </select>
            <select 
              v-model="sortDirection" 
              class="block rounded-md border-gray-300 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500 sm:text-sm transition-colors h-10"
            >
              <option value="asc">{{ $t('claims.sortDirection.asc') }}</option>
              <option value="desc">{{ $t('claims.sortDirection.desc') }}</option>
            </select>
          </div>
        </div>
      </div>
      <!-- Table Section -->
      <div class="overflow-x-auto">
        <table class="min-w-full divide-y divide-gray-200">
          <thead class="bg-gray-50">
            <tr>
              <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.name') }}
              </th>
              <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.displayName') }}
              </th>
              <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.claimType') }}
              </th>
              <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.type') }}
              </th>
              <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.scopes') }}
              </th>
              <th scope="col" class="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.actions') }}
              </th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
            <tr v-if="!loading && claims.length === 0">
              <td colspan="6" class="px-6 py-4">
                <div class="text-center py-8">
                  <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                  </svg>
                  <h3 class="mt-2 text-sm font-medium text-gray-900">{{ $t('claims.emptyState.title') }}</h3>
                  <p class="mt-1 text-sm text-gray-500">{{ $t('claims.emptyState.message') }}</p>
                </div>
              </td>
            </tr>
            <template v-if="!loading && claims.length > 0">
              <tr v-for="claim in claims" :key="claim.id" class="hover:bg-gray-50">
              <td class="px-6 py-4 whitespace-nowrap">
                <div class="flex items-center">
                  <div class="text-sm font-medium text-gray-900">{{ claim.name }}</div>
                  <span v-if="claim.isRequired" class="ml-2 inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-red-100 text-red-800">
                    {{ $t('claims.badges.required') }}
                  </span>
                </div>
              </td>
              <td class="px-6 py-4">
                <div class="text-sm text-gray-900">{{ claim.displayName }}</div>
                <div class="text-sm text-gray-500">{{ claim.description }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                <code class="text-xs bg-gray-100 px-2 py-1 rounded">{{ claim.claimType }}</code>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <span v-if="claim.isStandard" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                  {{ $t('claims.badges.standard') }}
                </span>
                <span v-else class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                  {{ $t('claims.badges.custom') }}
                </span>
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {{ $t('claims.scopeCount', { count: claim.scopeCount }) }}
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-center">
                <div class="inline-flex gap-1">
                  <button
                    @click="openEditModal(claim)"
                    class="inline-flex items-center px-3 py-1.5 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                    :title="$t('claims.actions.edit')"
                    data-test-id="claims-edit-btn"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </button>
                  <button
                    v-if="!claim.isStandard"
                    @click="deleteClaim(claim)"
                    class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-sm font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50 disabled:cursor-not-allowed"
                    :disabled="claim.scopeCount > 0"
                    :title="$t('claims.actions.delete')"
                    data-test-id="claims-delete-btn"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                  </button>
                </div>
              </td>
            </tr>
            </template>
          </tbody>
        </table>
      </div>

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

    <!-- Create/Edit Modal -->
    <ClaimFormModal
      :show="showModal"
      :claim="editingClaim"
      :error="modalError"
      @close="closeModal"
      @save="saveClaim"
    />
  </div>
</template>

<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import SearchInput from '@/components/common/SearchInput.vue'
import Pagination from '@/components/common/Pagination.vue'
import ClaimFormModal from './components/ClaimFormModal.vue'

const { t } = useI18n()

const claims = ref([])
const loading = ref(true)
const error = ref(null)
const showModal = ref(false)
const editingClaim = ref(null)
const modalError = ref(null)

// Pagination and filtering
const search = ref('')
const sortBy = ref('name')
const sortDirection = ref('asc')
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)

const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value))

async function fetchClaims() {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: ((page.value - 1) * pageSize.value).toString(),
      take: pageSize.value.toString(),
      sortBy: sortBy.value,
      sortDirection: sortDirection.value
    })
    
    if (search.value.trim()) {
      params.append('search', search.value.trim())
    }
    
    const response = await fetch(`/api/admin/claims?${params}`)
    if (!response.ok) throw new Error('Failed to fetch claims')
    
    const data = await response.json()
    claims.value = data.items || []
    totalCount.value = data.totalCount || 0
  } catch (err) {
    error.value = err.message
    claims.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

function openCreateModal() {
  editingClaim.value = null
  modalError.value = null
  showModal.value = true
}

function openEditModal(claim) {
  editingClaim.value = claim
  modalError.value = null
  showModal.value = true
}

function closeModal() {
  showModal.value = false
  editingClaim.value = null
}

async function saveClaim(formData) {
  error.value = null

  try {
    const url = editingClaim.value
      ? `/api/admin/claims/${editingClaim.value.id}`
      : '/api/admin/claims'
    
    const method = editingClaim.value ? 'PUT' : 'POST'
    
    const response = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(formData)
    })

    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.message || t('claims.saveError'))
    }

    closeModal()
    await fetchClaims()
  } catch (err) {
    modalError.value = err.message
    // Do not throw back to form as we handled it via prop
  }
}

async function deleteClaim(claim) {
  if (!confirm(t('claims.confirmDelete', { name: claim.name }))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/claims/${claim.id}`, {
      method: 'DELETE'
    })

    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.message || t('claims.deleteError'))
    }

    await fetchClaims()
  } catch (err) {
    error.value = err.message
  }
}

watch([search, sortBy, sortDirection, page, pageSize], () => {
  fetchClaims()
})

onMounted(() => {
  fetchClaims()
})
</script>
