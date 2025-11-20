<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  scope: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['submit', 'cancel'])

const isEdit = computed(() => props.scope !== null)
const formData = ref({
  name: '',
  displayName: '',
  description: '',
  resources: '',
  // Consent screen customization
  consentDisplayNameKey: '',
  consentDescriptionKey: '',
  iconUrl: '',
  isRequired: false,
  displayOrder: 0,
  category: ''
})

// Claims management
const availableClaims = ref([])
const selectedClaimIds = ref([])
const loadingClaims = ref(false)

const submitting = ref(false)
const error = ref(null)

const resetForm = () => {
  formData.value = {
    name: '',
    displayName: '',
    description: '',
    resources: '',
    consentDisplayNameKey: '',
    consentDescriptionKey: '',
    iconUrl: '',
    isRequired: false,
    displayOrder: 0,
    category: ''
  }
  selectedClaimIds.value = []
  error.value = null
}

// Fetch available claims
const fetchClaims = async () => {
  loadingClaims.value = true
  try {
    const response = await fetch('/api/admin/claims')
    if (!response.ok) throw new Error('Failed to fetch claims')
    const data = await response.json()
    availableClaims.value = data.items || []
  } catch (e) {
    console.error('Error fetching claims:', e)
  } finally {
    loadingClaims.value = false
  }
}

// Fetch scope claims when editing
const fetchScopeClaims = async (scopeId) => {
  try {
    const response = await fetch(`/api/admin/scopes/${scopeId}/claims`)
    if (!response.ok) throw new Error('Failed to fetch scope claims')
    const data = await response.json()
    selectedClaimIds.value = data.claims ? data.claims.map(sc => sc.claimId) : []
  } catch (e) {
    console.error('Error fetching scope claims:', e)
  }
}

watch(() => props.scope, async (newScope) => {
  if (newScope) {
    formData.value = {
      name: newScope.name || '',
      displayName: newScope.displayName || '',
      description: newScope.description || '',
      resources: newScope.resources?.join('\n') || '',
      consentDisplayNameKey: newScope.consentDisplayNameKey || '',
      consentDescriptionKey: newScope.consentDescriptionKey || '',
      iconUrl: newScope.iconUrl || '',
      isRequired: newScope.isRequired || false,
      displayOrder: newScope.displayOrder || 0,
      category: newScope.category || ''
    }
    // Load claims for this scope
    if (newScope.id) {
      await fetchScopeClaims(newScope.id)
    }
  } else {
    resetForm()
  }
}, { immediate: true })

onMounted(() => {
  fetchClaims()
})

const handleSubmit = async () => {
  submitting.value = true
  error.value = null

  try {
    const payload = {
      name: formData.value.name,
      displayName: formData.value.displayName || null,
      description: formData.value.description || null,
      resources: formData.value.resources
        .split('\n')
        .map(r => r.trim())
        .filter(r => r.length > 0),
      // Consent screen customization
      consentDisplayName: formData.value.consentDisplayName || null,
      consentDescription: formData.value.consentDescription || null,
      iconUrl: formData.value.iconUrl || null,
      isRequired: formData.value.isRequired,
      displayOrder: formData.value.displayOrder,
      category: formData.value.category || null
    }

    const url = isEdit.value
      ? `/api/admin/scopes/${encodeURIComponent(props.scope.id)}`
      : '/api/admin/scopes'
    
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
      throw new Error(`HTTP error! status: ${response.status}, ${errorText}`)
    }

    const savedScope = await response.json()

    // Save claims mapping
    if (savedScope.id) {
      await saveScopeClaims(savedScope.id)
    }

    emit('submit')
  } catch (e) {
    error.value = `Failed to save scope: ${e.message}`
    console.error('Error saving scope:', e)
  } finally {
    submitting.value = false
  }
}

