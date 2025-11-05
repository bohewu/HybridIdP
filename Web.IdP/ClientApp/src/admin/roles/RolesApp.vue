<template>
  <!-- Access Denied Dialog -->
  <AccessDeniedDialog
    :show="showAccessDenied"
    :message="deniedMessage"
    :required-permission="deniedPermission"
    @close="showAccessDenied = false"
  />

  <div class="px-4 py-6">
    <div class="flex justify-between items-center mb-6">
      <h1 class="text-2xl font-bold text-gray-900">Role Management</h1>
      <div>
        <button
          v-if="canCreate"
          class="inline-flex items-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed" 
          @click="showCreateModal = true" 
          :disabled="loading"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
          </svg>
          Create Role
        </button>
      </div>
    </div>

    <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
      <p class="text-sm text-red-700">{{ error }}</p>
    </div>

    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
      <div class="p-4">
        <div class="flex justify-between items-center mb-4 gap-4">
          <input 
            v-model="search" 
            @keyup.enter="fetchRoles(0)" 
            type="text" 
            class="block w-64 rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm" 
            placeholder="Search name/description" 
          />
          <div class="flex gap-2">
            <select 
              v-model="sortBy" 
              class="block rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
            >
              <option value="name">Name</option>
              <option value="createdat">Created</option>
            </select>
            <select 
              v-model="sortDirection" 
              class="block rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
            >
              <option value="asc">Asc</option>
              <option value="desc">Desc</option>
            </select>
            <button 
              class="px-4 py-2 bg-white border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50" 
              @click="fetchRoles(0)" 
              :disabled="loading"
            >
              Apply
            </button>
          </div>
        </div>

        <div class="overflow-x-auto">
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
                <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Description</th>
                <th scope="col" class="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Permissions</th>
                <th scope="col" class="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Users</th>
                <th scope="col" class="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">System</th>
                <th scope="col" class="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody class="bg-white divide-y divide-gray-200">
              <tr v-if="loading">
                <td colspan="6" class="px-6 py-4">
                  <div class="text-center text-gray-500">Loading...</div>
                </td>
              </tr>
              <tr v-else-if="roles.length === 0">
                <td colspan="6" class="px-6 py-4">
                  <div class="text-center text-gray-500">No roles found</div>
                </td>
              </tr>
              <tr v-for="r in roles" :key="r.id" class="hover:bg-gray-50">
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{{ r.name }}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{{ r.description }}</td>
                <td class="px-6 py-4 whitespace-nowrap text-center">
                  <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                    {{ r.permissions?.length || 0 }}
                  </span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-center">
                  <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                    {{ r.userCount }}
                  </span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-center">
                  <span v-if="r.isSystem" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
                    System
                  </span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-center">
                  <div class="inline-flex gap-1">
                    <button
                      v-if="canUpdate"
                      class="inline-flex items-center px-3 py-1.5 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500" 
                      @click="onEdit(r)" 
                      title="Edit Role"
                    >
                      <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                      </svg>
                    </button>
                    <button
                      v-if="canDelete"
                      class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-sm font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500" 
                      @click="onDelete(r)" 
                      title="Delete Role"
                    >
                      <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </button>
                    <span v-if="!canUpdate && !canDelete" class="text-xs text-gray-400 italic">No actions</span>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="flex justify-between items-center mt-4 px-6">
          <div class="text-sm text-gray-700">Total: {{ totalCount }}</div>
          <div class="inline-flex gap-2">
            <button 
              class="px-4 py-2 bg-white border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed" 
              :disabled="skip === 0 || loading" 
              @click="prevPage"
            >
              Prev
            </button>
            <button 
              class="px-4 py-2 bg-white border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed" 
              :disabled="skip + take >= totalCount || loading" 
              @click="nextPage"
            >
              Next
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Create Role Modal -->
    <CreateRoleModal
      v-if="showCreateModal"
      @close="showCreateModal = false"
      @created="handleRoleCreated"
    />

    <!-- Edit Role Modal -->
    <EditRoleModal
      v-if="showEditModal && selectedRole"
      :role="selectedRole"
      @close="showEditModal = false"
      @updated="handleRoleUpdated"
    />

    <!-- Delete Role Modal -->
    <DeleteRoleModal
      v-if="showDeleteModal && selectedRole"
      :role="selectedRole"
      @close="showDeleteModal = false"
      @deleted="handleRoleDeleted"
    />
  </div>
  
