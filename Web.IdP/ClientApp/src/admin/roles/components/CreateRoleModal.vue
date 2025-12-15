<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '../../../components/common/BaseModal.vue'

const { t } = useI18n()

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

// Group permissions by category (labels are i18n keys)
const permissionGroups = ref({
  clients: { labelKey: 'clients', permissions: [] },
  scopes: { labelKey: 'scopes', permissions: [] },
  users: { labelKey: 'users', permissions: [] },
  roles: { labelKey: 'roles', permissions: [] },
  claims: { labelKey: 'claims', permissions: [] },
  persons: { labelKey: 'persons', permissions: [] },
  audit: { labelKey: 'audit', permissions: [] },
  monitoring: { labelKey: 'monitoring', permissions: [] },
  settings: { labelKey: 'settings', permissions: [] },
  localization: { labelKey: 'localization', permissions: [] }
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
    errors.value.name = t('roles.validation.nameRequired')
  } else if (form.value.name.length < 2) {
    errors.value.name = t('roles.validation.nameLength')
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
  <BaseModal
    :show="true"
    :title="$t('roles.createModal.title')"
    size="lg"
    :show-close-icon="true"
    :close-on-backdrop="false"
    :close-on-esc="true"
    :loading="saving"
    @close="handleClose"
  >
    <template #body>
      <form @submit.prevent="handleSubmit">
        <!-- Error Alert -->
        <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
          <p class="text-sm text-red-700">{{ error }}</p>
        </div>
        <div class="max-h-[80vh] overflow-y-auto px-1">
                    <!-- Role Name -->
                    <div class="mb-5">
                      <label for="name" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('roles.createModal.roleName') }} <span class="text-red-600">*</span>
                      </label>
                      <input
                          id="name"
                          v-model="form.name"
                          type="text"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                          :class="{ 'border-red-500': errors.name }"
                          required
                          :placeholder="$t('roles.createModal.roleNamePlaceholder')"
                      />
                      <p v-if="errors.name" class="mt-1.5 text-sm text-red-600">{{ errors.name }}</p>
                    </div>

                    <!-- Description -->
                    <div class="mb-5">
                      <label for="description" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('roles.createModal.description') }}
                      </label>
                      <textarea
                          id="description"
                          v-model="form.description"
                          rows="3"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors px-3 py-2"
                          :placeholder="$t('roles.createModal.descriptionPlaceholder')"
                      />
                    </div>

                    <!-- Permissions Selector -->
                    <div class="mb-5">
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('roles.createModal.permissions') }}
                      </label>
                      <div v-if="loadingPermissions" class="text-center py-4 text-gray-500">
                        {{ $t('roles.createModal.loadingPermissions') }}
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
                                  class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500 cursor-pointer"
                              />
                              <label :for="`category-${key}`" class="ml-3 text-sm font-semibold text-gray-900 cursor-pointer">
                                {{ $t(`permissions.groups.${group.labelKey}`) }}
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
                                    class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500 cursor-pointer"
                                />
                                <label :for="`perm-${perm}`" class="ml-3 text-sm text-gray-700 cursor-pointer">
                                  {{ $t(`permissions.items.${perm}`, perm) }}
                                </label>
                              </div>
                            </div>
                          </div>
                        </div>

                        <!-- Selected Count -->
                        <div class="mt-4 pt-3 border-t border-gray-200">
                          <p class="text-sm text-gray-600">
                            {{ $t('roles.createModal.selectedCount', { count: form.permissions.length }) }}
                          </p>
                        </div>
                      </div>
                    </div>
        </div>
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        @click="handleSubmit"
        :disabled="saving || loadingPermissions"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 disabled:bg-gray-400 disabled:cursor-not-allowed sm:ml-3 sm:w-auto"
      >
        {{ saving ? $t('roles.createModal.creating') : $t('roles.createModal.createButton') }}
      </button>
      <button
        type="button"
        @click="handleClose"
        :disabled="saving"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed sm:mt-0 sm:w-auto"
      >
        {{ $t('roles.createModal.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>

<style scoped>
/* Additional scoped styles if needed */
</style>
