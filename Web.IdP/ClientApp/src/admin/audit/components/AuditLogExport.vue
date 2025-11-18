<script setup>
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const emit = defineEmits(['export'])

const exportCsv = () => {
  emit('export', 'csv')
}

const exportExcel = () => {
  emit('export', 'excel')
}
</script>

<template>
  <div class="audit-log-export">
    <!-- Export Card -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200 mt-6">
      <div class="p-4">
        <h3 class="text-lg font-medium text-gray-900 mb-4">{{ t('admin.audit.export.title') }}</h3>

        <div class="flex gap-3">
          <button
            @click="exportCsv"
            class="inline-flex items-center px-4 py-2 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
            </svg>
            {{ t('admin.audit.export.csv') }}
          </button>

          <button
            @click="exportExcel"
            class="inline-flex items-center px-4 py-2 border border-green-300 text-green-700 text-sm font-medium rounded-md hover:bg-green-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
          >
            <svg class="h-4 w-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
            </svg>
            {{ t('admin.audit.export.excel') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>