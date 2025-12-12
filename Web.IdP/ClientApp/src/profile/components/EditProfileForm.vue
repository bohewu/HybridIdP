<template>
  <div class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6">
    <div class="px-4 py-5 sm:px-6">
      <h3 class="text-lg leading-6 font-medium text-gray-900">{{ t('profile.edit.title') }}</h3>
    </div>
    <div class="border-t border-gray-200 px-4 py-5 sm:px-6">
      <!-- Success Message -->
      <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded flex items-center gap-2">
        <svg class="w-5 h-5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
        </svg>
        {{ t('profile.edit.success') }}
      </div>

      <!-- Error Message -->
      <div v-if="error" class="mb-4 bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
        {{ error }}
      </div>

      <form @submit.prevent="handleSubmit" class="space-y-4">
        <!-- Phone Number -->
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">{{ t('profile.edit.phoneNumber') }}</label>
          <input 
            v-model="form.phoneNumber"
            type="tel"
            class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            :placeholder="t('profile.edit.phoneNumberPlaceholder')"
          />
        </div>

        <!-- Locale -->
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">{{ t('profile.edit.locale') }}</label>
          <select 
            v-model="form.locale"
            class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">{{ t('profile.edit.selectLocale') }}</option>
            <option value="zh-TW">繁體中文 (zh-TW)</option>
            <option value="en-US">English (en-US)</option>
          </select>
        </div>

        <!-- TimeZone -->
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">{{ t('profile.edit.timeZone') }}</label>
          <select 
            v-model="form.timeZone"
            class="w-full border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">{{ t('profile.edit.selectTimeZone') }}</option>
            <option value="Asia/Taipei">Asia/Taipei (GMT+8)</option>
            <option value="America/Los_Angeles">America/Los_Angeles (PST)</option>
            <option value="America/New_York">America/New_York (EST)</option>
            <option value="Europe/London">Europe/London (GMT)</option>
            <option value="Asia/Tokyo">Asia/Tokyo (JST)</option>
          </select>
        </div>

        <!-- Submit Button -->
        <div class="flex justify-end gap-3 pt-4">
          <button 
            type="button"
            @click="resetForm"
            class="px-4 py-2 border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50"
          >
            {{ t('profile.common.cancel') }}
          </button>
          <button 
            type="submit"
            :disabled="loading"
            class="px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
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
  loading.value = true
  error.value = null
  showSuccess.value = false

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
      error.value = data.error || t('profile.common.errorOccurred')
    }
  } catch (err) {
    error.value = t('profile.common.networkError')
    console.error('Failed to update profile:', err)
  } finally {
    loading.value = false
  }
}
</script>
