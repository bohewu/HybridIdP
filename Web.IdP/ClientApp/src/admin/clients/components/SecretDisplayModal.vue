<script setup>
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

defineProps({
  secret: {
    type: String,
    required: true
  },
  visible: {
    type: Boolean,
    required: true
  }
})

const emit = defineEmits(['close'])

const secretCopied = ref(false)

const copySecret = async (secret) => {
  if (secret) {
    await navigator.clipboard.writeText(secret)
    secretCopied.value = true
    setTimeout(() => {
      secretCopied.value = false
    }, 2000)
  }
}

const handleClose = () => {
  emit('close')
}
</script>

<template>
  <BaseModal
    :show="visible"
    :title="t('clients.secretModal.title')"
    size="md"
    z-index="modal-nested"
    @close="handleClose"
  >
    <template #body>
      <div class="sm:flex sm:items-start">
        <div class="mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full bg-green-100 sm:mx-0 sm:h-10 sm:w-10">
          <svg class="h-6 w-6 text-green-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" aria-hidden="true">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.75l6 6 9-13.5" />
          </svg>
        </div>
        <div class="mt-3 sm:ml-4 sm:mt-0 w-full">
          <p class="text-sm text-gray-500">{{ t('clients.secretModal.message') }}</p>
          <div class="mt-4 relative">
            <input type="text" :value="secret" readonly
                   class="block w-full rounded-md border-gray-300 bg-gray-50 py-2 pl-3 pr-24 text-gray-900 shadow-sm sm:text-sm font-mono" />
            <button type="button" @click="copySecret(secret)"
                    class="absolute inset-y-0 right-0 flex items-center px-3 text-sm font-medium text-indigo-600 hover:text-indigo-900">
              {{ secretCopied ? t('clients.secretModal.copied') : t('clients.secretModal.copy') }}
            </button>
          </div>
        </div>
      </div>
    </template>

    <template #footer>
      <button type="button"
              class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 sm:ml-3 sm:w-auto"
              @click="handleClose">
        {{ t('clients.secretModal.close') }}
      </button>
    </template>
  </BaseModal>
</template>
