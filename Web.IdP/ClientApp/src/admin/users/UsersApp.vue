<script setup>
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import UserList from './components/UserList.vue'
import UserForm from './components/UserForm.vue'
import RoleAssignment from './components/RoleAssignment.vue'
import UserSessions from './components/UserSessions.vue'
import LoginHistoryDialog from './components/LoginHistoryDialog.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import PageHeader from '@/components/common/PageHeader.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()

const users = ref([])
const loading = ref(true)
const error = ref(null)
const selectedUser = ref(null)
const showForm = ref(false)
const showRoleDialog = ref(false)
const showSessionsDialog = ref(false)
const showLoginHistoryDialog = ref(false)
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')
const securityPolicy = ref(null)

// Check permissions
const canCreate = ref(false)
const canUpdate = ref(false)
const canDelete = ref(false)
const canRead = ref(false)

// Load permissions on mount
onMounted(async () => {
  await permissionService.loadPermissions()
  canCreate.value = permissionService.hasPermission(Permissions.Users.Create)
  canUpdate.value = permissionService.hasPermission(Permissions.Users.Update)
  canDelete.value = permissionService.hasPermission(Permissions.Users.Delete)
  canRead.value = permissionService.hasPermission(Permissions.Users.Read)
  
  // Show access denied if user doesn't have read permission
  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.users.noPermission')
    deniedPermission.value = Permissions.Users.Read
    return
  }
  
  fetchUsers()
})

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
    error.value = t('admin.users.errors.loadFailed', { message: e.message })
    console.error('Error fetching users:', e)
  } finally {
    loading.value = false
  }
}

const fetchSecurityPolicy = async () => {
  if (securityPolicy.value) return // Already fetched
  try {
    const response = await fetch('/api/admin/security/policies')
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    securityPolicy.value = await response.json()
  } catch (e) {
    console.error('Error fetching security policy:', e)
    // We can still proceed, the form will use default/less strict validation
    error.value = 'Could not load password policies. Please try again.'
  }
}

const handleCreate = async () => {
  if (!canCreate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.create')
    deniedPermission.value = Permissions.Users.Create
    return
  }
  await fetchSecurityPolicy()
  selectedUser.value = null
  showForm.value = true
}

const handleEdit = async (user) => {
  if (!canUpdate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.edit')
    deniedPermission.value = Permissions.Users.Update
    return
  }
  await fetchSecurityPolicy()
  selectedUser.value = user
  showForm.value = true
}

const handleManageRoles = (user) => {
  if (!canUpdate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.manageRoles')
    deniedPermission.value = Permissions.Users.Update
    return
  }
  selectedUser.value = user
  showRoleDialog.value = true
}

const handleManageSessions = (user) => {
  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.users.noPermission')
    deniedPermission.value = Permissions.Users.Read
    return
  }
  selectedUser.value = user
  showSessionsDialog.value = true
}

const handleViewLoginHistory = (user) => {
  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.users.noPermission')
    deniedPermission.value = Permissions.Users.Read
    return
  }
  selectedUser.value = user
  showLoginHistoryDialog.value = true
}

const handleDeactivate = async (user) => {
  if (!canDelete.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.deactivate')
    deniedPermission.value = Permissions.Users.Delete
    return
  }
  
  if (!confirm(t('admin.users.confirmations.deactivate', { email: user.email }))) {
    return
  }
  
  try {
    const response = await fetch(`/api/admin/users/${user.id}/deactivate`, {
      method: 'POST'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchUsers()
    alert(t('admin.users.alerts.deactivatedSuccess'))
  } catch (e) {
    alert(t('admin.users.errors.deactivateFailed', { message: e.message }))
    console.error('Error deactivating user:', e)
  }
}

const handleDelete = async (user) => {
  if (!canDelete.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.delete')
    deniedPermission.value = Permissions.Users.Delete
    return
  }
  
  if (!confirm(t('admin.users.confirmations.deleteWarning', { email: user.email }))) {
    return
  }
  
  // Second confirmation for safety
  if (!confirm(t('admin.users.confirmations.deleteFinal', { email: user.email }))) {
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
    alert(t('admin.users.alerts.deletedSuccess'))
  } catch (e) {
    alert(t('admin.users.errors.deleteFailed', { message: e.message }))
    console.error('Error deleting user:', e)
  }
}

const handleReactivate = async (user) => {
  if (!canUpdate.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('deniedMessages.reactivate')
    deniedPermission.value = Permissions.Users.Update
    return
  }
  
  if (!confirm(t('admin.users.confirmations.reactivate', { email: user.email }))) {
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
    alert(t('admin.users.alerts.reactivatedSuccess'))
  } catch (e) {
    alert(t('admin.users.errors.reactivateFailed', { message: e.message }))
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

const handleSessionsClose = () => {
  showSessionsDialog.value = false
  selectedUser.value = null
}

const handleLoginHistoryClose = () => {
  showLoginHistoryDialog.value = false
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
  <!-- Access Denied Dialog - Outside main container for proper z-index -->
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :requiredPermission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="users-app">
    <div class="px-4 py-6">
      <!-- Page Header -->
      <PageHeader 
        :title="$t('admin.users.pageTitle')" 
        :subtitle="$t('admin.users.pageSubtitle')"
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
            {{ t('admin.users.createUser') }}
          </button>
        </template>
      </PageHeader>

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
        :sort="sort"
        :can-update="canUpdate"
        :can-delete="canDelete"
        v-model:search="search"
        v-model:is-active-filter="isActiveFilter"
        @edit="handleEdit"
        @manage-roles="handleManageRoles"
        @manage-sessions="handleManageSessions"
        @view-login-history="handleViewLoginHistory"
        @deactivate="handleDeactivate"
        @delete="handleDelete"
        @reactivate="handleReactivate"
        @page-change="handlePageChange"
        @page-size-change="handlePageSizeChange"
        @sort-change="handleSortChange"
      />

      <UserForm
        v-if="showForm"
        :user="selectedUser"
        :policy="securityPolicy"
        :labels="{
          firstName: t('admin.users.firstName'),
          lastName: t('admin.users.lastName'),
          email: t('admin.users.email'),
          password: t('admin.users.password'),
          confirmPassword: t('admin.users.confirmPassword')
        }"
        :placeholders="{
          firstName: t('admin.users.firstName'),
          lastName: t('admin.users.lastName'),
          email: t('admin.users.email'),
          password: t('admin.users.password'),
          confirmPassword: t('admin.users.confirmPassword')
        }"
        :validationMessages="{
          required: t('validation.required'),
          email: t('validation.email')
        }"
        @close="handleFormClose"
        @save="handleFormSave"
      />

      <RoleAssignment
        v-if="showRoleDialog"
        :user="selectedUser"
        @close="handleRoleDialogClose"
        @save="handleRolesSaved"
      />

      <UserSessions
        v-if="showSessionsDialog"
        :user="selectedUser"
        :can-update="canUpdate"
        @close="handleSessionsClose"
      />

      <LoginHistoryDialog
        v-if="showLoginHistoryDialog"
        :user="selectedUser"
        :can-update="canUpdate"
        @close="handleLoginHistoryClose"
      />
    </div>
  </div>
</template>
