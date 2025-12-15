<script setup>
import { useI18n } from 'vue-i18n'
import SearchInput from '@/components/common/SearchInput.vue'

const { t } = useI18n()

const props = defineProps({
  filters: { type: Object, required: true }
})

const emit = defineEmits(['filters-change'])

const updateFilters = () => {
  emit('filters-change', { ...props.filters })
}

const clearFilters = () => {
  const cleared = {
    startDate: '',
    endDate: '',
    userId: '',
    eventType: '',
    ipAddress: '',
    search: ''
  }
  emit('filters-change', cleared)
}
</script>

<template>
  <div class="audit-log-filters">
    <!-- Filters Card -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6">
      <div class="p-4 border-b border-gray-200">
        <h3 class="text-lg font-medium text-gray-900 mb-4">{{ t('audit.filters.title') }}</h3>

        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          <!-- Date Range -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.startDate') }}
            </label>
            <input
              type="date"
              :value="filters.startDate"
              @input="filters.startDate = $event.target.value; updateFilters()"
              class="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-google-500 focus:border-google-500 sm:text-sm"
            />
          </div>

          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.endDate') }}
            </label>
            <input
              type="date"
              :value="filters.endDate"
              @input="filters.endDate = $event.target.value; updateFilters()"
              class="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-google-500 focus:border-google-500 sm:text-sm"
            />
          </div>

          <!-- User Search -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.user') }}
            </label>
            <SearchInput
              :model-value="filters.userId"
              @update:model-value="filters.userId = $event; updateFilters()"
              :placeholder="t('audit.filters.userPlaceholder')"
            />
          </div>

          <!-- Event Type -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.eventType') }}
            </label>
            <select
              :value="filters.eventType"
              @change="filters.eventType = $event.target.value; updateFilters()"
              class="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-google-500 focus:border-google-500 sm:text-sm"
            >
              <option value="">{{ t('audit.filters.allEventTypes') }}</option>
              <option value="UserCreated">UserCreated</option>
              <option value="UserUpdated">UserUpdated</option>
              <option value="UserDeleted">UserDeleted</option>
              <option value="LoginSuccess">LoginSuccess</option>
              <option value="LoginFailed">LoginFailed</option>
              <option value="SecurityPolicyChanged">SecurityPolicyChanged</option>
              <!-- Add more as needed -->
            </select>
          </div>

          <!-- IP Address -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.ipAddress') }}
            </label>
            <input
              type="text"
              :value="filters.ipAddress"
              @input="filters.ipAddress = $event.target.value; updateFilters()"
              :placeholder="t('audit.filters.ipPlaceholder')"
              class="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-google-500 focus:border-google-500 sm:text-sm"
            />
          </div>

          <!-- General Search -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              {{ t('audit.filters.search') }}
            </label>
            <SearchInput
              :model-value="filters.search"
              @update:model-value="filters.search = $event; updateFilters()"
              :placeholder="t('audit.filters.searchPlaceholder')"
            />
          </div>
        </div>

        <!-- Clear Filters Button -->
        <div class="mt-4 flex justify-end">
          <button
            @click="clearFilters"
            class="inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-google-500"
          >
            {{ t('audit.filters.clear') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>