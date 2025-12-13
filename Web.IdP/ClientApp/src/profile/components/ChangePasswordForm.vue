<template>
  <div class="bg-white shadow rounded-lg overflow-hidden mb-6" data-testid="change-password-form">
    <div class="border-b border-gray-200 px-4 py-5 sm:px-6">
      <h3 class="text-lg leading-6 font-medium text-gray-900">{{ t('profile.changePassword.title') }}</h3>
      <p class="mt-1 text-sm text-gray-500">{{ t('profile.changePassword.description') }}</p>
    </div>
    
    <div class="px-4 py-5 sm:p-6">
      <!-- Success Message -->
      <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded flex items-center gap-2">
        <i class="bi bi-check-circle-fill"></i>
        <span>{{ t('profile.changePassword.successMessage') }}</span>
      </div>

      <!-- Error Message -->
      <div v-if="errors.length > 0" class="mb-4 bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
        <div class="flex items-start gap-2">
           <i class="bi bi-exclamation-triangle-fill mt-0.5"></i>
           <ul class="list-disc list-inside">
             <li v-for="(error, index) in errors" :key="index">{{ error }}</li>
           </ul>
        </div>
      </div>

      <!-- External Login Info -->
      <div v-if="!props.hasLocalPassword && props.externalLogins.length > 0" class="bg-blue-50 border border-blue-200 text-blue-800 px-4 py-3 rounded">
        <div class="flex gap-2">
          <i class="bi bi-info-circle-fill mt-0.5"></i>
          <div>
            <p class="font-medium">{{ t('profile.changePassword.externalLoginTitle') }}</p>
            <p class="text-sm mt-1">{{ t('profile.changePassword.externalLoginMessage') }}</p>
            <div class="mt-2 flex gap-2">
              <span v-for="login in props.externalLogins" :key="login.loginProvider" 
                    class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                {{ login.providerDisplayName }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Policy Disabled Warning -->
      <div v-else-if="!props.allowPasswordChange" class="bg-gray-50 border border-gray-200 text-gray-600 px-4 py-3 rounded flex items-start gap-3">
        <i class="bi bi-lock-fill mt-0.5"></i>
        <p>{{ t('profile.changePassword.policyDisabled') }}</p>
      </div>

      <!-- Password Form -->
      <form v-else @submit.prevent="handleSubmit" class="space-y-6">
        <!-- Current Password -->
        <div>
          <label for="currentPassword" class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('profile.changePassword.currentPassword') }}
          </label>
          <div class="relative rounded-md shadow-sm">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
              <i class="bi bi-lock"></i>
            </div>
            <input 
              id="currentPassword"
              v-model="form.currentPassword"
              type="password"
              required
              class="block w-full pl-10 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              :placeholder="t('profile.changePassword.currentPasswordPlaceholder')"
            />
          </div>
        </div>

        <!-- New Password -->
        <div>
          <label for="newPassword" class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('profile.changePassword.newPassword') }}
          </label>
          <div class="relative rounded-md shadow-sm">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
              <i class="bi bi-key"></i>
            </div>
            <input 
              id="newPassword"
              v-model="form.newPassword"
              type="password"
              required
              class="block w-full pl-10 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              :placeholder="t('profile.changePassword.newPasswordPlaceholder')"
            />
          </div>
          
          <!-- Password Requirements Checklist -->
          <div v-if="policy && form.newPassword" class="mt-2 space-y-1">
            <div class="text-xs font-medium text-gray-600 mb-1">{{ t('profile.changePassword.requirements') }}</div>
            <div class="grid grid-cols-2 gap-1 text-xs">
              <div v-for="(req, index) in requirements" :key="index" :class="req.valid ? 'text-green-600' : 'text-gray-500'" class="flex items-center gap-1">
                <i :class="req.valid ? 'bi-check-circle-fill' : 'bi-circle'"></i>
                {{ req.text }}
              </div>
            </div>
          </div>
        </div>

        <!-- Confirm Password -->
        <div>
          <label for="confirmPassword" class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('profile.changePassword.confirmPassword') }}
          </label>
          <div class="relative rounded-md shadow-sm">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
              <i class="bi bi-check2-square"></i>
            </div>
            <input 
              id="confirmPassword"
              v-model="form.confirmPassword"
              type="password"
              required
              class="block w-full pl-10 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              :placeholder="t('profile.changePassword.confirmPasswordPlaceholder')"
            />
          </div>
          <!-- Password match indicator -->
          <div v-if="form.confirmPassword" class="mt-1 text-xs">
            <span v-if="passwordsMatch" class="text-green-600 flex items-center gap-1">
              <i class="bi bi-check-circle-fill"></i>
              {{ t('profile.changePassword.passwordsMatch') }}
            </span>
            <span v-else class="text-red-600 flex items-center gap-1">
              <i class="bi bi-x-circle-fill"></i>
              {{ t('profile.changePassword.passwordMismatch') }}
            </span>
          </div>
        </div>

        <!-- Actions -->
        <div class="flex justify-end pt-2 border-t border-gray-100 mt-4">
          <button 
            type="submit"
            :disabled="loading || !isFormValid"
            class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <i v-if="loading" class="bi bi-arrow-repeat animate-spin mr-2"></i>
            <i v-else class="bi bi-check-lg mr-2"></i>
            {{ loading ? t('profile.common.saving') : t('profile.changePassword.submit') }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, defineProps } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  allowPasswordChange: {
    type: Boolean,
    required: true
  },
  hasLocalPassword: {
    type: Boolean,
    required: true
  },
  externalLogins: {
    type: Array,
    default: () => []
  }
})

