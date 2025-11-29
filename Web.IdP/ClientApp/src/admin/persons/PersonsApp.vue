<script setup>
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PersonForm from './components/PersonForm.vue'
import LinkedAccountsDialog from './components/LinkedAccountsDialog.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import PageHeader from '@/components/common/PageHeader.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()

const persons = ref([])
const loading = ref(true)
const error = ref(null)
const selectedPerson = ref(null)
const showForm = ref(false)
const showLinkedAccountsDialog = ref(false)
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')

// Check permissions
const canCreate = ref(false)
const canUpdate = ref(false)
const canDelete = ref(false)
const canRead = ref(false)

// Load permissions on mount
onMounted(async () => {
  await permissionService.loadPermissions()
  canCreate.value = permissionService.hasPermission(Permissions.Persons.Create)
  canUpdate.value = permissionService.hasPermission(Permissions.Persons.Update)
  canDelete.value = permissionService.hasPermission(Permissions.Persons.Delete)
  canRead.value = permissionService.hasPermission(Permissions.Persons.Read)
  
  // Show access denied if user doesn't have read permission
  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.persons.noPermission')
    deniedPermission.value = Permissions.Persons.Read
    return
  }
  
  fetchPersons()
})

// Paging / filtering / sorting state
const pageSize = ref(10)
const page = ref(1)
const totalCount = ref(0)
const search = ref('')
const sort = ref('lastName:asc')

const fetchPersons = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: String((page.value - 1) * pageSize.value),
      take: String(pageSize.value)
    })
    
    let url = '/api/admin/persons'
    if (search.value) {
      url = '/api/admin/persons/search'
      params.append('term', search.value)
    }
    
    const response = await fetch(`${url}?${params.toString()}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    
    persons.value = data.persons || []
    totalCount.value = data.totalCount ?? persons.value.length
  } catch (e) {
    error.value = t('admin.persons.errors.loadFailed', { message: e.message })
    console.error('Error fetching persons:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  if (!canCreate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.create')
    deniedPermission.value = Permissions.Persons.Create
    return
  }
  selectedPerson.value = null
  showForm.value = true
}

const handleEdit = (person) => {
  if (!canUpdate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.edit')
    deniedPermission.value = Permissions.Persons.Update
    return
  }
  selectedPerson.value = person
  showForm.value = true
}

const handleManageAccounts = (person) => {
  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.persons.noPermission')
    deniedPermission.value = Permissions.Persons.Read
    return
  }
  selectedPerson.value = person
  showLinkedAccountsDialog.value = true
}

const handleDelete = async (person) => {
  if (!canDelete.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.delete')
    deniedPermission.value = Permissions.Persons.Delete
    return
  }
  
  if (!confirm(t('admin.persons.confirmations.delete', { name: `${person.firstName} ${person.lastName}` }))) {
    return
  }
  
  try {
    const response = await fetch(`/api/admin/persons/${person.id}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchPersons()
    alert(t('admin.persons.alerts.deleteSuccess'))
  } catch (e) {
    alert(t('admin.persons.errors.deleteFailed', { message: e.message }))
    console.error('Error deleting person:', e)
  }
}

const handleFormClose = () => {
  showForm.value = false
  selectedPerson.value = null
}

const handleFormSave = async () => {
  showForm.value = false
  selectedPerson.value = null
  await fetchPersons()
}

const handleAccountsClose = () => {
  showLinkedAccountsDialog.value = false
  selectedPerson.value = null
}

const handleAccountsUpdated = async () => {
  await fetchPersons()
}

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

// Watchers
watch([page, pageSize, search], () => {
  fetchPersons()
})
</script>

