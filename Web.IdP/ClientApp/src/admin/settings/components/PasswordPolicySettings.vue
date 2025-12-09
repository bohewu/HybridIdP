<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()
const props = defineProps({
  canUpdate: {
    type: Boolean,
    default: false
  }
})

const loading = ref(false)
const saving = ref(false)
const error = ref(null)
const successMessage = ref(null)

const policy = ref({
  minPasswordLength: 6,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireNonAlphanumeric: true,
  passwordHistoryCount: 0,
  passwordExpirationDays: 0,
  minPasswordAgeDays: 0,
  maxFailedAccessAttempts: 5,
  lockoutDurationMinutes: 15,
  abnormalLoginHistoryCount: 10,
  blockAbnormalLogin: false
})

const loadPolicy = async () => {
  loading.value = true
  error.value = null
  try {
    const response = await fetch('/api/admin/security/policies')
    if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`)
    }
    policy.value = await response.json()
  } catch (err) {
    console.error('Failed to load security policy:', err)
    error.value = t('settings.loadingError', { message: err.message })
  } finally {
    loading.value = false
  }
}

const savePolicy = async () => {
  saving.value = true
  error.value = null
  successMessage.value = null
  try {
    const response = await fetch('/api/admin/security/policies', {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(policy.value)
    })
    
    if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`HTTP error! status: ${response.status}, details: ${errorText}`)
    }

    successMessage.value = t('settings.saveSuccess')
    // Clear success message after 3 seconds
    setTimeout(() => {
      successMessage.value = null
    }, 3000)
  } catch (err) {
    console.error('Failed to save security policy:', err)
    error.value = t('settings.saveError', { message: err.message })
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (permissionService.hasPermission(Permissions.Settings.Read)) {
    loadPolicy()
  }
})
</script>

<template>
  <div class="bg-white shadow rounded-lg p-6" data-testid="security-policy-section">
    <div class="md:grid md:grid-cols-3 md:gap-6">
      <div class="md:col-span-1">
        <h3 class="text-lg font-medium leading-6 text-gray-900">{{ t('settings.security.title') }}</h3>
        <p class="mt-1 text-sm text-gray-500">{{ t('settings.security.description') }}</p>
      </div>
      <div class="mt-5 md:mt-0 md:col-span-2">
        <form @submit.prevent="savePolicy">
          <div class="grid grid-cols-6 gap-6">
            
            <!-- Password Complexity -->
            <div class="col-span-6 sm:col-span-3">
              <label for="minPasswordLength" class="block text-sm font-medium text-gray-700">{{ t('settings.security.minPasswordLength') }}</label>
              <input type="number" id="minPasswordLength" v-model.number="policy.minPasswordLength" min="1" max="128"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving" data-testid="min-password-length-input">
            </div>

            <div class="col-span-6 sm:col-span-3">
               <!-- Spacer or extra fields -->
            </div>

            <div class="col-span-6 sm:col-span-3">
                <div class="flex items-start">
                  <div class="flex h-5 items-center">
                    <input id="requireUppercase" v-model="policy.requireUppercase" type="checkbox" 
                           class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                           :disabled="!canUpdate || saving">
                  </div>
                  <div class="ml-3 text-sm">
                    <label for="requireUppercase" class="font-medium text-gray-700">{{ t('settings.security.requireUppercase') }}</label>
                  </div>
                </div>
            </div>

            <div class="col-span-6 sm:col-span-3">
                <div class="flex items-start">
                  <div class="flex h-5 items-center">
                    <input id="requireLowercase" v-model="policy.requireLowercase" type="checkbox" 
                           class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                           :disabled="!canUpdate || saving">
                  </div>
                  <div class="ml-3 text-sm">
                    <label for="requireLowercase" class="font-medium text-gray-700">{{ t('settings.security.requireLowercase') }}</label>
                  </div>
                </div>
            </div>

            <div class="col-span-6 sm:col-span-3">
                <div class="flex items-start">
                  <div class="flex h-5 items-center">
                    <input id="requireDigit" v-model="policy.requireDigit" type="checkbox" 
                           class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                           :disabled="!canUpdate || saving">
                  </div>
                  <div class="ml-3 text-sm">
                    <label for="requireDigit" class="font-medium text-gray-700">{{ t('settings.security.requireDigit') }}</label>
                  </div>
                </div>
            </div>

            <div class="col-span-6 sm:col-span-3">
                <div class="flex items-start">
                  <div class="flex h-5 items-center">
                    <input id="requireNonAlphanumeric" v-model="policy.requireNonAlphanumeric" type="checkbox" 
                           class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                           :disabled="!canUpdate || saving">
                  </div>
                  <div class="ml-3 text-sm">
                    <label for="requireNonAlphanumeric" class="font-medium text-gray-700">{{ t('settings.security.requireNonAlphanumeric') }}</label>
                  </div>
                </div>
            </div>
            
            <div class="col-span-6 border-t border-gray-200 my-2"></div>

            <!-- Password Expiration/History -->
            <div class="col-span-6 sm:col-span-3">
              <label for="passwordHistoryCount" class="block text-sm font-medium text-gray-700">{{ t('settings.security.passwordHistoryCount') }}</label>
              <input type="number" id="passwordHistoryCount" v-model.number="policy.passwordHistoryCount" min="0"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

            <div class="col-span-6 sm:col-span-3">
              <label for="passwordExpirationDays" class="block text-sm font-medium text-gray-700">{{ t('settings.security.passwordExpirationDays') }}</label>
              <input type="number" id="passwordExpirationDays" v-model.number="policy.passwordExpirationDays" min="0"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

             <div class="col-span-6 sm:col-span-3">
              <label for="minPasswordAgeDays" class="block text-sm font-medium text-gray-700">{{ t('settings.security.minPasswordAgeDays') }}</label>
              <input type="number" id="minPasswordAgeDays" v-model.number="policy.minPasswordAgeDays" min="0"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

            <div class="col-span-6 border-t border-gray-200 my-2"></div>

             <!-- Lockout -->
            <div class="col-span-6 sm:col-span-3">
              <label for="maxFailedAccessAttempts" class="block text-sm font-medium text-gray-700">{{ t('settings.security.maxFailedAccessAttempts') }}</label>
              <input type="number" id="maxFailedAccessAttempts" v-model.number="policy.maxFailedAccessAttempts" min="1"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

            <div class="col-span-6 sm:col-span-3">
              <label for="lockoutDurationMinutes" class="block text-sm font-medium text-gray-700">{{ t('settings.security.lockoutDurationMinutes') }}</label>
              <input type="number" id="lockoutDurationMinutes" v-model.number="policy.lockoutDurationMinutes" min="1"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

            <div class="col-span-6 border-t border-gray-200 my-2"></div>
            
            <!-- Abnormal Login -->
             <div class="col-span-6 sm:col-span-3">
                <div class="flex items-start">
                  <div class="flex h-5 items-center">
                    <input id="blockAbnormalLogin" v-model="policy.blockAbnormalLogin" type="checkbox" 
                           class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                           :disabled="!canUpdate || saving">
                  </div>
                  <div class="ml-3 text-sm">
                    <label for="blockAbnormalLogin" class="font-medium text-gray-700">{{ t('settings.security.blockAbnormalLogin') }}</label>
                  </div>
                </div>
            </div>
             <div class="col-span-6 sm:col-span-3">
              <label for="abnormalLoginHistoryCount" class="block text-sm font-medium text-gray-700">{{ t('settings.security.abnormalLoginHistoryCount') }}</label>
              <input type="number" id="abnormalLoginHistoryCount" v-model.number="policy.abnormalLoginHistoryCount" min="0" max="100"
                     class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                     :disabled="!canUpdate || saving">
            </div>

          </div>

          <!-- Messages -->
          <div v-if="error" class="mt-4 text-sm text-red-600">
            {{ error }}
          </div>
          <div v-if="successMessage" class="mt-4 text-sm text-green-600" data-testid="success-message">
            {{ successMessage }}
          </div>

          <div class="mt-4 text-right" v-if="canUpdate">
            <button type="submit" data-testid="save-policy-button"
                    class="inline-flex justify-center py-2 px-4 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                    :disabled="saving">
              {{ saving ? t('settings.saving') : t('settings.saveButton') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
