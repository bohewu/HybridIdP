<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  role: { type: Object, required: true }
})

const emit = defineEmits(['close', 'deleted'])

const deleting = ref(false)
const error = ref('')

const canDelete = computed(() => {
  // Cannot delete system roles
  return !props.role.isSystem
})

const handleDelete = async () => {
  if (!canDelete.value) {
    return
  }
  
  deleting.value = true
  error.value = ''
  
  try {
    const response = await fetch(`/api/admin/roles/${props.role.id}`, {
      method: 'DELETE'
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
    
    emit('deleted')
  } catch (e) {
    error.value = e.message || 'Failed to delete role'
    console.error('Error deleting role:', e)
  } finally {
    deleting.value = false
  }
}

const handleClose = () => {
  if (deleting.value) return
  emit('close')
}
</script>

<template>
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50" @click.self="handleClose">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full sm:max-w-lg max-h-[90vh] overflow-y-auto">
          <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
            <div class="sm:flex sm:items-start">
              <div class="mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full bg-red-100 sm:mx-0 sm:h-10 sm:w-10">
                <svg class="h-6 w-6 text-red-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
                </svg>
              </div>
              <div class="mt-3 text-center sm:ml-4 sm:mt-0 sm:text-left w-full">
                <h3 class="text-base font-semibold leading-6 text-gray-900">
                  {{ $t('admin.roles.deleteModal.title') }}
                </h3>
                <div class="mt-2">
                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

                  <!-- System Role Warning -->
                  <div v-if="role.isSystem" class="mb-4 bg-yellow-50 border-l-4 border-yellow-400 p-4">
                    <div class="flex">
                      <div class="flex-shrink-0">
                        <svg class="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
                          <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
                        </svg>
                      </div>
                      <div class="ml-3">
                        <p class="text-sm text-yellow-700">
                          <strong>{{ $t('admin.roles.deleteModal.systemRoleProtected') }}</strong> {{ $t('admin.roles.deleteModal.systemRoleInfo') }}
                        </p>
                      </div>
                    </div>
                  </div>

                  <!-- Users Assigned Warning -->
                  <div v-else-if="role.userCount > 0" class="mb-4 bg-orange-50 border-l-4 border-orange-400 p-4">
                    <div class="flex">
                      <div class="flex-shrink-0">
                        <svg class="h-5 w-5 text-orange-400" viewBox="0 0 20 20" fill="currentColor">
                          <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
                        </svg>
                      </div>
                      <div class="ml-3">
                        <p class="text-sm text-orange-700">
                          <strong>{{ $t('admin.roles.deleteModal.usersAssigned') }}</strong> {{ $t('admin.roles.deleteModal.usersAssignedInfo', { count: role.userCount }) }}
                        </p>
                        <p class="text-sm text-orange-700 mt-1">
                          {{ $t('admin.roles.deleteModal.reassignWarning') }}
                        </p>
                      </div>
                    </div>
                  </div>

                  <!-- Normal Delete Confirmation -->
                  <div v-else>
                    <p class="text-sm text-gray-500">
                      {{ $t('admin.roles.deleteModal.message', { name: role.name }) }}
                    </p>
                    <p class="text-sm text-gray-500 mt-2">
                      {{ $t('admin.roles.deleteModal.warning') }}
                    </p>
                    <div v-if="role.permissions && role.permissions.length > 0" class="mt-3 p-3 bg-gray-50 rounded">
                      <p class="text-xs text-gray-600 mb-1">{{ $t('admin.roles.deleteModal.roleHas') }}</p>
                      <ul class="text-xs text-gray-600 list-disc list-inside">
                        <li>{{ $t('admin.roles.deleteModal.permissionCount', { count: role.permissions.length }) }}</li>
                      </ul>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
            <button
              v-if="canDelete && role.userCount === 0"
              type="button"
              @click="handleDelete"
              :disabled="deleting"
              class="inline-flex w-full justify-center rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-red-500 disabled:bg-gray-400 disabled:cursor-not-allowed sm:ml-3 sm:w-auto"
            >
              {{ deleting ? $t('admin.roles.deleteModal.deleting') : $t('admin.roles.deleteModal.deleteButton') }}
            </button>
            <button
              type="button"
              @click="handleClose"
              :disabled="deleting"
              class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:cursor-not-allowed sm:mt-0 sm:w-auto"
            >
              {{ canDelete && role.userCount === 0 ? $t('admin.roles.createModal.cancel') : $t('admin.roles.deleteModal.close') }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Additional scoped styles if needed */
</style>