// Save scope claims
const saveScopeClaims = async (scopeId) => {
  try {
    const response = await fetch(`/api/admin/scopes/${scopeId}/claims`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        claimIds: selectedClaimIds.value
      })
    })

    if (!response.ok) {
      throw new Error('Failed to save scope claims')
    }
  } catch (e) {
    console.error('Error saving scope claims:', e)
    throw e
  }
}
</script>

<template>
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-lg">
          <form @submit.prevent="handleSubmit">
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    {{ $t(isEdit ? 'scopes.form.editTitle' : 'scopes.form.createTitle') }}
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

                  <div class="space-y-5">
                    <!-- Scope Name -->
                    <div class="mb-5">
                      <label for="name" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('scopes.form.name') }} <span class="text-red-500">*</span>
                      </label>
                      <input
                        id="name"
                        v-model="formData.name"
                        type="text"
                        required
                        :disabled="isEdit"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed"
                        :placeholder="$t('scopes.form.namePlaceholder')"
                      />
                      <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.nameHelp') }}</p>
                    </div>

                    <!-- Display Name -->
                    <div class="mb-5">
                      <label for="displayName" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('scopes.form.displayName') }}
                      </label>
                      <input
                        id="displayName"
                        v-model="formData.displayName"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                        :placeholder="$t('scopes.form.displayNamePlaceholder')"
                      />
                    </div>

                    <!-- Description -->
                    <div class="mb-5">
                      <label for="description" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('scopes.form.description') }}
                      </label>
                      <textarea
                        id="description"
                        v-model="formData.description"
                        rows="3"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm px-3 py-2"
                        :placeholder="$t('scopes.form.descriptionPlaceholder')"
                      ></textarea>
                    </div>

                    <!-- Resources -->
                    <div class="mb-5">
                      <label for="resources" class="block text-sm font-medium text-gray-700 mb-1.5">
                        {{ $t('scopes.form.resources') }}
                      </label>
                      <textarea
                        id="resources"
                        v-model="formData.resources"
                        rows="2"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm px-3 py-2"
                        :placeholder="$t('scopes.form.resourcesPlaceholder')"
                      ></textarea>
                      <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.resourcesHelp') }}</p>
                    </div>

                    <!-- Consent Screen Customization Section -->
                    <div class="mb-5 border-t pt-4">
                      <h4 class="text-sm font-semibold text-gray-900 mb-3">
                        {{ $t('scopes.form.consentSectionTitle') }}
                      </h4>
                      <p class="text-xs text-gray-500 mb-3">
                        {{ $t('scopes.form.consentSectionHelp') }}
                      </p>

                      <!-- Consent Display Name -->
                      <div class="mb-4">
                        <label for="consentDisplayNameKey" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.consentDisplayNameKey') }}
                        </label>
                        <input
                          id="consentDisplayNameKey"
                          v-model="formData.consentDisplayNameKey"
                          type="text"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                          :placeholder="$t('scopes.form.consentDisplayNameKeyPlaceholder')"
                        />
                        <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.consentDisplayNameKeyHelp') }}</p>
                      </div>

                      <!-- Consent Description -->
                      <div class="mb-4">
                        <label for="consentDescriptionKey" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.consentDescriptionKey') }}
                        </label>
                        <textarea
                          id="consentDescriptionKey"
                          v-model="formData.consentDescriptionKey"
                          rows="3"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm px-3 py-2"
                          :placeholder="$t('scopes.form.consentDescriptionKeyPlaceholder')"
                        ></textarea>
                        <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.consentDescriptionKeyHelp') }}</p>
                      </div>

                      <!-- Icon URL / Class -->
                      <div class="mb-4">
                        <label for="iconUrl" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.iconUrl') }}
                        </label>
                        <input
                          id="iconUrl"
                          v-model="formData.iconUrl"
                          type="text"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                          :placeholder="$t('scopes.form.iconUrlPlaceholder')"
                        />
                        <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.iconUrlHelp') }}</p>
                        <!-- Icon Preview -->
                        <div v-if="formData.iconUrl" class="mt-2 flex items-center space-x-2">
                          <span class="text-sm text-gray-600">{{ $t('scopes.form.iconPreview') }}:</span>
                          <i v-if="formData.iconUrl.startsWith('bi')" :class="formData.iconUrl" class="text-lg"></i>
                          <img v-else :src="formData.iconUrl" :alt="$t('scopes.form.iconPreviewAlt')" class="w-6 h-6 object-contain" />
                        </div>
                      </div>

                      <!-- Category -->
                      <div class="mb-4">
                        <label for="category" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.category') }}
                        </label>
                        <select
                          id="category"
                          v-model="formData.category"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                        >
                          <option value="">{{ $t('scopes.form.categoryNone') }}</option>
                          <option value="Profile">{{ $t('scopes.form.categoryProfile') }}</option>
                          <option value="API Access">{{ $t('scopes.form.categoryApiAccess') }}</option>
                          <option value="Custom">{{ $t('scopes.form.categoryCustom') }}</option>
                        </select>
                        <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.categoryHelp') }}</p>
                      </div>

                      <!-- Display Order -->
                      <div class="mb-4">
                        <label for="displayOrder" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.displayOrder') }}
                        </label>
                        <input
                          id="displayOrder"
                          v-model.number="formData.displayOrder"
                          type="number"
                          min="0"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
                          :placeholder="$t('scopes.form.displayOrderPlaceholder')"
                        />
                        <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.displayOrderHelp') }}</p>
                      </div>

                      <!-- Is Required -->
                      <div class="mb-4">
                        <label class="flex items-start cursor-pointer">
                          <input
                            id="isRequired"
                            v-model="formData.isRequired"
                            type="checkbox"
                            class="mt-0.5 h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                          />
                          <div class="ml-3">
                            <span class="block text-sm font-medium text-gray-700">
                              {{ $t('scopes.form.isRequired') }}
                            </span>
                            <p class="text-xs text-gray-500">{{ $t('scopes.form.isRequiredHelp') }}</p>
                          </div>
                        </label>
                      </div>
                    </div>

                    <!-- Claims -->
                    <div>
                      <label class="block text-sm font-medium text-gray-700 mb-2">
                        {{ $t('scopes.form.userClaimsTitle') }}
                      </label>
                      <div v-if="loadingClaims" class="text-sm text-gray-500">
                        {{ $t('scopes.form.userClaimsLoading') }}
                      </div>
                      <div v-else class="border border-gray-300 rounded-md max-h-48 overflow-y-auto p-2 space-y-1">
                        <div v-if="availableClaims.length === 0" class="text-sm text-gray-500 p-2">
                          {{ $t('scopes.form.userClaimsNone') }}
                        </div>
                        <label
                          v-for="claim in availableClaims"
                          :key="claim.id"
                          class="flex items-start p-2 hover:bg-gray-50 rounded cursor-pointer"
                        >
                          <input
                            type="checkbox"
                            :value="claim.id"
                            v-model="selectedClaimIds"
                            class="mt-0.5 h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                          />
                          <div class="ml-3 flex-1">
                            <div class="text-sm font-medium text-gray-900">
                              {{ claim.name }}
                              <span v-if="claim.isStandard" class="ml-2 inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                                {{ $t('scopes.form.userClaimsStandardLabel') }}
                              </span>
                            </div>
                            <div class="text-xs text-gray-500">{{ claim.displayName }}</div>
                          </div>
                        </label>
                      </div>
                      <p class="mt-1 text-xs text-gray-500">
                        {{ $t('scopes.form.userClaimsHelp') }}
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                type="submit"
                :disabled="submitting"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <svg v-if="submitting" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                {{ $t(submitting ? 'scopes.form.saving' : (isEdit ? 'scopes.form.updateButton' : 'scopes.form.createButton')) }}
              </button>
              <button
                type="button"
                @click="$emit('cancel')"
                :disabled="submitting"
                class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {{ $t('scopes.form.cancel') }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>
