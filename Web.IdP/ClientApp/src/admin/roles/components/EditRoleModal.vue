<script setup>
import { ref, onMounted } from 'vue'

const props = defineProps({
  role: { type: Object, required: true }
})

const emit = defineEmits(['close', 'updated'])

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

const initForm = () => {
  form.value = {
    name: props.role.name || '',
    description: props.role.description || '',
    permissions: [...(props.role.permissions || [])]
  }
  errors.value = {}
  error.value = ''
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
    
    const response = await fetch(`/api/admin/roles/${props.role.id}`, {
      method: 'PUT',
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
    
    emit('updated')
  } catch (e) {
    error.value = e.message || 'Failed to update role'
    console.error('Error updating role:', e)
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
  initForm()
  fetchAvailablePermissions()
})
</script>

<template>
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50" @click.self="handleClose">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full sm:max-w-2xl">
          <form @submit.prevent="handleSubmit">
            <div class="bg-white px-4 pt-5 sm:p-6">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    {{ $t('admin.roles.editModal.title') }}
                    <span v-if="role.isSystem" class="ml-2 inline-flex items-center rounded-md bg-yellow-50 px-2 py-1 text-xs font-medium text-yellow-800 ring-1 ring-inset ring-yellow-600/20">
                      {{ $t('admin.roles.editModal.systemRole') }}
                    </span>
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

                  <div class="max-h-[80vh] overflow-y-auto px-1">
                    <!-- System Role Info -->
                    <div v-if="role.isSystem" class="mb-4 bg-blue-50 border-l-4 border-blue-400 p-4">
                      <p class="text-sm text-blue-700">
                        <strong>{{ $t('admin.roles.editModal.note') }}</strong> {{ $t('admin.roles.editModal.systemRoleInfo') }}
                      </p>
                    </div>

                    <!-- Role Name -->
                    <div class="mb-5">
                      <label for="name" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('admin.roles.createModal.roleName') }} <span class="text-red-600">*</span>
                      </label>
                      <input
                          id="name"
                          v-model="form.name"
                          type="text"
                          :disabled="role.isSystem"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed transition-colors h-10 px-3"
                          :class="{ 'border-red-500': errors.name }"
                          required
                          :placeholder="$t('admin.roles.createModal.roleNamePlaceholder')"
                      />
                      <p v-if="errors.name" class="mt-1.5 text-sm text-red-600">{{ errors.name }}</p>
                      <p v-if="role.isSystem" class="mt-1.5 text-xs text-gray-500">{{ $t('admin.roles.editModal.systemRoleNameWarning') }}</p>
                    </div>

                    <!-- Description -->
                    <div class="mb-5">
                      <label for="description" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('admin.roles.createModal.description') }}
                      </label>
                      <textarea
                          id="description"
                          v-model="form.description"
                          rows="3"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors px-3 py-2"
                          :placeholder="$t('admin.roles.editModal.descriptionPlaceholder')"
                      />
                    </div>

                    <!-- Permissions Selector -->
                    <div class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('admin.roles.createModal.permissions') }}
                      </label>
                      <div v-if="loadingPermissions" class="text-center py-4 text-gray-500">
                        {{ $t('admin.roles.createModal.loadingPermissions') }}
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
                            {{ $t('admin.roles.createModal.selectedCount', { count: form.permissions.length }) }}
                          </p>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Footer Buttons -->
            <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                  type="submit"
                  :disabled="saving || loadingPermissions"
                  class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:bg-gray-400 disabled:cursor-not-allowed sm:ml-3 sm:w-auto"
              >
                {{ saving ? $t('admin.roles.editModal.updating') : $t('admin.roles.editModal.updateButton') }}
              </button>
              <button
                  type="button"
                  @click="handleClose"
                  :disabled="saving"
                  class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed sm:mt-0 sm:w-auto"
              >
                {{ $t('admin.roles.createModal.cancel') }}
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