const { t } = useI18n()
const loading = ref(false)
const showSuccess = ref(false)
const errors = ref([])
const policy = ref(null)

const form = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
})

// Fetch security policy on mount
onMounted(async () => {
  try {
    const res = await fetch('/api/admin/security/policies', { credentials: 'include' })
    if (res.ok) {
      policy.value = await res.json()
    }
  } catch (e) {
    console.error('Failed to fetch security policy:', e)
  }
})

// Real-time password validation using composable
import { usePasswordValidation } from '@/composables/usePasswordValidation'
const { getPasswordRequirements } = usePasswordValidation()

const requirements = computed(() => getPasswordRequirements(form.value.newPassword, policy.value))

const allPasswordChecksPass = computed(() => {
  if (!requirements.value.length) return false
  return requirements.value.every(r => r.valid)
})

// Form is valid when all fields filled, password meets requirements, and passwords match
const isFormValid = computed(() => {
  return form.value.currentPassword && 
         form.value.newPassword && 
         form.value.confirmPassword &&
         passwordsMatch.value &&
         allPasswordChecksPass.value
})

const resetForm = () => {
  form.value = {
    currentPassword: '',
    newPassword: '',
    confirmPassword: ''
  }
  errors.value = []
  showSuccess.value = false
}

const handleSubmit = async () => {
  errors.value = []
  showSuccess.value = false

  // Client-side validation
  if (!passwordsMatch.value) {
    errors.value.push(t('profile.changePassword.passwordMismatch'))
    return
  }

  if (!allPasswordChecksPass.value) {
    errors.value.push(t('profile.changePassword.passwordRequirementsNotMet'))
    return
  }

  loading.value = true

  try {
    const res = await fetch('/api/profile/change-password', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      credentials: 'include',
      body: JSON.stringify(form.value)
    })

    if (res.ok) {
      showSuccess.value = true
      resetForm()
      
      // Hide success message after 5 seconds
      setTimeout(() => {
        showSuccess.value = false
      }, 5000)
    } else if (res.status === 403) {
      errors.value.push(t('profile.changePassword.policyDisabled'))
    } else {
      const data = await res.json()
      if (data.errors && Array.isArray(data.errors)) {
        errors.value = data.errors.map(e => e.description || e.toString())
      } else if (data.error) {
        errors.value.push(data.error)
      } else {
        errors.value.push(t('profile.common.errorOccurred'))
      }
    }
  } catch (err) {
    errors.value.push(t('profile.common.networkError'))
    console.error('Failed to change password:', err)
  } finally {
    loading.value = false
  }
}
</script>
