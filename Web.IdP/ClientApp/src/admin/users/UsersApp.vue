<script setup>
import { ref, onMounted, watch } from 'vue'
import UserList from './components/UserList.vue'
import UserForm from './components/UserForm.vue'
import RoleAssignment from './components/RoleAssignment.vue'

const users = ref([])
const loading = ref(true)
const error = ref(null)
const selectedUser = ref(null)
const showForm = ref(false)
const showRoleDialog = ref(false)

// Paging / filtering / sorting state
const pageSize = ref(10)
const page = ref(1)
const totalCount = ref(0)
const search = ref('')
const isActiveFilter = ref('') // '', 'true', 'false'
const sort = ref('email:asc')

const fetchUsers = async () => {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: String((page.value - 1) * pageSize.value),
      take: String(pageSize.value),
      search: search.value || '',
      sort: sort.value || ''
    })
    
    if (isActiveFilter.value !== '') {
      params.append('isActive', isActiveFilter.value)
    }
    
    const response = await fetch(`/api/admin/users?${params.toString()}`)
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    
    users.value = data.items || []
    totalCount.value = data.totalCount ?? users.value.length
  } catch (e) {
    error.value = `Failed to load users: ${e.message}`
    console.error('Error fetching users:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  selectedUser.value = null
  showForm.value = true
}

const handleEdit = (user) => {
  selectedUser.value = user
  showForm.value = true
}

const handleManageRoles = (user) => {
  selectedUser.value = user
  showRoleDialog.value = true
}

const handleDeactivate = async (user) => {
  if (!confirm(`Are you sure you want to deactivate user ${user.email}?`)) {
    return
  }
  
  try {
    const response = await fetch(`/api/admin/users/${user.id}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchUsers()
    alert('User deactivated successfully')
  } catch (e) {
    alert(`Failed to deactivate user: ${e.message}`)
    console.error('Error deactivating user:', e)
  }
}

const handleReactivate = async (user) => {
  if (!confirm(`Are you sure you want to reactivate user ${user.email}?`)) {
    return
  }
  
  try {
    const response = await fetch(`/api/admin/users/${user.id}/reactivate`, {
      method: 'POST'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchUsers()
    alert('User reactivated successfully')
  } catch (e) {
    alert(`Failed to reactivate user: ${e.message}`)
    console.error('Error reactivating user:', e)
  }
}

const handleFormClose = () => {
  showForm.value = false
  selectedUser.value = null
}

const handleFormSave = async () => {
  showForm.value = false
  selectedUser.value = null
  await fetchUsers()
}

const handleRoleDialogClose = () => {
  showRoleDialog.value = false
  selectedUser.value = null
}

const handleRolesSaved = async () => {
  showRoleDialog.value = false
  selectedUser.value = null
  await fetchUsers()
}

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

const handleSearchChange = (newSearch) => {
  search.value = newSearch
  page.value = 1
}

const handleFilterChange = (filter) => {
  isActiveFilter.value = filter
  page.value = 1
}

const handleSortChange = (newSort) => {
  sort.value = newSort
}

// Watchers
watch([page, pageSize, search, isActiveFilter, sort], () => {
  fetchUsers()
})

onMounted(() => {
  fetchUsers()
})
</script>

<template>
  <div class="users-app">
    <div class="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
      <div class="mb-6 flex justify-content-between items-center">
        <h1 class="text-2xl font-bold text-gray-900">User Management</h1>
        <button
          @click="handleCreate"
          class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
        >
          <svg class="-ml-1 mr-2 h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6v6m0 0v6m0-6h6m-6 0H6"></path>
          </svg>
          Create User
        </button>
      </div>

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
            <button
              @click="error = null"
              class="inline-flex text-red-400 hover:text-red-600 focus:outline-none"
            >
              <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd" />
              </svg>
            </button>
          </div>
        </div>
      </div>

      <UserList
        :users="users"
        :loading="loading"
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :search="search"
        :is-active-filter="isActiveFilter"
        :sort="sort"
        @edit="handleEdit"
        @manage-roles="handleManageRoles"
        @deactivate="handleDeactivate"
        @reactivate="handleReactivate"
        @page-change="handlePageChange"
        @page-size-change="handlePageSizeChange"
        @search-change="handleSearchChange"
        @filter-change="handleFilterChange"
        @sort-change="handleSortChange"
      />

      <UserForm
        v-if="showForm"
        :user="selectedUser"
        @close="handleFormClose"
        @save="handleFormSave"
      />

      <RoleAssignment
        v-if="showRoleDialog"
        :user="selectedUser"
        @close="handleRoleDialogClose"
        @save="handleRolesSaved"
      />
    </div>
  </div>
</template>
