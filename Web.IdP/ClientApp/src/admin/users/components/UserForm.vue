<script setup>
import { ref, computed, onMounted } from 'vue'

const props = defineProps({
  user: { type: Object, default: null }
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
    errors.value.email = 'Email is required'
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.value.email)) {
    errors.value.email = 'Invalid email format'
  }
  
  if (!form.value.userName) {
    errors.value.userName = 'Username is required'
  }
  
  if (!isEdit.value) {
    if (!form.value.password) {
      errors.value.password = 'Password is required for new users'
    } else {
      // Validate password complexity for new users
      const passwordErrors = validatePasswordComplexity(form.value.password)
      if (passwordErrors.length > 0) {
        errors.value.password = passwordErrors.join('; ')
      }
    }
    
    if (form.value.password !== form.value.confirmPassword) {
      errors.value.confirmPassword = 'Passwords do not match'
    }
  } else {
    // For edit, only validate password if it's provided
    if (form.value.password) {
      const passwordErrors = validatePasswordComplexity(form.value.password)
      if (passwordErrors.length > 0) {
        errors.value.password = passwordErrors.join('; ')
      }
    }
    
    if (form.value.password && form.value.password !== form.value.confirmPassword) {
      errors.value.confirmPassword = 'Passwords do not match'
    }
  }
  
  return Object.keys(errors.value).length === 0
}

// Password complexity validation matching backend requirements
const validatePasswordComplexity = (password) => {
  const errors = []
  
  if (password.length < 6) {
    errors.push('At least 6 characters')
  }
  
  if (!/[A-Z]/.test(password)) {
    errors.push('At least one uppercase letter (A-Z)')
  }
  
  if (!/[a-z]/.test(password)) {
    errors.push('At least one lowercase letter (a-z)')
  }
  
  if (!/[0-9]/.test(password)) {
    errors.push('At least one digit (0-9)')
  }
  
  if (!/[^A-Za-z0-9]/.test(password)) {
    errors.push('At least one special character (!@#$%^&*)')
  }
  
  return errors
}

// Real-time password strength indicator
const passwordStrength = computed(() => {
  const password = form.value.password
  if (!password) return { label: '', color: '', strength: 0 }
  
  const errors = validatePasswordComplexity(password)
  const requirements = 5
  const met = requirements - errors.length
  
  if (met === requirements) {
    return { label: 'Strong', color: 'text-green-600', strength: 100 }
  } else if (met >= 4) {
    return { label: 'Good', color: 'text-blue-600', strength: 80 }
  } else if (met >= 3) {
    return { label: 'Fair', color: 'text-yellow-600', strength: 60 }
  } else {
    return { label: 'Weak', color: 'text-red-600', strength: 40 }
  }
})

