<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  person: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['close', 'save'])

const isEdit = computed(() => !!props.person)
const saving = ref(false)
const error = ref(null)

// Form fields
const formData = ref({
  firstName: '',
  middleName: '',
  lastName: '',
  nickname: '',
  employeeId: '',
  department: '',
  jobTitle: '',
  email: '',
  phoneNumber: '',
  address: '',
  birthdate: '',
  gender: '',
  timeZone: '',
  locale: '',
  profileUrl: '',
  pictureUrl: '',
  website: ''
})

// Load person data for edit
if (props.person) {
  formData.value = {
    firstName: props.person.firstName || '',
    middleName: props.person.middleName || '',
    lastName: props.person.lastName || '',
    nickname: props.person.nickname || '',
    employeeId: props.person.employeeId || '',
    department: props.person.department || '',
    jobTitle: props.person.jobTitle || '',
    email: props.person.email || '',
    phoneNumber: props.person.phoneNumber || '',
    address: props.person.address || '',
    birthdate: props.person.birthdate || '',
    gender: props.person.gender || '',
    timeZone: props.person.timeZone || '',
    locale: props.person.locale || '',
    profileUrl: props.person.profileUrl || '',
    pictureUrl: props.person.pictureUrl || '',
    website: props.person.website || ''
  }
}

const handleSubmit = async () => {
  error.value = null
  
  // Validation
  if (!formData.value.firstName?.trim()) {
    error.value = t('admin.persons.errors.firstNameRequired')
    return
  }
  if (!formData.value.lastName?.trim()) {
    error.value = t('admin.persons.errors.lastNameRequired')
    return
  }
  
  saving.value = true
  
  try {
    const url = isEdit.value 
      ? `/api/admin/persons/${props.person.id}`
      : '/api/admin/persons'
    
    const method = isEdit.value ? 'PUT' : 'POST'
    
    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(formData.value)
    })
    
    if (!response.ok) {
      const errorData = await response.text()
      throw new Error(errorData || `HTTP error! status: ${response.status}`)
    }
    
    emit('save')
  } catch (e) {
    error.value = t('admin.persons.errors.saveFailed', { message: e.message })
    console.error('Error saving person:', e)
  } finally {
    saving.value = false
  }
}

const handleClose = () => {
  emit('close')
}
</script>

