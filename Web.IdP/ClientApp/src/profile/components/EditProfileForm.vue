<template>
  <div class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6" data-testid="edit-profile-form">
    <div class="px-6 py-5 border-b border-gray-200 bg-gray-50 rounded-t-lg">
      <h3 class="text-lg font-semibold text-gray-900 flex items-center gap-2">
        <i class="bi bi-person-gear text-indigo-600"></i>
        {{ t('profile.edit.title') }}
      </h3>
    </div>
    <div class="px-6 py-6">
      <!-- Success Message -->
      <transition enter-active-class="transition ease-out duration-300" enter-from-class="opacity-0 translate-y-[-10px]" enter-to-class="opacity-100 translate-y-0" leave-active-class="transition ease-in duration-200" leave-from-class="opacity-100" leave-to-class="opacity-0">
        <div v-if="showSuccess" class="mb-6 bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded-md flex items-center gap-3">
          <i class="bi bi-check-circle-fill"></i>
          {{ t('profile.edit.success') }}
        </div>
      </transition>

      <!-- Error Message -->
      <div v-if="error" class="mb-6 bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-md flex items-start gap-3">
        <i class="bi bi-exclamation-triangle-fill mt-0.5"></i>
        <p>{{ error }}</p>
      </div>

      <form @submit.prevent="handleSubmit" class="space-y-6">
        <!-- Phone Number -->
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">
            {{ t('profile.edit.phoneNumber') }}
          </label>
          <div class="relative rounded-md shadow-sm">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
              <i class="bi bi-telephone"></i>
            </div>
            <input 
              v-model="form.phoneNumber"
              type="tel"
              class="block w-full pl-10 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              :placeholder="t('profile.edit.phoneNumberPlaceholder')"
            />
          </div>
        </div>

        <div class="grid grid-cols-1 gap-6 sm:grid-cols-2">
          <!-- Locale -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('profile.edit.locale') }}
            </label>
            <div class="relative rounded-md shadow-sm">
              <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
                <i class="bi bi-translate"></i>
              </div>
              <select 
                v-model="form.locale"
                class="block w-full pl-10 border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              >
                <option value="">{{ t('profile.edit.selectLocale') }}</option>
                <option value="zh-TW">繁體中文 (zh-TW)</option>
                <option value="en-US">English (en-US)</option>
              </select>
            </div>
          </div>

          <!-- TimeZone -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('profile.edit.timeZone') }}
            </label>
            <div class="relative rounded-md shadow-sm">
              <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-gray-400">
                <i class="bi bi-clock"></i>
              </div>
              <select 
                v-model="form.timeZone"
                class="block w-full pl-10 border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm h-10"
              >
                <option value="">{{ t('profile.edit.selectTimeZone') }}</option>
                <option value="Asia/Taipei">Asia/Taipei (GMT+8)</option>
                <option value="America/Los_Angeles">America/Los_Angeles (PST)</option>
                <option value="America/New_York">America/New_York (EST)</option>
                <option value="Europe/London">Europe/London (GMT)</option>
                <option value="Asia/Tokyo">Asia/Tokyo (JST)</option>
              </select>
            </div>
          </div>
        </div>

        <!-- Submit Button -->
        <div class="flex justify-end gap-3 pt-4 border-t border-gray-100">
          <button 
            type="button"
            @click="resetForm"
            class="px-4 py-2 border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 text-sm font-medium transition-colors"
          >
            {{ t('profile.common.cancel') }}
          </button>
          <button 
            type="submit"
            :disabled="loading"
            class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <i v-if="loading" class="bi bi-arrow-repeat animate-spin mr-2"></i>
            <i v-else class="bi bi-check-lg mr-2"></i>
            {{ loading ? t('profile.common.saving') : t('profile.common.save') }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, defineProps, defineEmits, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  profile: {
    type: Object,
    required: true
  }
})

const emit = defineEmits(['updated'])

const { t } = useI18n()
const loading = ref(false)
const showSuccess = ref(false)
const error = ref(null)

const form = ref({
  phoneNumber: '',
  locale: '',
  timeZone: ''
})

// Initialize form with profile data
watch(() => props.profile, (newProfile) => {
  if (newProfile?.person) {
    form.value = {
      phoneNumber: newProfile.person.phoneNumber || '',
      locale: newProfile.person.locale || '',
      timeZone: newProfile.person.timeZone || ''
    }
  }
}, { immediate: true })

const resetForm = () => {
  if (props.profile?.person) {
    form.value = {
      phoneNumber: props.profile.person.phoneNumber || '',
      locale: props.profile.person.locale || '',
      timeZone: props.profile.person.timeZone || ''
    }
  }
  showSuccess.value = false
  error.value = null
}

const handleSubmit = async () => {
  error.value = null
  showSuccess.value = false
  
  // Basic frontend validation
  // Removed strict regex validation as per user request to allow various formats
  
  loading.value = true

  try {
    const res = await fetch('/api/profile', {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      credentials: 'include',
      body: JSON.stringify(form.value)
    })

    if (res.ok) {
      showSuccess.value = true
      emit('updated')
      
      setTimeout(() => {
        showSuccess.value = false
      }, 3000)
    } else {
      const data = await res.json()
      
      // Improved error handling to extract specific validation messages
      let errorMessage = data.title || t('profile.common.errorOccurred')
      
      if (data.errors) {
          // Get the first validation error message
          const firstError = Object.values(data.errors).flat()[0]
          if (firstError) {
              errorMessage = String(firstError)
          }
      } else if (data.error) {
          errorMessage = data.error
      }
      
      error.value = errorMessage
    }
  } catch (err) {
    error.value = t('profile.common.networkError')
    console.error('Failed to update profile:', err)
  } finally {
    loading.value = false
  }
}
</script>
