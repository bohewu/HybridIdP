<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '../../../components/common/BaseModal.vue'
import PasswordPolicyInput from './PasswordPolicyInput.vue'

const { t } = useI18n()

const props = defineProps({
  user: { type: Object, default: null },
  policy: { type: Object, required: true }
})

const emit = defineEmits(['close', 'save'])

const isEdit = computed(() => !!props.user)

const form = ref({
  email: '',
  firstName: '',
  lastName: '',
  userName: '',
  phoneNumber: '',
  department: '',
  jobTitle: '',
  employeeId: '',
  password: '',
  confirmPassword: ''
})

const errors = ref({})
const saving = ref(false)
const error = ref('')
const passwordInputRef = ref(null)

const initForm = () => {
  error.value = ''
  if (props.user) {
    form.value = {
      email: props.user.email || '',
      firstName: props.user.firstName || '',
      lastName: props.user.lastName || '',
      userName: props.user.userName || '',
      phoneNumber: props.user.phoneNumber || '',
      department: props.user.department || '',
      jobTitle: props.user.jobTitle || '',
      employeeId: props.user.employeeId || '',
      password: '',
      confirmPassword: ''
    }
  } else {
    form.value = {
      email: '',
      firstName: '',
      lastName: '',
      userName: '',
      phoneNumber: '',
      department: '',
      jobTitle: '',
      employeeId: '',
      password: '',
      confirmPassword: ''
    }
  }
  errors.value = {}
}

const validate = () => {
  errors.value = {}
  
  if (!form.value.email) {
    errors.value.email = 'users.emailRequired'
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.value.email)) {
    errors.value.email = 'users.invalidEmail'
  }
  
  if (!form.value.userName) {
    errors.value.userName = 'users.usernameRequired'
  }
  
  // Let the child component handle its own validation, but we can trigger it.
  passwordInputRef.value?.validate()

  return Object.keys(errors.value).length === 0
}

const handlePasswordUpdate = (newPassword) => {
  form.value.password = newPassword
}

const handlePasswordErrors = (passwordErrors) => {
  if (Object.keys(passwordErrors).length > 0) {
    errors.value = { ...errors.value, ...passwordErrors }
  } else {
    delete errors.value.password
    delete errors.value.confirmPassword
  }
}

const handleSubmit = async () => {
  if (!validate()) {
    // Also trigger validation in child component
    const passwordIsValid = passwordInputRef.value?.validate()
    if (!passwordIsValid) {
      return
    }
    return
  }
  
  saving.value = true
  error.value = ''
  
  try {
    const payload = {
      email: form.value.email,
      userName: form.value.userName,
      firstName: form.value.firstName || null,
      lastName: form.value.lastName || null,
      phoneNumber: form.value.phoneNumber || null,
      department: form.value.department || null,
      jobTitle: form.value.jobTitle || null,
      employeeId: form.value.employeeId || null
    }
    
    // For updates, preserve existing user state fields that aren't in the form
    if (isEdit.value && props.user) {
      payload.isActive = props.user.isActive
      payload.emailConfirmed = props.user.emailConfirmed
      payload.phoneNumberConfirmed = props.user.phoneNumberConfirmed
      payload.roles = props.user.roles || []
    }
    
    // Only include password if it's set (create or edit with password change)
    if (form.value.password) {
      payload.password = form.value.password
    }
    
    const url = isEdit.value
      ? `/api/admin/users/${props.user.id}`
      : '/api/admin/users'
    
    const method = isEdit.value ? 'PUT' : 'POST'
    
    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })
    
    if (!response.ok) {
      const errorData = await response.json().catch(() => null)
      const errorMessage = errorData?.message || t('users.errors.saveFailed')
      throw new Error(errorMessage)
    }
    
    emit('save')
  } catch (e) {
    error.value = e.message || t('users.errors.unknownSaveFailed')
    console.error('Error saving user:', e)
  } finally {
    saving.value = false
  }
}

const handleClose = () => {
  if (saving.value) return
  emit('close')
}

onMounted(() => {
  initForm()
})
</script>

