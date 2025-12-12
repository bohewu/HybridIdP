<template>
  <!-- Show password change form only if allowed AND has local password -->
  <div v-if="props.allowPasswordChange && props.hasLocalPassword" class="bg-white shadow rounded-lg p-6 mb-6">
    <h2 class="text-xl font-semibold text-gray-900 mb-4">
      {{ t('profile.changePassword.title') }}
    </h2>

    <!-- Info Note: Account-specific password change -->
    <div class="mb-4 bg-blue-50 border border-blue-200 text-blue-700 px-4 py-3 rounded text-sm">
      <div class="flex gap-2">
        <svg class="w-5 h-5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd"/>
        </svg>
        <span>{{ t('profile.changePassword.accountSpecificNote') }}</span>
      </div>
    </div>

    <!-- Success Message -->
    <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded">
      <div class="flex items-center gap-2">
        <svg class="w-5 h-5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
        </svg>
        <span>{{ t('profile.changePassword.success') }}</span>
      </div>
    </div>

    <!-- Validation Errors -->
    <div v-if="errors.length > 0" class="mb-4 bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
      <ul class="list-disc list-inside space-y-1">
        <li v-for="(error, index) in errors" :key="index" class="text-sm">{{ error }}</li>
      </ul>
    </div>

    <form @submit.prevent="handleSubmit" class="space-y-4">
      <!-- Current Password -->
      <div>
        <label class="block text-sm font-medium text-gray-700 mb-1">
          {{ t('profile.changePassword.currentPassword') }}
        </label>
        <input 
          v-model="form.currentPassword"
          type="password"
          required
          class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          :placeholder="t('profile.changePassword.currentPasswordPlaceholder')"
        />
      </div>

      <!-- New Password -->
      <div>
        <label class="block text-sm font-medium text-gray-700 mb-1">
          {{ t('profile.changePassword.newPassword') }}
        </label>
        <input 
          v-model="form.newPassword"
          type="password"
          required
          minlength="8"
          class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          :placeholder="t('profile.changePassword.newPasswordPlaceholder')"
        />
        <p class="mt-1 text-xs text-gray-500">{{ t('profile.changePassword.passwordRequirements') }}</p>
      </div>

      <!-- Confirm Password -->
      <div>
        <label class="block text-sm font-medium text-gray-700 mb-1">
          {{ t('profile.changePassword.confirmPassword') }}
        </label>
        <input 
          v-model="form.confirmPassword"
          type="password"
          required
          class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          :placeholder="t('profile.changePassword.confirmPasswordPlaceholder')"
        />
      </div>

      <!-- Submit Button -->
      <div class="flex justify-end gap-3">
        <button 
          type="button"
          @click="resetForm"
          class="px-4 py-2 border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50"
        >
          {{ t('common.cancel') }}
        </button>
        <button 
          type="submit"
          :disabled="loading"
          class="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {{ loading ? t('common.saving') : t('common.save') }}
        </button>
      </div>
    </form>
  </div>

  <!-- Show info message if external login (no local password) -->
  <div v-else-if="!props.hasLocalPassword && props.externalLogins.length > 0" 
       class="bg-blue-50 border border-blue-200 text-blue-700 px-4 py-3 rounded mb-6">
    <div class="flex gap-2">
      <svg class="w-5 h-5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
        <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd"/>
      </svg>
      <div>
        <p class="font-medium">{{ t('profile.changePassword.externalLoginNote') }}</p>
        <ul class="mt-2 text-sm space-y-1">
          <li v-for="login in props.externalLogins" :key="login.loginProvider" class="flex items-center gap-1">
            • {{ login.providerDisplayName || login.loginProvider }}
          </li>
        </ul>
      </div>
    </div>
  </div>

  <!-- Show warning if policy disabled -->
  <div v-else-if="!props.allowPasswordChange" 
       class="bg-yellow-50 border border-yellow-200 text-yellow-800 px-4 py-3 rounded mb-6">
    <div class="flex gap-2">
      <svg class="w-5 h-5 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
        <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
      </svg>
      <p>{{ t('profile.changePassword.disabledByPolicy') }}</p>
    </div>
  </div>
</template>

<script setup>
import { ref, defineProps } from 'vue'
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

const form = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
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
  if (form.value.newPassword !== form.value.confirmPassword) {
    errors.value.push(t('profile.changePassword.passwordMismatch'))
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
      errors.value.push(t('profile.changePassword.disabledByPolicy'))
    } else {
      const data = await res.json()
      if (data.errors && Array.isArray(data.errors)) {
        errors.value = data.errors.map(e => e.description || e.toString())
      } else if (data.error) {
        errors.value.push(data.error)
      } else {
        errors.value.push(t('common.errorOccurred'))
      }
    }
  } catch (err) {
    errors.value.push(t('common.networkError'))
    console.error('Failed to change password:', err)
  } finally {
    loading.value = false
  }
}
</script>
