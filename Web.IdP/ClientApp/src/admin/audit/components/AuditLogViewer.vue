<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Pagination from '@/components/common/Pagination.vue'

const { t } = useI18n()

const props = defineProps({
  auditEvents: { type: Array, required: true },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  totalCount: { type: Number, required: true },
  sort: { type: String, default: '' }
})

const emit = defineEmits([
  'page-change',
  'page-size-change',
  'sort-change',
  'refresh'
])

const totalPages = computed(() => {
  return Math.ceil(props.totalCount / props.pageSize)
})

const formatDate = (dateString) => {
  if (!dateString) return t('audit.never')
  return new Date(dateString).toLocaleString()
}

const getEventTypeBadgeClass = (eventType) => {
  // Customize based on event type
  const classes = {
    'UserCreated': 'bg-green-100 text-green-800',
    'UserUpdated': 'bg-blue-100 text-blue-800',
    'UserDeleted': 'bg-red-100 text-red-800',
    'LoginSuccess': 'bg-green-100 text-green-800',
    'LoginFailed': 'bg-red-100 text-red-800',
    'SecurityPolicyChanged': 'bg-yellow-100 text-yellow-800'
  }
  return classes[eventType] || 'bg-gray-100 text-gray-800'
}

const handleSort = (field) => {
  const [currentField, currentDir] = props.sort.split(':')
  let newDir = 'asc'

  if (currentField === field) {
    newDir = currentDir === 'asc' ? 'desc' : 'asc'
  }

  emit('sort-change', `${field}:${newDir}`)
}

const getSortIcon = (field) => {
  const [currentField, currentDir] = props.sort.split(':')
  if (currentField !== field) {
    return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16V4m0 0L3 8m4-4l4 4m6 0v12m0 0l4-4m-4 4l-4-4"></path>
    </svg>`
  }

  if (currentDir === 'asc') {
    return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7"></path>
    </svg>`
  }

  return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"></path>
  </svg>`
}
</script>

<template>
  <div class="audit-log-viewer">
    <!-- Unified Card: Table + Pagination -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
      <!-- Error State -->
      <div v-if="error" class="p-4 border-b border-red-200 bg-red-50">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-red-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <p class="text-sm text-red-800">{{ t('admin.audit.errorLoading') }}: {{ error }}</p>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="loading" class="flex flex-col items-center justify-center py-12">
        <svg class="animate-spin h-10 w-10 text-indigo-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        <p class="mt-2 text-sm text-gray-600">{{ t('admin.audit.loading') }}</p>
      </div>

      <!-- Empty State -->
      <div v-else-if="auditEvents.length === 0" class="text-center py-12">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
        </svg>
        <p class="mt-2 text-sm text-gray-600">{{ t('admin.audit.noEvents') }}</p>
      </div>

      <!-- Table Header with Refresh -->
      <div v-if="!loading && auditEvents.length > 0" class="px-4 py-3 border-b border-gray-200 bg-gray-50 flex justify-between items-center">
        <h3 class="text-sm font-medium text-gray-900">{{ t('admin.audit.events', { count: totalCount }) }}</h3>
        <button
          @click="emit('refresh')"
          class="inline-flex items-center px-3 py-1.5 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
        >
          <svg class="h-4 w-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"></path>
          </svg>
          {{ t('admin.audit.refresh') }}
        </button>
      </div>

        <div class="overflow-x-auto">
        <table class="min-w-full divide-y divide-gray-200">
          <thead class="bg-gray-50">
            <tr>
              <th
                @click="handleSort('timestamp')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ t('tableHeaders.timestamp') }}</span>
                  <span v-html="getSortIcon('timestamp')"></span>
                </div>
              </th>
              <th
                @click="handleSort('eventType')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ t('tableHeaders.eventType') }}</span>
                  <span v-html="getSortIcon('eventType')"></span>
                </div>
              </th>
              <th
                @click="handleSort('user')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ t('tableHeaders.user') }}</span>
                  <span v-html="getSortIcon('user')"></span>
                </div>
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ t('tableHeaders.details') }}
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ t('tableHeaders.ipAddress') }}
              </th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
            <tr v-for="event in auditEvents" :key="event.id" class="hover:bg-gray-50">
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {{ formatDate(event.timestamp) }}
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <span
                  :class="['inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium', getEventTypeBadgeClass(event.eventType)]"
                >
                  {{ event.eventType }}
                </span>
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                {{ event.user || t('audit.system') }}
              </td>
              <td class="px-6 py-4 text-sm text-gray-900 max-w-xs truncate" :title="event.details">
                {{ event.details }}
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {{ event.ipAddress || t('audit.unknown') }}
              </td>
            </tr>
          </tbody>
        </table>
        </div>
      </div>

      <Pagination
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :total-pages="totalPages"
        @page-change="emit('page-change', $event)"
        @page-size-change="emit('page-size-change', $event)"
      />
    </div>
  </div>
</template>