<template>
  <!-- Access Denied Dialog -->
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :requiredPermission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="persons-app">
    <div class="px-4 py-6">
      <!-- Page Header -->
      <PageHeader 
        :title="$t('admin.persons.pageTitle')" 
        :subtitle="$t('admin.persons.pageSubtitle')"
      >
        <template #actions>
          <button
            v-if="canCreate"
            @click="handleCreate"
            class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
            :disabled="loading"
          >
            <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
            </svg>
            {{ t('admin.persons.createPerson') }}
          </button>
        </template>
      </PageHeader>

      <!-- Error Message -->
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-red-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3 flex-1">
            <p class="text-sm text-red-700">{{ error }}</p>
          </div>
          <div class="ml-auto pl-3">
            <button @click="error = null" class="inline-flex text-red-400 hover:text-red-600 focus:outline-none">
              <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd" />
              </svg>
            </button>
          </div>
        </div>
      </div>

      <!-- Unified Card: Filters + Table + Pagination -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <!-- Filter Section -->
        <div class="p-4 border-b border-gray-200">
          <div class="flex flex-col md:flex-row md:items-center gap-3">
            <!-- Search Input -->
            <div class="flex-1">
              <input
                v-model="search"
                type="text"
                :placeholder="t('admin.persons.search')"
                class="block w-full px-4 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 transition-colors h-10"
              />
            </div>
          </div>
        </div>

        <!-- Loading State -->
        <div v-if="loading" class="flex justify-center items-center py-12">
          <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
          <span class="ml-3 text-gray-600">{{ t('admin.persons.loading') }}</span>
        </div>

        <!-- Empty State -->
        <div v-else-if="persons.length === 0" class="text-center py-12">
          <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
          </svg>
          <p class="mt-2 text-sm text-gray-600">{{ t('admin.persons.noPersons') }}</p>
        </div>

        <!-- Persons Table -->
        <div v-else class="overflow-x-auto">
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.name') }}
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.employeeId') }}
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.department') }}
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.jobTitle') }}
                </th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.linkedAccounts') }}
                </th>
                <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  {{ t('admin.persons.table.actions') }}
                </th>
              </tr>
            </thead>
            <tbody class="bg-white divide-y divide-gray-200">
              <tr v-for="person in persons" :key="person.id" class="hover:bg-gray-50">
                <td class="px-6 py-4 whitespace-nowrap">
                  <div class="flex items-center">
                    <div class="flex-shrink-0 h-10 w-10">
                      <div class="h-10 w-10 rounded-full bg-indigo-100 flex items-center justify-center">
                        <span class="text-indigo-600 font-medium text-sm">
                          {{ person.firstName?.charAt(0) || '' }}{{ person.lastName?.charAt(0) || '' }}
                        </span>
                      </div>
                    </div>
                    <div class="ml-4">
                      <div class="text-sm font-medium text-gray-900">
                        {{ person.firstName }} {{ person.middleName ? person.middleName + ' ' : '' }}{{ person.lastName }}
                      </div>
                      <div v-if="person.nickname" class="text-sm text-gray-500">{{ person.nickname }}</div>
                    </div>
                  </div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                  <span v-if="person.employeeId" class="text-sm text-gray-900">{{ person.employeeId }}</span>
                  <span v-else class="text-sm text-gray-400">-</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                  <span v-if="person.department" class="text-sm text-gray-900">{{ person.department }}</span>
                  <span v-else class="text-sm text-gray-400">-</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                  <span v-if="person.jobTitle" class="text-sm text-gray-900">{{ person.jobTitle }}</span>
                  <span v-else class="text-sm text-gray-400">-</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                  <span v-if="person.accounts && person.accounts.length > 0" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-indigo-100 text-indigo-800">
                    {{ person.accounts.length }}
                  </span>
                  <span v-else class="text-sm text-gray-400">0</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                  <div class="flex items-center justify-end space-x-2">
                    <button
                      v-if="canRead"
                      @click="handleManageAccounts(person)"
                      class="text-indigo-600 hover:text-indigo-900"
                      :title="t('admin.persons.manageAccounts')"
                    >
                      <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                      </svg>
                    </button>
                    <button
                      v-if="canUpdate"
                      @click="handleEdit(person)"
                      class="text-gray-600 hover:text-gray-900"
                      :title="t('admin.persons.edit')"
                    >
                      <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                      </svg>
                    </button>
                    <button
                      v-if="canDelete"
                      @click="handleDelete(person)"
                      class="text-red-600 hover:text-red-900"
                      :title="t('admin.persons.delete')"
                    >
                      <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        <div v-if="!loading && persons.length > 0" class="p-4 border-t border-gray-200 flex items-center justify-between">
        <div class="flex-1 flex justify-between sm:hidden">
          <button
            @click="handlePageChange(page - 1)"
            :disabled="page === 1"
            class="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ t('pagination.previous') }}
          </button>
          <button
            @click="handlePageChange(page + 1)"
            :disabled="page * pageSize >= totalCount"
            class="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ t('pagination.next') }}
          </button>
        </div>
        <div class="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
          <div>
            <p class="text-sm text-gray-700">
              {{ t('pagination.showing', { 
                from: (page - 1) * pageSize + 1, 
                to: Math.min(page * pageSize, totalCount), 
                total: totalCount 
              }) }}
            </p>
          </div>
          <div class="flex items-center space-x-2">
            <select
              :value="pageSize"
              @change="handlePageSizeChange(Number($event.target.value))"
              class="block w-auto pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
            >
              <option :value="10">10 {{ t('pagination.perPage') }}</option>
              <option :value="25">25 {{ t('pagination.perPage') }}</option>
              <option :value="50">50 {{ t('pagination.perPage') }}</option>
              <option :value="100">100 {{ t('pagination.perPage') }}</option>
            </select>
            <nav class="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
              <button
                @click="handlePageChange(page - 1)"
                :disabled="page === 1"
                class="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <span class="sr-only">{{ t('pagination.previous') }}</span>
                <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clip-rule="evenodd" />
                </svg>
              </button>
              <button
                @click="handlePageChange(page + 1)"
                :disabled="page * pageSize >= totalCount"
                class="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <span class="sr-only">{{ t('pagination.next') }}</span>
                <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
                </svg>
              </button>
            </nav>
          </div>
        </div>
        </div>
      </div>

      <!-- Dialogs -->
      <PersonForm
        v-if="showForm"
        :person="selectedPerson"
        @close="handleFormClose"
        @save="handleFormSave"
      />

      <LinkedAccountsDialog
        v-if="showLinkedAccountsDialog"
        :person="selectedPerson"
        :can-update="canUpdate"
        @close="handleAccountsClose"
        @updated="handleAccountsUpdated"
      />
    </div>
  </div>
</template>
