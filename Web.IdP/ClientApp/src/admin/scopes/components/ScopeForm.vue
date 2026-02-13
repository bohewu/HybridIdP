<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import SearchableCheckboxList from '@/components/common/SearchableCheckboxList.vue'
import permissionService from '@/utils/permissionService'

const { t } = useI18n()

// Standard OIDC scopes with fixed standard claims
const STANDARD_OIDC_SCOPES = ['openid', 'profile', 'email', 'phone', 'address', 'offline_access']

const props = defineProps({
  scope: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['submit', 'cancel'])

const isEdit = computed(() => props.scope !== null)
const isStandardScope = computed(() => {
  if (!props.scope?.name) return false
  return STANDARD_OIDC_SCOPES.includes(props.scope.name.toLowerCase())
})
const isAdmin = ref(false)
const isStandardScopeReadOnly = computed(() => isEdit.value && isStandardScope.value && !isAdmin.value)
const formData = ref({
  name: '',
  displayName: '',
  description: '',
  resources: '', // Legacy string field, kept for compatibility if needed, but UI uses selectedResourceNames
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

const claimsWithLockState = computed(() => {
  if (!isStandardScope.value) {
    return availableClaims.value
  }

  return availableClaims.value.map(claim => ({
    ...claim,
    isLockedInStandardScope: claim.isStandard === true
  }))
})

const lockedClaimIds = computed(() => {
  if (!isStandardScope.value) {
    return []
  }

  const selected = new Set(selectedClaimIds.value)
  return claimsWithLockState.value
    .filter(claim => claim.isLockedInStandardScope && selected.has(claim.id))
    .map(claim => claim.id)
})

const selectedLockedClaimNames = computed(() => {
  if (!isStandardScope.value) {
    return []
  }

  const locked = new Set(lockedClaimIds.value)
  return claimsWithLockState.value
    .filter(claim => locked.has(claim.id))
    .map(claim => claim.name)
})

// Resources management
const availableResources = ref([])
const selectedResourceNames = ref([])
const loadingResources = ref(false)

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
  selectedResourceNames.value = []
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

// Fetch available resources
const fetchResources = async () => {
  loadingResources.value = true
  try {
    // Current UI doesn't support server-side search/pagination for resources
    // So we fetch a large number to likely cover all of them.
    // If you have > 1000 API resources, this UI needs a redesign anyway.
    const response = await fetch('/api/admin/resources?take=1000') 
    if (!response.ok) throw new Error('Failed to fetch resources')
    const data = await response.json()
    availableResources.value = data.items || []
  } catch (e) {
    console.error('Error fetching resources:', e)
  } finally {
    loadingResources.value = false
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
    
    // Populate selected resources
    selectedResourceNames.value = newScope.resources || []
    
    // Load claims for this scope
    if (newScope.id) {
      await fetchScopeClaims(newScope.id)
    }
  } else {
    resetForm()
  }
}, { immediate: true })

onMounted(async () => {
  await permissionService.loadPermissions()
  isAdmin.value = permissionService.isAdmin
  fetchClaims()
  fetchResources()
})

const handleSubmit = async () => {
  if (isStandardScopeReadOnly.value) {
    return
  }
  submitting.value = true
  error.value = null

  try {
    const payload = {
      name: formData.value.name,
      displayName: formData.value.displayName || null,
      description: formData.value.description || null,
      resources: selectedResourceNames.value,
      // Consent screen customization
      consentDisplayNameKey: formData.value.consentDisplayNameKey || null,
      consentDescriptionKey: formData.value.consentDescriptionKey || null,
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
        let errorMessage = `HTTP error! status: ${response.status}`
        try {
            const errorJson = JSON.parse(errorText)
            errorMessage = errorJson.message || errorJson.title || errorMessage
        } catch {
            // text content
            if (errorText && errorText.length < 200) {
                 errorMessage = errorText
            }
        }
        throw new Error(errorMessage)
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
    const claimIds = isStandardScope.value
      ? Array.from(new Set([...selectedClaimIds.value, ...lockedClaimIds.value]))
      : selectedClaimIds.value

    const response = await fetch(`/api/admin/scopes/${scopeId}/claims`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        claimIds
      })
    })

    if (!response.ok) {
      const errorText = await response.text()
      let errorMessage = `Failed to save scope claims (HTTP ${response.status})`
      try {
        const errorJson = JSON.parse(errorText)
        errorMessage = errorJson.message || errorJson.title || errorMessage
      } catch {
        if (errorText && errorText.length < 500) {
          errorMessage = errorText
        }
      }
      throw new Error(errorMessage)
    }
  } catch (e) {
    console.error('Error saving scope claims:', e)
    throw e
  }
}
</script>

