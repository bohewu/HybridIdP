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
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('claims.searchPlaceholder')" />
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
            <tr v-if="loading">
              <td colspan="6" class="px-6 py-4">
                <div class="text-center text-gray-500">{{ $t('claims.loadingMessage') }}</div>
              </td>
            </tr>
            <tr v-else-if="!loading && claims.length === 0">
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
            <template v-else>
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
      <div class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-3 px-4 py-3 border-t border-gray-200">
        <div class="text-sm text-gray-700">{{ $t('claims.pagination.total', { count: totalCount }) }}</div>
        <div class="inline-flex gap-2">
          <button 
            class="px-4 py-2 bg-white border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10" 
            :disabled="skip === 0 || loading" 
            @click="prevPage"
          >
            {{ $t('claims.pagination.prev') }}
          </button>
          <button 
            class="px-4 py-2 bg-white border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10" 
            :disabled="skip + take >= totalCount || loading" 
            @click="nextPage"
          >
            {{ $t('claims.pagination.next') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Create/Edit Modal -->
    <div v-if="showModal" class="fixed z-10 inset-0 overflow-y-auto">
      <div class="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
        <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" @click="closeModal"></div>

        <div class="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-lg sm:w-full">
          <form @submit.prevent="saveClaim">
            <div class="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
              <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                {{ editingClaim ? $t('claims.form.editTitle') : $t('claims.form.createTitle') }}
              </h3>

              <!-- Name -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.name') }} *</label>
                <input
                  v-model="formData.name"
                  type="text"
                  required
                  :disabled="editingClaim?.isStandard"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
                  :placeholder="$t('claims.form.namePlaceholder')"
                />
              </div>

              <!-- Display Name -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.displayName') }} *</label>
                <input
                  v-model="formData.displayName"
                  type="text"
                  required
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm transition-colors h-10 px-3"
                  :placeholder="$t('claims.form.displayNamePlaceholder')"
                />
              </div>

              <!-- Description -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.description') }}</label>
                <textarea
                  v-model="formData.description"
                  rows="2"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm transition-colors px-3 py-2"
                  :placeholder="$t('claims.form.descriptionPlaceholder')"
                ></textarea>
              </div>

              <!-- Claim Type -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.claimType') }} *</label>
                <input
                  v-model="formData.claimType"
                  type="text"
                  required
                  :disabled="editingClaim?.isStandard"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
                  :placeholder="$t('claims.form.claimTypePlaceholder')"
                />
                <p class="mt-1.5 text-xs text-gray-500">{{ $t('claims.form.claimTypeHelp') }}</p>
              </div>

              <!-- User Property Path -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.userPropertyPath') }} *</label>
                <input
                  v-model="formData.userPropertyPath"
                  type="text"
                  required
                  :disabled="editingClaim?.isStandard"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
                  :placeholder="$t('claims.form.userPropertyPathPlaceholder')"
                />
                <p class="mt-1.5 text-xs text-gray-500">{{ $t('claims.form.userPropertyPathHelp') }}</p>
              </div>

              <!-- Data Type -->
              <div class="mb-5">
                <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ $t('claims.form.dataType') }} *</label>
                <select
                  v-model="formData.dataType"
                  :disabled="editingClaim?.isStandard"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
                >
                  <option value="String">{{ $t('claims.form.dataTypes.string') }}</option>
                  <option value="Boolean">{{ $t('claims.form.dataTypes.boolean') }}</option>
                  <option value="Integer">{{ $t('claims.form.dataTypes.integer') }}</option>
                  <option value="DateTime">{{ $t('claims.form.dataTypes.dateTime') }}</option>
                  <option value="JSON">{{ $t('claims.form.dataTypes.json') }}</option>
                </select>
              </div>

              <!-- Is Required -->
              <div class="mb-5">
                <label class="flex items-center">
                  <input
                    v-model="formData.isRequired"
                    type="checkbox"
                    :disabled="editingClaim?.isStandard"
                    class="rounded border-gray-300 text-indigo-600 shadow-sm focus:border-indigo-300 focus:ring focus:ring-indigo-200 focus:ring-opacity-50 disabled:bg-gray-100 h-4 w-4"
                  />
                  <span class="ml-2 text-sm text-gray-700">{{ $t('claims.form.isRequired') }}</span>
                </label>
              </div>

              <div v-if="editingClaim?.isStandard" class="mt-4 p-3 bg-blue-50 rounded-md">
                <p class="text-sm text-blue-800">
                  {{ $t('claims.form.standardNote') }}
                </p>
              </div>
            </div>

            <div class="bg-gray-50 px-4 py-2.5 sm:px-6 sm:flex sm:flex-row-reverse">
              <button
                type="submit"
                :disabled="saving"
                class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-indigo-600 text-base font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50"
              >
                {{ saving ? $t('claims.form.saving') : $t('claims.form.save') }}
              </button>
              <button
                type="button"
                @click="closeModal"
                :disabled="saving"
                class="mt-2.5 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50"
              >
                {{ $t('claims.form.cancel') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import SearchInput from '@/components/common/SearchInput.vue'

const { t } = useI18n()

const claims = ref([])
const loading = ref(false)
const error = ref(null)
const showModal = ref(false)
const editingClaim = ref(null)
const saving = ref(false)

// Pagination and filtering
const search = ref('')
const sortBy = ref('name')
const sortDirection = ref('asc')
const skip = ref(0)
const take = ref(20)
const totalCount = ref(0)

const formData = ref({
  name: '',
  displayName: '',
  description: '',
  claimType: '',
  userPropertyPath: '',
  dataType: 'String',
  isRequired: false
})

async function fetchClaims(newSkip = null) {
  if (newSkip !== null) {
    skip.value = newSkip
  }
  
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: skip.value.toString(),
      take: take.value.toString(),
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

function prevPage() {
  if (skip.value >= take.value) {
    fetchClaims(skip.value - take.value)
  }
}

function nextPage() {
  if (skip.value + take.value < totalCount.value) {
    fetchClaims(skip.value + take.value)
  }
}

function openCreateModal() {
  editingClaim.value = null
  formData.value = {
    name: '',
    displayName: '',
    description: '',
    claimType: '',
    userPropertyPath: '',
    dataType: 'String',
    isRequired: false
  }
  showModal.value = true
}

function openEditModal(claim) {
  editingClaim.value = claim
  formData.value = {
    name: claim.name,
    displayName: claim.displayName,
    description: claim.description || '',
    claimType: claim.claimType,
    userPropertyPath: claim.userPropertyPath,
    dataType: claim.dataType,
    isRequired: claim.isRequired
  }
  showModal.value = true
}

function closeModal() {
  showModal.value = false
  editingClaim.value = null
}

async function saveClaim() {
  saving.value = true
  error.value = null

  try {
    const url = editingClaim.value
      ? `/api/admin/claims/${editingClaim.value.id}`
      : '/api/admin/claims'
    
    const method = editingClaim.value ? 'PUT' : 'POST'
    
    const response = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(formData.value)
    })

    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.message || t('claims.saveError'))
    }

    closeModal()
    await fetchClaims()
  } catch (err) {
    error.value = err.message
  } finally {
    saving.value = false
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

watch([search, sortBy, sortDirection], () => {
  fetchClaims(0)
})

onMounted(() => {
  fetchClaims()
})
</script>
