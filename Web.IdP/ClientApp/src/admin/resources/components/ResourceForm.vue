<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

const props = defineProps({
  resource: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['submit', 'cancel'])

const isEdit = computed(() => props.resource !== null)
const formData = ref({
  name: '',
  displayName: '',
  description: '',
  baseUrl: '',
  scopeIds: []
})

// Scopes management
const availableScopes = ref([])
const selectedScopeIds = ref([])
const loadingScopes = ref(false)

const submitting = ref(false)
const error = ref(null)

const nameInput = ref(null)

const resetForm = () => {
  formData.value = {
    name: '',
    displayName: '',
    description: '',
    baseUrl: '',
    scopeIds: []
  }
  selectedScopeIds.value = []
  error.value = null
}

// Fetch available scopes
const fetchScopes = async () => {
  loadingScopes.value = true
  try {
    const response = await fetch('/api/admin/scopes?page=1&pageSize=100')
    if (!response.ok) throw new Error('Failed to fetch scopes')
    const data = await response.json()
    availableScopes.value = data.items || []
  } catch (e) {
    console.error('Error fetching scopes:', e)
  } finally {
    loadingScopes.value = false
  }
}

// Fetch resource details when editing
const fetchResourceScopes = async (resourceId) => {
  try {
    const response = await fetch(`/api/admin/resources/${resourceId}`)
    if (!response.ok) throw new Error('Failed to fetch resource details')
    const data = await response.json()
    selectedScopeIds.value = data.scopes ? data.scopes.map(s => s.scopeId) : []
  } catch (e) {
    console.error('Error fetching resource scopes:', e)
  }
}

watch(() => props.resource, async (newResource) => {
  if (newResource) {
    formData.value = {
      name: newResource.name || '',
      displayName: newResource.displayName || '',
      description: newResource.description || '',
      baseUrl: newResource.baseUrl || '',
      scopeIds: []
    }
    // Load scopes for this resource
    if (newResource.id) {
      await fetchResourceScopes(newResource.id)
    }
  } else {
    resetForm()
  }
}, { immediate: true })

onMounted(() => {
  fetchScopes()
})

const handleSubmit = async () => {
  submitting.value = true
  error.value = null

  // Client-side validation: ensure required Name is present
  if (!formData.value.name || formData.value.name.trim() === '') {
    error.value = t('resources.form.validation.nameRequired')
    submitting.value = false
    // Focus the name input so the user sees validation
    // Use Vue template ref for focusing (avoids direct DOM access)
    if (nameInput.value && typeof nameInput.value.focus === 'function') {
      // nextTick optional but focus should work immediately here
      nameInput.value.focus()
    }
    return
  }

  try {
    const payload = {
      name: formData.value.name,
      displayName: formData.value.displayName || null,
      description: formData.value.description || null,
      baseUrl: formData.value.baseUrl || null,
      scopeIds: selectedScopeIds.value
    }

    const url = isEdit.value
      ? `/api/admin/resources/${props.resource.id}`
      : '/api/admin/resources'
    
    const method = isEdit.value ? 'PUT' : 'POST'

    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })

    if (!response.ok) {
      const errorText = await response.text()
      let errorMessage = `HTTP error! status: ${response.status}`
      try {
        const errorJson = JSON.parse(errorText)
        errorMessage = errorJson.message || errorJson.title || errorMessage
      } catch {
        // If not JSON, use the text as-is
        errorMessage = errorText || errorMessage
      }
      throw new Error(errorMessage)
    }

    emit('submit')
  } catch (e) {
    error.value = `Failed to save API resource: ${e.message}`
    console.error('Error saving resource:', e)
  } finally {
    submitting.value = false
  }
}

const toggleScope = (scopeId) => {
  const index = selectedScopeIds.value.indexOf(scopeId)
  if (index > -1) {
    selectedScopeIds.value.splice(index, 1)
  } else {
    selectedScopeIds.value.push(scopeId)
  }
}

const isScopeSelected = (scopeId) => {
  return selectedScopeIds.value.includes(scopeId)
}
</script>

<template>
  <BaseModal
    :show="true"
    :title="$t(isEdit ? 'resources.form.editTitle' : 'resources.form.createTitle')"
    size="2xl"
    :loading="submitting"
    :close-on-backdrop="false"
    @close="emit('cancel')"
  >
    <template #body>
      <!-- Error Alert -->
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <p class="text-sm text-red-700">{{ error }}</p>
      </div>

      <form @submit.prevent="handleSubmit" novalidate>
        <div class="space-y-5">
          <!-- Resource Name -->
          <div class="mb-5">
            <label for="name" class="block text-sm font-medium text-gray-700 mb-1.5">
              {{ $t('resources.form.name') }} <span class="text-red-500">*</span>
            </label>
            <input
              id="name"
              ref="nameInput"
              v-model="formData.name"
              type="text"
              :disabled="isEdit"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed"
              :placeholder="$t('resources.form.namePlaceholder')"
              data-test-id="resources-name-input"
            />
            <p class="mt-1 text-xs text-gray-500">{{ $t('resources.form.nameHelp') }}</p>
          </div>

          <!-- Display Name -->
          <div class="mb-5">
            <label for="displayName" class="block text-sm font-medium text-gray-700 mb-1.5">
              {{ $t('resources.form.displayName') }}
            </label>
            <input
              id="displayName"
              v-model="formData.displayName"
              type="text"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
              :placeholder="$t('resources.form.displayNamePlaceholder')"
              data-test-id="resources-display-name-input"
            />
          </div>

          <!-- Description -->
          <div class="mb-5">
            <label for="description" class="block text-sm font-medium text-gray-700 mb-1.5">
              {{ $t('resources.form.description') }}
            </label>
            <textarea
              id="description"
              v-model="formData.description"
              rows="3"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm px-3 py-2"
              :placeholder="$t('resources.form.descriptionPlaceholder')"
              data-test-id="resources-description-input"
            ></textarea>
          </div>

          <!-- Base URL -->
          <div class="mb-5">
            <label for="baseUrl" class="block text-sm font-medium text-gray-700 mb-1.5">
              {{ $t('resources.form.baseUrl') }}
            </label>
            <input
              id="baseUrl"
              v-model="formData.baseUrl"
              type="url"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
              :placeholder="$t('resources.form.baseUrlPlaceholder')"
              data-test-id="resources-base-url-input"
            />
            <p class="mt-1 text-xs text-gray-500">{{ $t('resources.form.baseUrlHelp') }}</p>
          </div>

        </div>
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        @click="handleSubmit"
        :disabled="submitting"
        class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
        data-test-id="resources-save-btn"
      >
        <svg v-if="submitting" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        {{ $t(isEdit ? 'resources.form.saveButton' : 'resources.form.createButton') }}
      </button>
      <button
        type="button"
        :disabled="submitting"
        @click="emit('cancel')"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
        data-test-id="resources-cancel-btn"
      >
        {{ $t('resources.form.cancelButton') }}
      </button>
    </template>
  </BaseModal>
</template>