<template>
  <div class="fixed inset-0 z-50 overflow-y-auto" aria-labelledby="modal-title" role="dialog" aria-modal="true">
    <div class="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
      <!-- Background overlay -->
      <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" aria-hidden="true" @click="handleClose"></div>

      <!-- Center modal -->
      <span class="hidden sm:inline-block sm:align-middle sm:h-screen" aria-hidden="true">&#8203;</span>

      <div class="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-3xl sm:w-full">
        <form @submit.prevent="handleSubmit">
          <div class="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
            <div class="sm:flex sm:items-start">
              <div class="w-full mt-3 text-center sm:mt-0 sm:text-left">
                <h3 class="text-lg leading-6 font-medium text-gray-900" id="modal-title">
                  {{ isEdit ? t('admin.persons.editPerson') : t('admin.persons.createPerson') }}
                </h3>
                
                <!-- Error Message -->
                <div v-if="error" class="mt-4 bg-red-50 border-l-4 border-red-400 p-4">
                  <p class="text-sm text-red-700">{{ error }}</p>
                </div>

                <div class="mt-4 space-y-4">
                  <!-- Name Section -->
                  <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
                    <div>
                      <label for="firstName" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.firstName') }} <span class="text-red-500">*</span>
                      </label>
                      <input
                        id="firstName"
                        v-model="formData.firstName"
                        type="text"
                        required
                        :placeholder="t('admin.persons.form.firstName')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                    
                    <div>
                      <label for="middleName" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.middleName') }}
                      </label>
                      <input
                        id="middleName"
                        v-model="formData.middleName"
                        type="text"
                        :placeholder="t('admin.persons.form.middleName')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                    
                    <div>
                      <label for="lastName" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.lastName') }} <span class="text-red-500">*</span>
                      </label>
                      <input
                        id="lastName"
                        v-model="formData.lastName"
                        type="text"
                        required
                        :placeholder="t('admin.persons.form.lastName')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                  </div>

                  <!-- Nickname -->
                  <div>
                    <label for="nickname" class="block text-sm font-medium text-gray-700">
                      {{ t('admin.persons.form.nickname') }}
                    </label>
                    <input
                      id="nickname"
                      v-model="formData.nickname"
                      type="text"
                      :placeholder="t('admin.persons.form.nickname')"
                      class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                    />
                  </div>

                  <!-- Employment Info -->
                  <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
                    <div>
                      <label for="employeeId" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.employeeId') }}
                      </label>
                      <input
                        id="employeeId"
                        v-model="formData.employeeId"
                        type="text"
                        :placeholder="t('admin.persons.form.employeeId')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                    
                    <div>
                      <label for="department" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.department') }}
                      </label>
                      <input
                        id="department"
                        v-model="formData.department"
                        type="text"
                        :placeholder="t('admin.persons.form.department')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                    
                    <div>
                      <label for="jobTitle" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.jobTitle') }}
                      </label>
                      <input
                        id="jobTitle"
                        v-model="formData.jobTitle"
                        type="text"
                        :placeholder="t('admin.persons.form.jobTitle')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      />
                    </div>
                  </div>

                  <!-- Personal Info (Optional) -->
                  <div class="border-t pt-4">
                    <h4 class="text-sm font-medium text-gray-700 mb-3">{{ t('admin.persons.form.personalInfo') }}</h4>
                    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                      <div>
                        <label for="gender" class="block text-sm font-medium text-gray-700">
                          {{ t('admin.persons.form.gender') }}
                        </label>
                        <input
                          id="gender"
                          v-model="formData.gender"
                          type="text"
                          :placeholder="t('admin.persons.form.gender')"
                          class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                        />
                      </div>
                      
                      <div>
                        <label for="birthdate" class="block text-sm font-medium text-gray-700">
                          {{ t('admin.persons.form.birthdate') }}
                        </label>
                        <input
                          id="birthdate"
                          v-model="formData.birthdate"
                          type="date"
                          class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                        />
                      </div>
                    </div>
                    
                    <div class="mt-4">
                      <label for="address" class="block text-sm font-medium text-gray-700">
                        {{ t('admin.persons.form.address') }}
                      </label>
                      <textarea
                        id="address"
                        v-model="formData.address"
                        rows="2"
                        :placeholder="t('admin.persons.form.address')"
                        class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                      ></textarea>
                    </div>

                    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 mt-4">
                      <div>
                        <label for="timeZone" class="block text-sm font-medium text-gray-700">
                          {{ t('admin.persons.form.timeZone') }}
                        </label>
                        <input
                          id="timeZone"
                          v-model="formData.timeZone"
                          type="text"
                          :placeholder="t('admin.persons.form.timeZone')"
                          class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                        />
                      </div>
                      
                      <div>
                        <label for="locale" class="block text-sm font-medium text-gray-700">
                          {{ t('admin.persons.form.locale') }}
                        </label>
                        <input
                          id="locale"
                          v-model="formData.locale"
                          type="text"
                          :placeholder="t('admin.persons.form.locale')"
                          class="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                        />
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
          
          <div class="bg-gray-50 px-4 py-3 sm:px-6 sm:flex sm:flex-row-reverse">
            <button
              type="submit"
              :disabled="saving"
              class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-indigo-600 text-base font-medium text-white hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              {{ saving ? t('admin.persons.saving') : t('admin.persons.save') }}
            </button>
            <button
              type="button"
              @click="handleClose"
              :disabled="saving"
              class="mt-2.5 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {{ t('admin.persons.cancel') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
