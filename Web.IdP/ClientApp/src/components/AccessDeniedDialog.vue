<!-- AccessDeniedDialog Component -->
<!-- Shows when user tries to access a feature without permission -->
<script setup>
import { useI18n } from 'vue-i18n'
import BaseModal from './common/BaseModal.vue'

const { t } = useI18n()

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  message: {
    type: String,
    default: ''
  },
  requiredPermission: {
    type: String,
    default: ''
  },
  contactAdmin: {
    type: Boolean,
    default: true
  },
  showCancel: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(['close']);

const close = () => {
  emit('close');
};
</script>

<template>
  <BaseModal
    :show="show"
    :title="t('accessDenied.title')"
    size="md"
    :show-close-icon="true"
    :close-on-backdrop="true"
    :close-on-esc="true"
    z-index="alert"
    aria-labelledby="modal-title"
    @close="close"
  >
    <template #body>
      <div class="sm:flex sm:items-start">
        <!-- Warning icon -->
        <div class="mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full bg-yellow-100 sm:mx-0 sm:h-10 sm:w-10">
          <svg class="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
        </div>

        <!-- Content -->
        <div class="mt-3 text-center sm:ml-4 sm:mt-0 sm:text-left">
          <div class="mt-2">
            <p class="text-sm text-gray-500">
              {{ message || t('accessDenied.defaultMessage') }}
            </p>
            <p v-if="requiredPermission" class="mt-2 text-xs text-gray-400">
              {{ t('accessDenied.requiredPermission') }}: <code class="rounded bg-gray-100 px-2 py-1 text-gray-700">{{ requiredPermission }}</code>
            </p>
            <p v-if="contactAdmin" class="mt-2 text-xs text-gray-500">
              {{ t('accessDenied.contactAdmin') }}
            </p>
          </div>
        </div>
      </div>
    </template>

    <template #footer>
      <button 
        type="button" 
        @click="close"
        class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:ml-3 sm:w-auto sm:text-sm"
      >
        {{ t('buttons.ok') }}
      </button>
      <button 
        v-if="showCancel"
        type="button" 
        @click="close"
        class="mt-2.5 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:mt-0 sm:w-auto sm:text-sm"
      >
        {{ t('buttons.cancel') }}
      </button>
    </template>
  </BaseModal>
</template>