<template>
  <BaseModal
    :show="true"
    :title="$t(isEdit ? 'scopes.form.editTitle' : 'scopes.form.createTitle')"
    size="lg"
    :loading="submitting"
    :close-on-backdrop="false"
    @close="$emit('cancel')"
  >
    <template #body>
      <!-- Error Alert -->
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <p class="text-sm text-red-700">{{ error }}</p>
      </div>

      <form id="scope-form" @submit.prevent="handleSubmit">
        <div v-if="isStandardScopeReadOnly" class="mb-4 bg-amber-50 border-l-4 border-amber-400 p-3">
          <div class="flex items-center">
            <svg class="h-5 w-5 text-amber-400 mr-2" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
            </svg>
            <p class="text-sm text-amber-700">
              {{ $t('scopes.form.standardScopeReadOnlyWarning') }}
            </p>
          </div>
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
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed"
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
              :disabled="isStandardScopeReadOnly"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
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
              :disabled="isStandardScopeReadOnly"
              class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm px-3 py-2"
              :placeholder="$t('scopes.form.descriptionPlaceholder')"
            ></textarea>
          </div>

          <!-- Resources (Multi-select) -->
          <div class="mb-5">
            <label class="block text-sm font-medium text-gray-700 mb-1.5">
              {{ $t('scopes.form.resources') }}
            </label>
            
            <SearchableCheckboxList
              v-model="selectedResourceNames"
              :items="availableResources"
              label-key="name"
              sub-label-key="displayName"
              value-key="name"
              :loading="loadingResources"
              :disabled="isStandardScopeReadOnly"
              placeholder="scopes.form.resourcesPlaceholder"
              height-class="max-h-48"
            />
            
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
                  :disabled="isStandardScopeReadOnly"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
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
                  :disabled="isStandardScopeReadOnly"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm px-3 py-2"
                  :placeholder="$t('scopes.form.consentDescriptionKeyPlaceholder')"
                ></textarea>
              <p class="mt-1 text-xs text-gray-500">{{ $t('scopes.form.consentDescriptionKeyHelp') }}</p>
            </div>

            <!-- Icon URL / Class -->
            <div class="mb-4">
                        <label for="iconUrl" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('scopes.form.iconUrl') }}
                        </label>
                        <!-- Common Icons Dropdown -->
                        <div class="flex space-x-2 mb-2">
                          <select
                            @change="e => { if (e.target.value) formData.iconUrl = e.target.value; e.target.value = '' }"
                            :disabled="isStandardScopeReadOnly"
                            class="block w-48 rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
                          >
                            <option value="">{{ $t('scopes.form.iconQuickSelect') }}</option>
                            <optgroup :label="$t('scopes.form.iconCategoryIdentity')">
                              <option value="bi bi-person-circle">👤 person-circle</option>
                              <option value="bi bi-envelope">📧 envelope (email)</option>
                              <option value="bi bi-shield-check">🛡️ shield-check</option>
                              <option value="bi bi-key">🔑 key</option>
                              <option value="bi bi-lock">🔒 lock</option>
                            </optgroup>
                            <optgroup :label="$t('scopes.form.iconCategoryData')">
                              <option value="bi bi-database">🗄️ database</option>
                              <option value="bi bi-folder">📁 folder</option>
                              <option value="bi bi-file-earmark-text">📄 file-text</option>
                              <option value="bi bi-cloud">☁️ cloud</option>
                            </optgroup>
                            <optgroup :label="$t('scopes.form.iconCategoryAccess')">
                              <option value="bi bi-gear">⚙️ gear (settings)</option>
                              <option value="bi bi-people">👥 people (group)</option>
                              <option value="bi bi-building">🏢 building (org)</option>
                              <option value="bi bi-globe">🌐 globe</option>
                            </optgroup>
                            <optgroup :label="$t('scopes.form.iconCategoryOther')">
                              <option value="bi bi-credit-card">💳 credit-card</option>
                              <option value="bi bi-cart">🛒 cart</option>
                              <option value="bi bi-calendar">📅 calendar</option>
                              <option value="bi bi-chat">💬 chat</option>
                              <option value="bi bi-bell">🔔 bell</option>
                            </optgroup>
                          </select>
                          <span class="text-xs text-gray-400 self-center">{{ $t('scopes.form.iconOrManual') }}</span>
                        </div>
                        <input
                          id="iconUrl"
                          v-model="formData.iconUrl"
                          type="text"
                          :disabled="isStandardScopeReadOnly"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
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
                          :disabled="isStandardScopeReadOnly"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
                        >
                          <option value="">{{ $t('scopes.form.categoryNone') }}</option>
                          <option value="Identity">{{ $t('scopes.form.categoryIdentity') }}</option>
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
                          :disabled="isStandardScopeReadOnly"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm h-10 px-3"
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
                            :disabled="isStandardScopeReadOnly"
                            class="mt-0.5 h-4 w-4 text-google-500 border-gray-300 rounded focus:ring-google-500"
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
            
            <!-- Standard Scope Warning -->
            <div v-if="isStandardScope" class="mb-3 bg-amber-50 border-l-4 border-amber-400 p-3">
              <div class="flex items-center">
                <svg class="h-5 w-5 text-amber-400 mr-2" viewBox="0 0 20 20" fill="currentColor">
                  <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
                </svg>
                <p class="text-sm text-amber-700">
                  {{ $t('scopes.form.standardScopeClaimsWarning') }}
                </p>
              </div>
              <p v-if="selectedLockedClaimNames.length > 0" class="mt-2 text-xs text-amber-700">
                {{ selectedLockedClaimNames.join(', ') }}
              </p>
            </div>
            
            <SearchableCheckboxList
              v-model="selectedClaimIds"
              :items="claimsWithLockState"
              label-key="name"
              sub-label-key="displayName"
              value-key="id"
              item-disabled-key="isLockedInStandardScope"
              :loading="loadingClaims"
              :disabled="isStandardScopeReadOnly"
              placeholder="scopes.form.userClaimsLoading"
              height-class="max-h-48"
            >
              <template #label-suffix="{ item }">
                 <span v-if="item.isStandard" class="ml-2 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] uppercase font-bold tracking-wider bg-blue-100 text-blue-800 border border-blue-200">
                    {{ $t('scopes.form.userClaimsStandardLabel') }}
                 </span>
              </template>
            </SearchableCheckboxList>
            
            <p class="mt-1 text-xs text-gray-500">
              {{ $t('scopes.form.userClaimsHelp') }}
            </p>
          </div>
        </div>
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        form="scope-form"
        :disabled="submitting || isStandardScopeReadOnly"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
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
    </template>
  </BaseModal>
</template>
