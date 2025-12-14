<script setup>
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

const props = defineProps({
  resource: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['submit', 'cancel'])

const isEdit = computed(() => props.resource !== null)
const submitting = ref(false)
const error = ref(null)

const formData = ref({
  key: '',
  culture: 'zh-TW',
  value: '',
  category: ''
})

// Initialize form
watch(() => props.resource, (newVal) => {
  if (newVal) {
    formData.value = {
      key: newVal.key,
      culture: newVal.culture, // Usually readonly in update but API allows value/category update only
      value: newVal.value,
      category: newVal.category || ''
    }
  } else {
    formData.value = {
      key: '',
      culture: 'zh-TW', // Default
      value: '',
      category: ''
    }
  }
}, { immediate: true })

const handleSubmit = async () => {
  submitting.value = true
  error.value = null

  try {
    const url = isEdit.value
      ? `/api/admin/localization/${props.resource.id}`
      : '/api/admin/localization'
    
    const method = isEdit.value ? 'PUT' : 'POST'

    const payload = isEdit.value 
        ? {
            value: formData.value.value,
            category: formData.value.category || null
          }
        : {
            key: formData.value.key,
            culture: formData.value.culture,
            value: formData.value.value,
            category: formData.value.category || null
        }

    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })

    if (!response.ok) {
        const errorText = await response.text()
        let errorMessage = `HTTP error! status: ${response.status}`
        try {
            const errorJson = JSON.parse(errorText)
            errorMessage = errorJson.message || errorJson.title || errorMessage
        } catch {
             if (errorText && errorText.length < 200) errorMessage = errorText
        }
        throw new Error(errorMessage)
    }

    emit('submit')
  } catch (e) {
    error.value = e.message
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <BaseModal
    :show="true"
    :title="isEdit ? $t('localization.form.editTitle') : $t('localization.form.createTitle')"
    :loading="submitting"
    @close="$emit('cancel')"
  >
    <template #body>
      <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
        <p class="text-sm text-red-700">{{ error }}</p>
      </div>

      <form id="localization-form" @submit.prevent="handleSubmit" class="space-y-4">
        
        <!-- Key -->
        <div>
          <label for="key" class="block text-sm font-medium text-gray-700">{{ $t('localization.form.key') }} <span class="text-red-500">*</span></label>
          <input
            id="key"
            data-test-id="key-input"
            v-model="formData.key"
            type="text"
            required
            :disabled="isEdit"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed"
            :placeholder="$t('localization.form.keyPlaceholder')"
          />
        </div>

        <!-- Culture -->
        <div>
          <label for="culture" class="block text-sm font-medium text-gray-700">{{ $t('localization.form.culture') }} <span class="text-red-500">*</span></label>
          <select
            id="culture"
            data-test-id="culture-select"
            v-model="formData.culture"
            required
            :disabled="isEdit"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3 disabled:bg-gray-100 disabled:cursor-not-allowed"
          >
            <option value="zh-TW">繁體中文 (zh-TW)</option>
            <option value="en-US">English (en-US)</option>
          </select>
        </div>

        <!-- Category -->
        <div>
          <label for="category" class="block text-sm font-medium text-gray-700">{{ $t('localization.form.category') }}</label>
          <input
            id="category"
            data-test-id="category-input"
            v-model="formData.category"
            type="text"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3"
            :placeholder="$t('localization.form.categoryPlaceholder')"
          />
        </div>

        <!-- Value -->
        <div>
          <label for="value" class="block text-sm font-medium text-gray-700">{{ $t('localization.form.value') }} <span class="text-red-500">*</span></label>
          <textarea
            id="value"
            data-test-id="value-textarea"
            v-model="formData.value"
            required
            rows="3"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm p-3"
            :placeholder="$t('localization.form.valuePlaceholder')"
          ></textarea>
        </div>

      </form>
    </template>

    <template #footer>
      <button
        type="submit"
        form="localization-form"
        data-test-id="submit-btn"
        :disabled="submitting"
        class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto disabled:opacity-50"
      >
        {{ submitting ? $t('localization.form.saving') : $t('localization.form.save') }}
      </button>
      <button
        type="button"
        @click="$emit('cancel')"
        :disabled="submitting"
        class="mt-2.5 inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:mt-0 sm:w-auto"
      >
        {{ $t('localization.form.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>