const handleSubmit = async () => {
  if (!validate()) {
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
      throw new Error(errorData?.message || `HTTP error! status: ${response.status}`)
    }
    
    emit('save')
  } catch (e) {
    error.value = e.message || 'Failed to save user'
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
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50" @click.self="handleClose">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full sm:max-w-3xl max-h-[90vh] overflow-y-auto">
          <form @submit.prevent="handleSubmit">
            <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
              <div class="sm:flex sm:items-start">
                <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                  <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
                    {{ isEdit ? 'Edit User' : 'Create New User' }}
                  </h3>

                  <!-- Error Alert -->
                  <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
                    <p class="text-sm text-red-700">{{ error }}</p>
                  </div>

                  <div class="grid grid-cols-1 md:grid-cols-2 gap-x-4 gap-y-5">
                    <!-- Email -->
                    <div>
                      <label for="email" class="block text-sm font-medium text-gray-700 mb-1.5">
                        Email <span class="text-red-600">*</span>
                      </label>
                      <input
                        id="email"
                        v-model="form.email"
                        type="email"
                        :disabled="isEdit"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed transition-colors h-10 px-3"
                        :class="{ 'border-red-500': errors.email }"
                        required
                      />
                      <p v-if="errors.email" class="mt-1.5 text-sm text-red-600">{{ errors.email }}</p>
                      <p v-if="isEdit" class="mt-1.5 text-xs text-gray-500">Email cannot be changed</p>
                    </div>

                    <!-- Username -->
                    <div>
                      <label for="userName" class="block text-sm font-medium text-gray-700 mb-1.5">
                        Username <span class="text-red-600">*</span>
                      </label>
                      <input
                        id="userName"
                        v-model="form.userName"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                        :class="{ 'border-red-500': errors.userName }"
                        required
                      />
                      <p v-if="errors.userName" class="mt-1.5 text-sm text-red-600">{{ errors.userName }}</p>
                    </div>

                    <!-- First Name -->
                    <div>
                      <label for="firstName" class="block text-sm font-medium text-gray-700 mb-1.5">First Name</label>
                      <input
                        id="firstName"
                        v-model="form.firstName"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>

                    <!-- Last Name -->
                    <div>
                      <label for="lastName" class="block text-sm font-medium text-gray-700 mb-1.5">Last Name</label>
                      <input
                        id="lastName"
                        v-model="form.lastName"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>

                    <!-- Phone Number -->
                    <div>
                      <label for="phoneNumber" class="block text-sm font-medium text-gray-700 mb-1.5">Phone Number</label>
                      <input
                        id="phoneNumber"
                        v-model="form.phoneNumber"
                        type="tel"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>

                    <!-- Employee ID -->
                    <div>
                      <label for="employeeId" class="block text-sm font-medium text-gray-700 mb-1.5">Employee ID</label>
                      <input
                        id="employeeId"
                        v-model="form.employeeId"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>

                    <!-- Department -->
                    <div>
                      <label for="department" class="block text-sm font-medium text-gray-700 mb-1.5">Department</label>
                      <input
                        id="department"
                        v-model="form.department"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>

                    <!-- Job Title -->
                    <div>
                      <label for="jobTitle" class="block text-sm font-medium text-gray-700 mb-1.5">Job Title</label>
                      <input
                        id="jobTitle"
                        v-model="form.jobTitle"
                        type="text"
                        class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                      />
                    </div>
                  </div>

                  <div class="mt-6 pt-6 border-t border-gray-200">
                    <h4 class="text-md font-medium text-gray-900 mb-5">
                      {{ isEdit ? 'Change Password (optional)' : 'Password' }}
                    </h4>
                    
                    <div class="grid grid-cols-1 md:grid-cols-2 gap-x-4 gap-y-5">
                      <!-- Password -->
                      <div>
                        <label for="password" class="block text-sm font-medium text-gray-700 mb-1.5">
                          Password
                          <span v-if="!isEdit" class="text-red-600">*</span>
                        </label>
                        <input
                          id="password"
                          v-model="form.password"
                          type="password"
                          autocomplete="new-password"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                          :class="{ 'border-red-500': errors.password }"
                          :required="!isEdit"
                        />
                        <p v-if="errors.password" class="mt-1.5 text-sm text-red-600">{{ errors.password }}</p>
                        <p v-if="isEdit" class="mt-1.5 text-xs text-gray-500">Leave blank to keep current password</p>
                      </div>

                      <!-- Confirm Password -->
                      <div>
                        <label for="confirmPassword" class="block text-sm font-medium text-gray-700 mb-1.5">
                          Confirm Password
                          <span v-if="!isEdit" class="text-red-600">*</span>
                        </label>
                        <input
                          id="confirmPassword"
                          v-model="form.confirmPassword"
                          type="password"
                          autocomplete="new-password"
                          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm transition-colors h-10 px-3"
                          :class="{ 'border-red-500': errors.confirmPassword }"
                          :required="!isEdit"
                        />
                        <p v-if="errors.confirmPassword" class="mt-1.5 text-sm text-red-600">{{ errors.confirmPassword }}</p>
                      </div>
                    </div>

                    <!-- Password Requirements Info Box -->
                    <div v-if="form.password || !isEdit" class="mt-4 p-4 bg-blue-50 border border-blue-200 rounded-md">
                      <p class="text-sm font-medium text-blue-900 mb-2">Password must contain:</p>
                      <ul class="text-sm text-blue-800 space-y-1">
                        <li class="flex items-center">
                          <span :class="form.password && form.password.length >= 6 ? 'text-green-600 font-bold' : 'text-gray-500'">
                            {{ form.password && form.password.length >= 6 ? '✓' : '○' }}
                          </span>
                          <span class="ml-2">At least 6 characters</span>
                        </li>
                        <li class="flex items-center">
                          <span :class="form.password && /[A-Z]/.test(form.password) ? 'text-green-600 font-bold' : 'text-gray-500'">
                            {{ form.password && /[A-Z]/.test(form.password) ? '✓' : '○' }}
                          </span>
                          <span class="ml-2">At least one uppercase letter (A-Z)</span>
                        </li>
                        <li class="flex items-center">
                          <span :class="form.password && /[a-z]/.test(form.password) ? 'text-green-600 font-bold' : 'text-gray-500'">
                            {{ form.password && /[a-z]/.test(form.password) ? '✓' : '○' }}
                          </span>
                          <span class="ml-2">At least one lowercase letter (a-z)</span>
                        </li>
                        <li class="flex items-center">
                          <span :class="form.password && /[0-9]/.test(form.password) ? 'text-green-600 font-bold' : 'text-gray-500'">
                            {{ form.password && /[0-9]/.test(form.password) ? '✓' : '○' }}
                          </span>
                          <span class="ml-2">At least one digit (0-9)</span>
                        </li>
                        <li class="flex items-center">
                          <span :class="form.password && /[^A-Za-z0-9]/.test(form.password) ? 'text-green-600 font-bold' : 'text-gray-500'">
                            {{ form.password && /[^A-Za-z0-9]/.test(form.password) ? '✓' : '○' }}
                          </span>
                          <span class="ml-2">At least one special character (!@#$%^&*)</span>
                        </li>
                      </ul>
                      
                      <!-- Password Strength Indicator -->
                      <div v-if="form.password" class="mt-3">
                        <div class="flex items-center justify-between mb-1">
                          <span class="text-xs text-gray-600">Password Strength:</span>
                          <span :class="['text-xs font-semibold', passwordStrength.color]">{{ passwordStrength.label }}</span>
                        </div>
                        <div class="w-full bg-gray-200 rounded-full h-2">
                          <div 
                            :class="[
                              'h-2 rounded-full transition-all duration-300',
                              passwordStrength.strength === 100 ? 'bg-green-600' :
                              passwordStrength.strength >= 80 ? 'bg-blue-600' :
                              passwordStrength.strength >= 60 ? 'bg-yellow-600' : 'bg-red-600'
                            ]"
                            :style="`width: ${passwordStrength.strength}%`"
                          ></div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
              <button
                type="submit"
                :disabled="saving"
                class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                {{ saving ? 'Saving...' : (isEdit ? 'Update User' : 'Create User') }}
              </button>
              <button
                type="button"
                @click="handleClose"
                :disabled="saving"
                class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Cancel
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>