<template>
  <BaseModal
    :show="true"
    :title="isEdit ? $t('users.editUser') : $t('users.createNewUser')"
    size="xl"
    :show-close-icon="true"
    :close-on-backdrop="false"
    :close-on-esc="true"
    :loading="saving"
    @close="handleClose"
  >
    <template #body>
      <form @submit.prevent="handleSubmit">
        <!-- Error Alert -->
        <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
          <p class="text-sm text-red-700">{{ error }}</p>
        </div>
        <div class="max-h-[65vh] overflow-y-auto px-1">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-x-4 gap-y-5">
                      <!-- Email -->
                      <div>
                        <label for="email" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.email') }} <span class="text-red-600">*</span>
                        </label>
                        <input
                            id="email"
                            v-model="form.email"
                            type="email"
                            :disabled="isEdit"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed transition-colors h-10 px-3"
                            :class="{ 'border-red-500': errors.email }"
                            required
                            :placeholder="$t('users.email')"
                        />
                        <p v-if="errors.email" class="mt-1.5 text-sm text-red-600">{{ $t(errors.email) }}</p>
                        <p v-if="isEdit" class="mt-1.5 text-xs text-gray-500">
                          {{ $t('users.emailCannotBeChanged') }}</p>
                      </div>

                      <!-- Username -->
                      <div>
                        <label for="userName" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.username') }} <span class="text-red-600">*</span>
                        </label>
                        <input
                            id="userName"
                            v-model="form.userName"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :class="{ 'border-red-500': errors.userName }"
                            required
                            :placeholder="$t('users.username')"
                        />
                        <p v-if="errors.userName" class="mt-1.5 text-sm text-red-600">{{ $t(errors.userName) }}</p>
                      </div>

                      <!-- First Name -->
                      <div>
                        <label for="firstName" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.firstName') }}
                        </label>
                        <input
                            id="firstName"
                            v-model="form.firstName"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.firstName')"
                        />
                      </div>

                      <!-- Last Name -->
                      <div>
                        <label for="lastName" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.lastName') }}
                        </label>
                        <input
                            id="lastName"
                            v-model="form.lastName"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.lastName')"
                        />
                      </div>

                      <!-- Phone Number -->
                      <div>
                        <label for="phoneNumber" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.phoneNumber') }}
                        </label>
                        <input
                            id="phoneNumber"
                            v-model="form.phoneNumber"
                            type="tel"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.phoneNumber')"
                        />
                      </div>

                      <!-- Employee ID -->
                      <div>
                        <label for="employeeId" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.employeeId') }}
                        </label>
                        <input
                            id="employeeId"
                            v-model="form.employeeId"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.employeeId')"
                        />
                      </div>

                      <!-- Department -->
                      <div>
                        <label for="department" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.department') }}
                        </label>
                        <input
                            id="department"
                            v-model="form.department"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.department')"
                        />
                      </div>

                      <!-- Job Title -->
                      <div>
                        <label for="jobTitle" class="block text-sm font-medium text-gray-700 mb-1.5">
                          {{ $t('users.jobTitle') }}
                        </label>
                        <input
                            id="jobTitle"
                            v-model="form.jobTitle"
                            type="text"
                            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10 px-3"
                            :placeholder="$t('users.jobTitle')"
                        />
                      </div>
                    </div>

          <div class="mt-6 pt-6 border-t border-gray-200">
            <h4 class="text-md font-medium text-gray-900 mb-5">
              {{ isEdit ? $t('users.changePasswordOptional') : $t('users.password') }}
            </h4>

            <PasswordPolicyInput
              ref="passwordInputRef"
              :policy="policy"
              :is-edit-mode="isEdit"
              @update:password="handlePasswordUpdate"
              @update:errors="handlePasswordErrors"
            />
          </div>
        </div>
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        @click="handleSubmit"
        :disabled="saving"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white"
             xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor"
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        {{
          saving ? $t('users.saving') : (isEdit ? $t('users.updateUser') : $t('users.createUser'))
        }}
      </button>
      <button
        type="button"
        @click="handleClose"
        :disabled="saving"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {{ $t('users.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>
