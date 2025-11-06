<script setup>
import { ref, onMounted } from 'vue'

const emit = defineEmits(['close', 'created'])

const form = ref({
  name: '',
  description: '',
  permissions: []
})

const errors = ref({})
const saving = ref(false)
const error = ref('')
const availablePermissions = ref([])
const loadingPermissions = ref(false)

// Group permissions by category
const permissionGroups = ref({
  clients: { label: 'Clients', permissions: [] },
  scopes: { label: 'Scopes', permissions: [] },
  users: { label: 'Users', permissions: [] },
  roles: { label: 'Roles', permissions: [] },
  audit: { label: 'Audit', permissions: [] },
  settings: { label: 'Settings', permissions: [] }
})

const fetchAvailablePermissions = async () => {
  loadingPermissions.value = true
  try {
    const res = await fetch('/api/Admin/roles/permissions', {
      headers: { 'Accept': 'application/json' }
    })
    if (!res.ok) throw new Error(`Failed to load permissions (${res.status})`)
    const perms = await res.json()
    availablePermissions.value = perms || []
    
    // Group permissions by category
    perms.forEach(perm => {
      const [category] = perm.split('.')
      if (permissionGroups.value[category]) {
        permissionGroups.value[category].permissions.push(perm)
      }
    })
  } catch (e) {
    error.value = e?.message || 'Failed to load permissions'
  } finally {
    loadingPermissions.value = false
  }
}

const validate = () => {
  errors.value = {}
  
  if (!form.value.name) {
    errors.value.name = 'Role name is required'
  } else if (form.value.name.length < 2) {
    errors.value.name = 'Role name must be at least 2 characters'
  }
  
  return Object.keys(errors.value).length === 0
}

const handleSubmit = async () => {
  if (!validate()) {
    return
  }
  
  saving.value = true
  error.value = ''
  
  try {
    const payload = {
      name: form.value.name,
      description: form.value.description || null,
      permissions: form.value.permissions
    }
    
    const response = await fetch('/api/admin/roles', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })
    
    if (!response.ok) {
      const errorData = await response.json().catch(() => null)
      if (errorData?.errors) {
        // Handle validation errors from backend
        if (Array.isArray(errorData.errors)) {
          error.value = errorData.errors.join(', ')
        } else {
          error.value = errorData.errors
        }
      } else {
        error.value = errorData?.message || `HTTP error! status: ${response.status}`
      }
      return
    }
    
    emit('created')
  } catch (e) {
    error.value = e.message || 'Failed to create role'
    console.error('Error creating role:', e)
  } finally {
    saving.value = false
  }
}

const handleClose = () => {
  if (saving.value) return
  emit('close')
}

const togglePermission = (permission) => {
  const index = form.value.permissions.indexOf(permission)
  if (index > -1) {
    form.value.permissions.splice(index, 1)
  } else {
    form.value.permissions.push(permission)
  }
}

const toggleAllInCategory = (category) => {
  const categoryPerms = permissionGroups.value[category].permissions
  const allSelected = categoryPerms.every(p => form.value.permissions.includes(p))
  
  if (allSelected) {
    // Deselect all in this category
    form.value.permissions = form.value.permissions.filter(p => !categoryPerms.includes(p))
  } else {
    // Select all in this category
    categoryPerms.forEach(p => {
      if (!form.value.permissions.includes(p)) {
        form.value.permissions.push(p)
      }
    })
  }
}

const isCategoryFullySelected = (category) => {
  const categoryPerms = permissionGroups.value[category].permissions
  return categoryPerms.length > 0 && categoryPerms.every(p => form.value.permissions.includes(p))
}

const isCategoryPartiallySelected = (category) => {
  const categoryPerms = permissionGroups.value[category].permissions
  return categoryPerms.some(p => form.value.permissions.includes(p)) && !isCategoryFullySelected(category)
}

onMounted(() => {
  fetchAvailablePermissions()
})
</script>

<template>
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50" @click.self="handleClose">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full sm:max-w-2xl max-h-[90vh] overflow-y-auto">
          <form @submit.prevent="handleSubmit">
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    Create New Role
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

                  <!-- Role Name -->
                  <div class="mb-5">
                    <label for="name" class="block text-sm font-medium text-gray-700 mb-1.5">
                      Role Name <span class="text-red-600">*</span>
                    </label>
                    <input
                      id="name"
                      v-model="form.name"
                      type="text"
                      class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      :class="{ 'border-red-500': errors.name }"
                      required
                      placeholder="e.g., Content Editor"
                    />
                    <p v-if="errors.name" class="mt-1.5 text-sm text-red-600">{{ errors.name }}</p>
                  </div>

                  <!-- Description -->
                  <div class="mb-5">
                    <label for="description" class="block text-sm font-medium text-gray-700 mb-1.5">
                      Description
                    </label>
                    <textarea
                      id="description"
                      v-model="form.description"
                      rows="3"
                      class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors px-3 py-2"
                      placeholder="Optional description of the role"
                    />
                  </div>

                  <!-- Permissions Selector -->
                  <div class="mb-5">
                    <label class="block text-sm font-medium text-gray-700 mb-2">
                      Permissions
                    </label>
                    <div v-if="loadingPermissions" class="text-center py-4 text-gray-500">
                      Loading permissions...
                    </div>
                    <div v-else class="border border-gray-300 rounded-md p-4 max-h-96 overflow-y-auto bg-gray-50">
                      <div v-for="(group, key) in permissionGroups" :key="key" class="mb-4 last:mb-0">
                        <div v-if="group.permissions.length > 0" class="mb-2">
                          <!-- Category Header -->
                          <div class="flex items-center mb-3 pb-2 border-b border-gray-200">
                            <input
                              :id="`category-${key}`"
                              type="checkbox"
                              :checked="isCategoryFullySelected(key)"
                              :indeterminate.prop="isCategoryPartiallySelected(key)"
                              @change="toggleAllInCategory(key)"
                              class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 cursor-pointer"
                            />
                            <label :for="`category-${key}`" class="ml-3 text-sm font-semibold text-gray-900 cursor-pointer">
                              {{ group.label }}
                            </label>
                          </div>
                          
                          <!-- Individual Permissions -->
                          <div class="ml-6 space-y-2.5">
                            <div v-for="perm in group.permissions" :key="perm" class="flex items-center">
                              <input
                                :id="`perm-${perm}`"
                                type="checkbox"
                                :checked="form.permissions.includes(perm)"
                                @change="togglePermission(perm)"
                                class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 cursor-pointer"
                              />
                              <label :for="`perm-${perm}`" class="ml-3 text-sm text-gray-700 cursor-pointer font-mono">
                                {{ perm }}
                              </label>
                            </div>
                          </div>
                        </div>
                      </div>
                      
                      <!-- Selected Count -->
                      <div class="mt-4 pt-3 border-t border-gray-200">
                        <p class="text-sm text-gray-600">
                          Selected: <span class="font-semibold">{{ form.permissions.length }}</span> permission(s)
                        </p>
                      </div>
                    </div>
                  </div>

                </div>
              </div>
            </div>

            <!-- Footer Buttons -->
            <div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                type="submit"
                :disabled="saving || loadingPermissions"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:bg-gray-400 disabled:cursor-not-allowed sm:ml-3 sm:w-auto"
              >
                {{ saving ? 'Creating...' : 'Create Role' }}
              </button>
              <button
                type="button"
                @click="handleClose"
                :disabled="saving"
                class="mt-3 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed sm:mt-0 sm:w-auto"
              >
                Cancel
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Additional scoped styles if needed */
</style>
