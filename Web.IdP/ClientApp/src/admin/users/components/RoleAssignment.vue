<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '../../../components/common/BaseModal.vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const { t } = useI18n()

const props = defineProps({
  user: { type: Object, required: true },
  canUpdate: { type: Boolean, default: false }
})

const emit = defineEmits(['close', 'save'])

const availableRoles = ref([])
const selectedRoles = ref([])
const loading = ref(true)
const saving = ref(false)
const error = ref('')

const fetchRoles = async () => {
  loading.value = true
  error.value = ''
  try {
    const response = await fetch('/api/admin/roles')
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    const data = await response.json()
    availableRoles.value = data.items || []
    
    // Pre-select user's current roles
    selectedRoles.value = props.user.roles || []
  } catch (e) {
    error.value = t('roles.assignment.errors.loadFailed')
    console.error('Error fetching roles:', e)
  } finally {
    loading.value = false
  }
}

const toggleRole = (roleName) => {
  const index = selectedRoles.value.indexOf(roleName)
  if (index > -1) {
    selectedRoles.value.splice(index, 1)
  } else {
    selectedRoles.value.push(roleName)
  }
}

const isSelected = (roleName) => {
  return selectedRoles.value.includes(roleName)
}

const handleSave = async () => {
  saving.value = true
  error.value = ''
  
  try {
    const response = await fetch(`/api/admin/users/${props.user.id}/roles`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        Roles: selectedRoles.value
      })
    })
    
    if (!response.ok) {
      const errorData = await response.json().catch(() => null)
      throw new Error(errorData?.message || `HTTP error! status: ${response.status}`)
    }
    
    emit('save')
  } catch (e) {
    error.value = t('roles.assignment.errors.updateFailed')
    console.error('Error updating roles:', e)
  } finally {
    saving.value = false
  }
}

const handleClose = () => {
  if (saving.value) return
  emit('close')
}

onMounted(() => {
  fetchRoles()
})
</script>

<template>
  <BaseModal
    :show="true"
    :title="t('roles.assignment.title')"
    size="md"
    :show-close-icon="true"
    :close-on-backdrop="false"
    :close-on-esc="true"
    :loading="saving"
    @close="handleClose"
  >
    <template #body>
      <div class="mb-4 p-3 bg-gray-50 rounded-md">
        <p class="text-sm text-gray-700">
          <span class="font-medium">{{ t('roles.assignment.userLabel') }}:</span> {{ user.email }}
        </p>
      </div>

      <!-- Error Alert -->
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <p class="text-sm text-red-700">{{ error }}</p>
      </div>

                <!-- Loading State -->
                <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('roles.assignment.loading')" />

                <!-- No Roles -->
                <div v-else-if="availableRoles.length === 0" class="bg-blue-50 border-l-4 border-blue-400 p-4">
                  <p class="text-sm text-blue-700">{{ t('roles.assignment.noRoles') }}</p>
                </div>

                <!-- Role List -->
                <div v-else>
                  <p class="text-sm font-medium text-gray-700 mb-3">{{ t('roles.assignment.selectRoles') }}:</p>
                  
                  <div class="max-h-[65vh] overflow-y-auto space-y-2 px-1">
                    <div
                      v-for="role in availableRoles"
                      :key="role.id"
                      class="relative flex items-start p-3 border rounded-md cursor-pointer transition-all"
                      :class="isSelected(role.name) ? 'bg-google-100 border-google-500' : 'bg-white border-gray-300 hover:bg-gray-50'"
                      @click="toggleRole(role.name)"
                    >
                      <div class="flex h-5 items-center">
                        <input
                          :id="`role-${role.id}`"
                          type="checkbox"
                          :checked="isSelected(role.name)"
                          @click.stop
                          @change="toggleRole(role.name)"
                          class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
                        />
                      </div>
                      <div class="ml-3 flex-1">
                        <label :for="`role-${role.id}`" class="font-medium text-gray-900 cursor-pointer">
                          {{ role.name }}
                        </label>
                        <p v-if="role.description" class="text-sm text-gray-500">
                          {{ role.description }}
                        </p>
                      </div>
                      <span
                        v-if="role.isSystem"
                        class="inline-flex items-center rounded-full bg-blue-100 px-2 py-1 text-xs font-medium text-blue-700"
                        :title="t('roles.assignment.systemRoleTooltip')"
                      >
                        {{ t('roles.assignment.systemRole') }}
                      </span>
                    </div>
                  </div>

        <div v-if="selectedRoles.length > 0" class="mt-3">
          <p class="text-xs text-gray-500">
            {{ t('roles.assignment.selectedRolesCount', { count: selectedRoles.length }) }}
          </p>
        </div>
      </div>
    </template>

    <template #footer>
      <button
        type="button"
        @click="handleSave"
        :disabled="saving || loading"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        {{ saving ? t('common.buttons.saving') : t('common.buttons.saveRoles') }}
      </button>
      <button
        type="button"
        @click="handleClose"
        :disabled="saving"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {{ t('common.buttons.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>
