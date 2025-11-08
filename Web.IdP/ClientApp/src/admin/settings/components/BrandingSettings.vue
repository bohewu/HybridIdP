<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  canUpdate: {
    type: Boolean,
    default: false
  }
})

const { t } = useI18n()

const loading = ref(true)
const saving = ref(false)
const showSuccess = ref(false)
const error = ref(null)

// Form state
const appName = ref('')
const productName = ref('')

// Track original values for change detection
const originalAppName = ref('')
const originalProductName = ref('')

const hasChanges = computed(() => {
  return appName.value !== originalAppName.value || 
         productName.value !== originalProductName.value
})

// Load settings from API
const loadSettings = async () => {
  loading.value = true
  error.value = null
  try {
    const response = await fetch('/api/admin/settings?prefix=branding.', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json'
      },
      credentials: 'include'
    })

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }

    const settings = await response.json()
    
    // Extract values from settings array
    const appNameSetting = settings.find(s => s.key === 'branding.appName')
    const productNameSetting = settings.find(s => s.key === 'branding.productName')
    
    appName.value = appNameSetting?.value || ''
    productName.value = productNameSetting?.value || ''
    
    // Store originals
    originalAppName.value = appName.value
    originalProductName.value = productName.value
  } catch (err) {
    console.error('Failed to load branding settings:', err)
    error.value = t('admin.settings.loadingError', { message: err.message })
  } finally {
    loading.value = false
  }
}

// Save settings
const saveSettings = async () => {
  if (!hasChanges.value || !props.canUpdate) return

  saving.value = true
  error.value = null
  showSuccess.value = false

  try {
    // Update both settings
    const updates = []
    
    if (appName.value !== originalAppName.value) {
      updates.push(
        fetch('/api/admin/settings/branding.appName', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            value: appName.value,
            dataType: 'String'
          })
        })
      )
    }
    
    if (productName.value !== originalProductName.value) {
      updates.push(
        fetch('/api/admin/settings/branding.productName', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            value: productName.value,
            dataType: 'String'
          })
        })
      )
    }

    const results = await Promise.all(updates)
    
    // Check if all succeeded
    const allSucceeded = results.every(r => r.ok)
    if (!allSucceeded) {
      throw new Error('One or more settings failed to save')
    }

    // Invalidate cache to refresh everywhere
    await fetch('/api/admin/settings/invalidate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        keys: ['branding.appName', 'branding.productName']
      })
    })

    // Update originals
    originalAppName.value = appName.value
    originalProductName.value = productName.value

    showSuccess.value = true
    setTimeout(() => {
      showSuccess.value = false
    }, 3000)
  } catch (err) {
    console.error('Failed to save settings:', err)
    error.value = t('admin.settings.saveError', { message: err.message })
  } finally {
    saving.value = false
  }
}

// Cancel changes
const cancelChanges = () => {
  if (hasChanges.value) {
    if (!confirm(t('admin.settings.confirmCancel'))) {
      return
    }
  }
  appName.value = originalAppName.value
  productName.value = originalProductName.value
}

onMounted(() => {
  loadSettings()
})
</script>

<template>
  <div class="bg-white shadow-sm rounded-lg border border-gray-200">
    <!-- Section Header -->
    <div class="border-b border-gray-200 p-4">
      <h2 class="text-lg font-semibold text-gray-900">
        {{ t('admin.settings.brandingSection') }}
      </h2>
      <p class="mt-1 text-sm text-gray-500">
        {{ t('admin.settings.brandingSectionDesc') }}
      </p>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="p-6 flex justify-center items-center">
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      <span class="ml-3 text-gray-600">{{ t('admin.settings.loading') }}</span>
    </div>

    <!-- Form Content -->
    <div v-else class="p-4">
      <!-- Error Alert -->
      <div v-if="error" class="mb-4 bg-red-50 border border-red-200 rounded-lg p-3">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <p class="text-sm text-red-700">{{ error }}</p>
          </div>
        </div>
      </div>

      <!-- Success Alert -->
      <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 rounded-lg p-3">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <p class="text-sm text-green-700">{{ t('admin.settings.saveSuccess') }}</p>
          </div>
        </div>
      </div>

      <!-- Form Fields -->
      <div class="space-y-4">
        <!-- App Name -->
        <div>
          <label for="appName" class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('admin.settings.appName') }}
          </label>
          <input
            id="appName"
            v-model="appName"
            type="text"
            :disabled="!canUpdate"
            :placeholder="t('admin.settings.appNamePlaceholder')"
            class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed"
          />
          <p class="mt-1 text-xs text-gray-500">
            {{ t('admin.settings.appNameHelp') }}
          </p>
        </div>

        <!-- Product Name -->
        <div>
          <label for="productName" class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('admin.settings.productName') }}
          </label>
          <input
            id="productName"
            v-model="productName"
            type="text"
            :disabled="!canUpdate"
            :placeholder="t('admin.settings.productNamePlaceholder')"
            class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed"
          />
          <p class="mt-1 text-xs text-gray-500">
            {{ t('admin.settings.productNameHelp') }}
          </p>
        </div>
      </div>

      <!-- Action Buttons -->
      <div v-if="canUpdate" class="mt-6 flex items-center justify-end gap-3">
        <button
          type="button"
          :disabled="!hasChanges || saving"
          @click="cancelChanges"
          class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {{ t('admin.settings.cancelButton') }}
        </button>
        <button
          type="button"
          :disabled="!hasChanges || saving"
          @click="saveSettings"
          class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
        >
          <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          {{ saving ? t('admin.settings.saving') : t('admin.settings.saveButton') }}
        </button>
      </div>

      <!-- No Permission Message -->
      <div v-else class="mt-4 bg-yellow-50 border border-yellow-200 rounded-lg p-3">
        <p class="text-sm text-yellow-700">
          {{ t('deniedMessages.edit') }}
        </p>
      </div>
    </div>
  </div>
</template>
