<script setup>
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

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
  }
}, { immediate: true })

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
            :disabled="claim?.isStandard"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
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
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm transition-colors h-10 px-3"
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
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm transition-colors px-3 py-2"
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
            :disabled="claim?.isStandard"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            :placeholder="t('claims.form.claimTypePlaceholder')"
             data-test-id="claim-type-input"
          />
          <p class="mt-1.5 text-xs text-gray-500">{{ t('claims.form.claimTypeHelp') }}</p>
        </div>

        <!-- User Property Path -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.userPropertyPath') }} *</label>
          <input
            v-model="formData.userPropertyPath"
            type="text"
            required
            :disabled="claim?.isStandard"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
            :placeholder="t('claims.form.userPropertyPathPlaceholder')"
             data-test-id="claim-property-path-input"
          />
          <p class="mt-1.5 text-xs text-gray-500">{{ t('claims.form.userPropertyPathHelp') }}</p>
        </div>

        <!-- Data Type -->
        <div class="mb-5">
          <label class="block text-sm font-medium text-gray-700 mb-1.5">{{ t('claims.form.dataType') }} *</label>
          <select
            v-model="formData.dataType"
            :disabled="claim?.isStandard"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm disabled:bg-gray-100 transition-colors h-10 px-3"
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
              :disabled="claim?.isStandard"
              class="rounded border-gray-300 text-indigo-600 shadow-sm focus:border-indigo-300 focus:ring focus:ring-indigo-200 focus:ring-opacity-50 disabled:bg-gray-100 h-4 w-4"
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
      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        form="claim-form"
        :disabled="saving"
        class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
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
