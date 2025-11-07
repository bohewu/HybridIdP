<template>
  <div class="p-6">
    <!-- Header -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">{{ $t('claims.pageTitle') }}</h1>
        <p class="mt-1 text-sm text-gray-600">
          {{ $t('claims.pageSubtitle') }}
        </p>
      </div>
      <button
        @click="openCreateModal"
        class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
      >
        <svg class="-ml-1 mr-2 h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
        </svg>
        {{ $t('claims.createButton') }}
      </button>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="flex justify-center items-center py-12">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="rounded-md bg-red-50 p-4">
      <div class="flex">
        <div class="flex-shrink-0">
          <svg class="h-5 w-5 text-red-400" fill="currentColor" viewBox="0 0 20 20">
            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
          </svg>
        </div>
        <div class="ml-3">
          <h3 class="text-sm font-medium text-red-800">{{ $t('claims.errorLoading') }}</h3>
          <div class="mt-2 text-sm text-red-700">{{ error }}</div>
          <button @click="fetchClaims" class="mt-3 text-sm font-medium text-red-800 hover:text-red-900">
            {{ $t('claims.tryAgain') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Claims Table -->
    <div v-else class="bg-white shadow-sm rounded-lg border border-gray-200">
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
              <th scope="col" class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('claims.table.actions') }}
              </th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
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
            <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
              <button
                @click="openEditModal(claim)"
                class="text-indigo-600 hover:text-indigo-900 mr-3"
              >
                {{ $t('claims.actions.edit') }}
              </button>
              <button
                v-if="!claim.isStandard"
                @click="deleteClaim(claim)"
                class="text-red-600 hover:text-red-900"
                :disabled="claim.scopeCount > 0"
                :class="{ 'opacity-50 cursor-not-allowed': claim.scopeCount > 0 }"
              >
                {{ $t('claims.actions.delete') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <!-- Empty State -->
      <div v-if="claims.length === 0" class="text-center py-12">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
        <h3 class="mt-2 text-sm font-medium text-gray-900">{{ $t('claims.emptyState.title') }}</h3>
        <p class="mt-1 text-sm text-gray-500">{{ $t('claims.emptyState.message') }}</p>
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
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const claims = ref([])
const loading = ref(false)
const error = ref(null)
const showModal = ref(false)
const editingClaim = ref(null)
const saving = ref(false)

const formData = ref({
  name: '',
  displayName: '',
  description: '',
  claimType: '',
  userPropertyPath: '',
  dataType: 'String',
  isRequired: false
})

async function fetchClaims() {
  loading.value = true
  error.value = null
  try {
    const response = await fetch('/api/admin/claims')
    if (!response.ok) throw new Error('Failed to fetch claims')
    claims.value = await response.json()
  } catch (err) {
    error.value = err.message
  } finally {
    loading.value = false
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

onMounted(() => {
  fetchClaims()
})
</script>