</template>

<script setup>
import { onMounted, ref } from 'vue'
import CreateRoleModal from './components/CreateRoleModal.vue'
import EditRoleModal from './components/EditRoleModal.vue'
import DeleteRoleModal from './components/DeleteRoleModal.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

// Permission state
const canCreate = ref(false)
const canUpdate = ref(false)
const canDelete = ref(false)
const canRead = ref(false)

// Access denied dialog
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')

const roles = ref([])
const loading = ref(false)
const error = ref('')

const skip = ref(0)
const take = ref(25)
const totalCount = ref(0)

const search = ref('')
const sortBy = ref('name')
const sortDirection = ref('asc')

const showCreateModal = ref(false)
const showEditModal = ref(false)
const showDeleteModal = ref(false)
const selectedRole = ref(null)

async function fetchRoles(newSkip = skip.value) {
  loading.value = true
  error.value = ''
  try {
    const params = new URLSearchParams({
      skip: String(newSkip),
      take: String(take.value),
      search: search.value || '',
      sortBy: sortBy.value,
      sortDirection: sortDirection.value
    })
    const res = await fetch(`/api/Admin/roles?${params.toString()}`, {
      headers: { 'Accept': 'application/json' }
    })
    if (!res.ok) throw new Error(`Failed to load roles (${res.status})`)
    const data = await res.json()
    roles.value = data.items || []
    totalCount.value = data.totalCount || 0
    skip.value = data.skip || 0
    take.value = data.take || take.value
  } catch (e) {
    error.value = e?.message || 'Unknown error'
  } finally {
    loading.value = false
  }
}

function prevPage() { if (skip.value > 0) fetchRoles(Math.max(0, skip.value - take.value)) }
function nextPage() { if (skip.value + take.value < totalCount.value) fetchRoles(skip.value + take.value) }

function handleRoleCreated() {
  showCreateModal.value = false
  // Refresh the list and reset to first page
  fetchRoles(0)
}

function onEdit(role) {
  if (!canUpdate.value) {
    deniedMessage.value = 'You do not have permission to update roles.'
    deniedPermission.value = Permissions.Roles.UPDATE
    showAccessDenied.value = true
    return
  }
  selectedRole.value = role
  showEditModal.value = true
}

function handleRoleUpdated() {
  showEditModal.value = false
  selectedRole.value = null
  // Refresh the list keeping current page
  fetchRoles(skip.value)
}

function onDelete(role) {
  if (!canDelete.value) {
    deniedMessage.value = 'You do not have permission to delete roles.'
    deniedPermission.value = Permissions.Roles.DELETE
    showAccessDenied.value = true
    return
  }
  selectedRole.value = role
  showDeleteModal.value = true
}

function handleRoleDeleted() {
  showDeleteModal.value = false
  selectedRole.value = null
  // Refresh the list, go to first page if current page becomes empty
  const newTotal = totalCount.value - 1
  const maxSkip = Math.max(0, newTotal - 1)
  const newSkip = skip.value > maxSkip ? Math.max(0, skip.value - take.value) : skip.value
  fetchRoles(newSkip)
}

onMounted(async () => {
  // Load permissions
  await permissionService.loadPermissions()
  
  canRead.value = permissionService.hasPermission(Permissions.Roles.READ)
  canCreate.value = permissionService.hasPermission(Permissions.Roles.CREATE)
  canUpdate.value = permissionService.hasPermission(Permissions.Roles.UPDATE)
  canDelete.value = permissionService.hasPermission(Permissions.Roles.DELETE)
  
  if (!canRead.value) {
    deniedMessage.value = 'You do not have permission to view roles.'
    deniedPermission.value = Permissions.Roles.READ
    showAccessDenied.value = true
    return
  }
  
  fetchRoles(0)
})
</script>

<style scoped>
.card { border-radius: 10px; }
</style>
