<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

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
    error.value = 'Name is required and cannot be empty.'
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
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-2xl">
          <form @submit.prevent="handleSubmit" novalidate>
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    {{ $t(isEdit ? 'resources.form.editTitle' : 'resources.form.createTitle') }}
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

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
                      />
                      <p class="mt-1 text-xs text-gray-500">{{ $t('resources.form.baseUrlHelp') }}</p>
                    </div>

                    <!-- Associated Scopes -->
                    <div class="mb-5 border-t pt-4">
                      <h4 class="text-sm font-semibold text-gray-900 mb-3">
                        {{ $t('resources.form.scopesTitle') }}
                      </h4>
                      <p class="text-xs text-gray-500 mb-3">
                        {{ $t('resources.form.scopesHelp') }}
                      </p>

                      <div v-if="loadingScopes" class="text-sm text-gray-500">
                        {{ $t('resources.form.loadingScopes') }}
                      </div>

                      <div v-else-if="availableScopes.length === 0" class="text-sm text-gray-500">
                        {{ $t('resources.form.noScopesAvailable') }}
                      </div>

                      <div v-else class="max-h-64 overflow-y-auto border border-gray-200 rounded-md p-3 space-y-2">
                        <div
                          v-for="scope in availableScopes"
                          :key="scope.id"
                          class="flex items-start"
                        >
                          <div class="flex items-center h-5">
                            <input
                              :id="`scope-${scope.id}`"
                              type="checkbox"
                              :checked="isScopeSelected(scope.id)"
                              @change="toggleScope(scope.id)"
                              class="focus:ring-indigo-500 h-4 w-4 text-indigo-600 border-gray-300 rounded cursor-pointer"
                            />
                          </div>
                          <div class="ml-3 text-sm">
                            <label :for="`scope-${scope.id}`" class="font-medium text-gray-700 cursor-pointer">
                              {{ scope.name }}
                            </label>
                            <p v-if="scope.displayName" class="text-gray-500 text-xs">
                              {{ scope.displayName }}
                            </p>
                            <p v-if="scope.description" class="text-gray-400 text-xs mt-0.5">
                              {{ scope.description }}
                            </p>
                          </div>
                        </div>
                      </div>

                      <div v-if="selectedScopeIds.length > 0" class="mt-2 text-xs text-gray-500">
                        {{ $t('resources.form.scopesSelected', { count: selectedScopeIds.length }) }}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Modal Footer -->
            <div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6 gap-2">
              <button
                type="submit"
                :disabled="submitting"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
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
                class="mt-3 inline-flex w-full justify-center rounded-md bg-white px-4 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {{ $t('resources.form.cancelButton') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>
