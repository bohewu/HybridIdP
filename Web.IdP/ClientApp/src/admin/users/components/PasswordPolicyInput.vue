<script setup>
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  policy: {
    type: Object,
    required: true
  },
  isEditMode: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:password', 'update:errors'])

const password = ref('')
const confirmPassword = ref('')
const errors = ref({})

import { usePasswordValidation } from '@/composables/usePasswordValidation'

const { validatePasswordComplexity, getPasswordRequirements } = usePasswordValidation()

const validate = () => {
  const newErrors = {}
  
  if (!props.isEditMode && !password.value) {
    newErrors.password = t('users.passwordRequired')
  } else if (password.value) {
    const complexityErrors = validatePasswordComplexity(password.value, props.policy)
    if (complexityErrors.length > 0) {
      newErrors.password = complexityErrors.join('; ')
    }
  }
  
  if (password.value && password.value !== confirmPassword.value) {
    newErrors.confirmPassword = t('users.passwordsDoNotMatch')
  } else if (!props.isEditMode && !confirmPassword.value) {
    newErrors.confirmPassword = t('users.confirmPasswordRequired')
  }
  
  errors.value = newErrors
  emit('update:errors', newErrors)
  emit('update:password', password.value)
  return Object.keys(newErrors).length === 0
}

const passwordRequirements = computed(() => {
  return getPasswordRequirements(password.value, props.policy)
})

defineExpose({ validate })

</script>

<template>
  <div>
    <div class="grid grid-cols-1 md:grid-cols-2 gap-x-4 gap-y-5">
      <!-- Password -->
      <div>
        <label for="password" class="block text-sm font-medium text-gray-700 mb-1.5">
          {{ $t('users.password') }}
          <span v-if="!isEditMode" class="text-red-600">*</span>
        </label>
        <input
            id="password"
            v-model="password"
            type="password"
            autocomplete="new-password"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
            :class="{ 'border-red-500': errors.password }"
            :required="!isEditMode"
            :placeholder="$t('users.password')"
            @input="validate"
            data-test-id="user-password-input"
        />
        <p v-if="errors.password" class="mt-1.5 text-sm text-red-600">{{ errors.password }}</p>
        <p v-if="isEditMode" class="mt-1.5 text-xs text-gray-500">
          {{ $t('users.leaveBlankToKeepCurrent') }}
        </p>
      </div>

      <!-- Confirm Password -->
      <div>
        <label for="confirmPassword" class="block text-sm font-medium text-gray-700 mb-1.5">
          {{ $t('users.confirmPassword') }}
          <span v-if="!isEditMode" class="text-red-600">*</span>
        </label>
        <input
            id="confirmPassword"
            v-model="confirmPassword"
            type="password"
            autocomplete="new-password"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
            :class="{ 'border-red-500': errors.confirmPassword }"
            :required="!isEditMode"
            :placeholder="$t('users.confirmPassword')"
            @input="validate"
            data-test-id="user-confirm-password-input"
        />
        <p v-if="errors.confirmPassword" class="mt-1.5 text-sm text-red-600">
          {{ errors.confirmPassword }}
        </p>
      </div>
    </div>

    <!-- Password Requirements Info Box -->
    <div v-if="password || !isEditMode" class="mt-4 p-4 bg-blue-50 border border-blue-200 rounded-md">
      <p class="text-sm font-medium text-blue-900 mb-2">
        {{ $t('users.passwordMustContain') }}
      </p>
      <ul class="text-sm text-blue-800 space-y-1">
        <li v-for="(req, index) in passwordRequirements" :key="index" class="flex items-center">
          <span :class="req.valid ? 'text-green-600 font-bold' : 'text-gray-500'">
            {{ req.valid ? '✓' : '○' }}
          </span>
          <span class="ml-2">{{ req.text }}</span>
        </li>
      </ul>
    </div>
  </div>
</template>
