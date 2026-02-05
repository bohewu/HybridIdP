<script setup>
import { ref, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

// Common user property paths for claim mapping
const propertyPathOptions = [
  { value: 'Email', labelKey: 'claims.form.propertyPaths.email' },
  { value: 'UserName', labelKey: 'claims.form.propertyPaths.userName' },
  { value: 'FirstName', labelKey: 'claims.form.propertyPaths.firstName' },
  { value: 'LastName', labelKey: 'claims.form.propertyPaths.lastName' },
  { value: 'PhoneNumber', labelKey: 'claims.form.propertyPaths.phoneNumber' },
  { value: 'Department', labelKey: 'claims.form.propertyPaths.department' },
  { value: 'JobTitle', labelKey: 'claims.form.propertyPaths.jobTitle' },
  { value: 'Locale', labelKey: 'claims.form.propertyPaths.locale' },
  { value: 'PersonId', labelKey: 'claims.form.propertyPaths.personId' },
  { value: 'Person.FirstName', labelKey: 'claims.form.propertyPaths.personFirstName' },
  { value: 'Person.LastName', labelKey: 'claims.form.propertyPaths.personLastName' },
  { value: 'Person.Department', labelKey: 'claims.form.propertyPaths.personDepartment' },
  { value: 'Person.JobTitle', labelKey: 'claims.form.propertyPaths.personJobTitle' },
  { value: 'Person.EmployeeId', labelKey: 'claims.form.propertyPaths.personEmployeeId' },
  { value: 'Person.Email', labelKey: 'claims.form.propertyPaths.personEmail' },
  { value: 'Person.NationalId', labelKey: 'claims.form.propertyPaths.personNationalIdHash' }
]

const props = defineProps({
  show: {
    type: Boolean,
    default: false
  },
  claim: {
    type: Object,
    default: null
  },
  error: {
    type: String,
    default: null
  }
})

const emit = defineEmits(['close', 'save'])

const formData = ref({
  name: '',
  displayName: '',
  description: '',
  claimType: '',
  userPropertyPath: '',
  dataType: 'String',
  isRequired: false
})

const useCustomPath = ref(false)
const customPathValue = ref('')

// Check if current path is in the predefined options
const isKnownPath = computed(() => {
  return propertyPathOptions.some(opt => opt.value === formData.value.userPropertyPath)
})

// Check if claim is protected (PersonId-related fields)
const isProtected = computed(() => {
  const protectedPaths = ['PersonId', 'Person.NationalId', 'Person.NationalIdHash']
  return protectedPaths.includes(formData.value.userPropertyPath)
})

const saving = ref(false)

watch(() => props.claim, (newClaim) => {
  if (newClaim) {
    formData.value = {
      name: newClaim.name,
      displayName: newClaim.displayName,
      description: newClaim.description || '',
      claimType: newClaim.claimType,
      userPropertyPath: newClaim.userPropertyPath,
      dataType: newClaim.dataType,
      isRequired: newClaim.isRequired
    }
    // If existing claim has a custom path, show custom input
    const known = propertyPathOptions.some(opt => opt.value === newClaim.userPropertyPath)
    useCustomPath.value = !known && !!newClaim.userPropertyPath
    customPathValue.value = useCustomPath.value ? newClaim.userPropertyPath : ''
  } else {
    formData.value = {
      name: '',
      displayName: '',
      description: '',
      claimType: '',
      userPropertyPath: '',
      dataType: 'String',
      isRequired: false
    }
    useCustomPath.value = false
    customPathValue.value = ''
  }
}, { immediate: true })

// Sync custom path value to formData
watch(customPathValue, (val) => {
  if (useCustomPath.value) {
    formData.value.userPropertyPath = val
  }
})

// When switching to custom, clear the select value
watch(useCustomPath, (val) => {
  if (val) {
    formData.value.userPropertyPath = customPathValue.value
  } else {
    customPathValue.value = ''
  }
})

const handleSubmit = async () => {
  saving.value = true
  try {
    await emit('save', formData.value)
  } finally {
    saving.value = false
  }
}

const handleClose = () => {
  if (!saving.value) {
    emit('close')
  }
}
</script>

<template>
  <BaseModal
    :show="show"
    :title="claim ? t('claims.form.editTitle') : t('claims.form.createTitle')"
    size="lg"
    :loading="saving"
    :close-on-backdrop="false"
    @close="handleClose"
  >
    <template #body>
      <!-- Error Alert -->
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <p class="text-sm text-red-700">{{ error }}</p>
      </div>

      <form id="claim-form" @submit.prevent="handleSubmit">
        <!-- Name -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.name') }} *</label>
          <input
            v-model="formData.name"
            type="text"
            required
            :disabled="claim?.isStandard || isProtected"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            :placeholder="t('claims.form.namePlaceholder')"
            data-test-id="claim-name-input"
          />
        </div>

        <!-- Display Name -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.displayName') }} *</label>
          <input
            v-model="formData.displayName"
            type="text"
            required
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm transition-colors h-10 px-3"
            :placeholder="t('claims.form.displayNamePlaceholder')"
             data-test-id="claim-display-name-input"
          />
        </div>

        <!-- Description -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.description') }}</label>
          <textarea
            v-model="formData.description"
            rows="2"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm transition-colors px-3 py-2"
            :placeholder="t('claims.form.descriptionPlaceholder')"
            data-test-id="claim-description-input"
          ></textarea>
        </div>

        <!-- Claim Type -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.claimType') }} *</label>
          <input
            v-model="formData.claimType"
            type="text"
            required
            :disabled="claim?.isStandard || isProtected"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            :placeholder="t('claims.form.claimTypePlaceholder')"
             data-test-id="claim-type-input"
          />
          <p class="mt-1.5 text-xs text-gray-500">{{ t('claims.form.claimTypeHelp') }}</p>
        </div>

        <!-- User Property Path -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.userPropertyPath') }} *</label>
          
          <!-- Select for common paths -->
          <select
            v-if="!useCustomPath"
            v-model="formData.userPropertyPath"
            :disabled="claim?.isStandard || isProtected"
            required
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            data-test-id="claim-property-path-select"
          >
            <option value="" disabled>{{ t('claims.form.selectPropertyPath') }}</option>
            <option v-for="opt in propertyPathOptions" :key="opt.value" :value="opt.value">
              {{ t(opt.labelKey) }} ({{ opt.value }})
            </option>
          </select>
          
          <!-- Custom path input -->
          <input
            v-else
            v-model="customPathValue"
            type="text"
            required
            :disabled="claim?.isStandard || isProtected"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            :placeholder="t('claims.form.userPropertyPathPlaceholder')"
            data-test-id="claim-property-path-input"
          />
          
          <!-- Toggle for custom path -->
          <label class="flex items-center mt-2" v-if="!claim?.isStandard && !isProtected">
            <input
              v-model="useCustomPath"
              type="checkbox"
              class="rounded border-gray-300 text-google-500 shadow-sm focus:border-google-500 focus:ring focus:ring-google-100 focus:ring-opacity-50 h-4 w-4"
              data-test-id="claim-use-custom-path-checkbox"
            />
            <span class="ml-2 text-xs text-gray-500">{{ t('claims.form.useCustomPath') }}</span>
          </label>
          
          <p class="mt-1.5 text-xs text-gray-500">{{ t('claims.form.userPropertyPathHelp') }}</p>
        </div>

        <!-- Data Type -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.dataType') }} *</label>
          <select
            v-model="formData.dataType"
            :disabled="claim?.isStandard || isProtected"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-google-500 focus:border-google-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
             data-test-id="claim-data-type-select"
          >
            <option value="String">{{ t('claims.form.dataTypes.string') }}</option>
            <option value="Boolean">{{ t('claims.form.dataTypes.boolean') }}</option>
            <option value="Integer">{{ t('claims.form.dataTypes.integer') }}</option>
            <option value="DateTime">{{ t('claims.form.dataTypes.dateTime') }}</option>
            <option value="JSON">{{ t('claims.form.dataTypes.json') }}</option>
          </select>
        </div>

        <!-- Is Required -->
        <div class="mb-5">
          <label class="flex items-center">
            <input
              v-model="formData.isRequired"
              type="checkbox"
              :disabled="claim?.isStandard || isProtected"
              class="rounded border-gray-300 text-google-500 shadow-sm focus:border-google-500 focus:ring focus:ring-google-100 focus:ring-opacity-50 disabled:bg-gray-100 h-4 w-4"
              data-test-id="claim-required-checkbox"
            />
            <span class="ml-2 text-sm text-gray-700">{{ t('claims.form.isRequired') }}</span>
          </label>
        </div>

        <div v-if="claim?.isStandard" class="mt-4 p-3 bg-blue-50 rounded-md">
          <p class="text-sm text-blue-800">
            {{ t('claims.form.standardNote') }}
          </p>
        </div>

        <div v-if="isProtected" class="mt-4 p-3 bg-amber-50 rounded-md">
          <p class="text-sm text-amber-800">
            {{ t('claims.form.protectedNote') }}
          </p>
        </div>
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        form="claim-form"
        :disabled="saving"
        class="inline-flex w-full justify-center rounded-md bg-google-500 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-google-1000 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
        data-test-id="claim-save-btn"
      >
        {{ saving ? t('claims.form.saving') : t('claims.form.save') }}
      </button>
      <button
        type="button"
        @click="handleClose"
        :disabled="saving"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
        data-test-id="claim-cancel-btn"
      >
        {{ t('claims.form.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>
