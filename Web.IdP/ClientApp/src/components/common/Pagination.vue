<template>
  <div v-if="totalCount > 0" class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-3 px-4 py-3 border-t border-gray-200">
    <div class="flex-1 flex justify-between sm:hidden">
      <button
        @click="emit('page-change', page - 1)"
        :disabled="page === 1"
        class="relative inline-flex items-center justify-center px-4 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
      >
        {{ $t('common.pagination.previous') }}
      </button>
      <button
        @click="emit('page-change', page + 1)"
        :disabled="page === totalPages"
        class="relative inline-flex items-center justify-center px-4 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
      >
        {{ $t('common.pagination.next') }}
      </button>
    </div>
    <div class="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
      <div>
        <p class="text-sm text-gray-700">
          {{ $t('common.pagination.showing', { 
            from: (page - 1) * pageSize + 1, 
            to: Math.min(page * pageSize, totalCount), 
            total: totalCount 
          }) }}
        </p>
      </div>
      <div class="flex items-center gap-2">
        <select
          :value="pageSize"
          @change="emit('page-size-change', Number($event.target.value))"
          class="block px-3 text-base border-gray-300 focus:outline-none focus:ring-google-500 focus:border-google-500 sm:text-sm rounded-md transition-colors h-10"
        >
          <option :value="10">{{ $t('common.pagination.perPage', { count: 10 }) }}</option>
          <option :value="25">{{ $t('common.pagination.perPage', { count: 25 }) }}</option>
          <option :value="50">{{ $t('common.pagination.perPage', { count: 50 }) }}</option>
          <option :value="100">{{ $t('common.pagination.perPage', { count: 100 }) }}</option>
        </select>
        <nav class="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
          <button
            @click="emit('page-change', page - 1)"
            :disabled="page === 1"
            class="relative inline-flex items-center justify-center px-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          >
            <span class="sr-only">{{ $t('common.pagination.previous') }}</span>
            <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clip-rule="evenodd" />
            </svg>
          </button>
          <button
            v-for="p in Math.min(totalPages, 10)"
            :key="p"
            @click="emit('page-change', p)"
            :class="[
              'relative inline-flex items-center justify-center px-4 border text-sm font-medium transition-colors h-10',
              p === page
                ? 'z-10 bg-google-100 border-google-500 text-google-500'
                : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
            ]"
          >
            {{ p }}
          </button>
          <button
            @click="emit('page-change', page + 1)"
            :disabled="page === totalPages"
            class="relative inline-flex items-center justify-center px-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          >
            <span class="sr-only">{{ $t('common.pagination.next') }}</span>
            <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
            </svg>
          </button>
        </nav>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  totalCount: { type: Number, required: true },
  totalPages: { type: Number, required: true }
})

const emit = defineEmits(['page-change', 'page-size-change'])
</script>
