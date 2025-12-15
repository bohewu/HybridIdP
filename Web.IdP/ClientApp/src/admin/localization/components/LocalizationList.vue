<script setup>
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

defineProps({
  resources: {
    type: Array,
    required: true
  },
  canUpdate: {
    type: Boolean,
    default: false
  },
  canDelete: {
    type: Boolean,
    default: false
  }
})

defineEmits(['edit', 'delete'])
</script>

<template>
  <div class="overflow-x-auto">
    <table class="min-w-full divide-y divide-gray-200" data-test-id="resources-table">
      <thead class="bg-gray-50">
        <tr>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('localization.table.key') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('localization.table.culture') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('localization.table.value') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('localization.table.category') }}
          </th>
          <th scope="col" class="relative px-6 py-3">
            <span class="sr-only">{{ $t('localization.table.actions') }}</span>
          </th>
        </tr>
      </thead>
      <tbody class="bg-white divide-y divide-gray-200">
        <tr v-for="resource in resources" :key="resource.id" :data-test-id="`resource-row-${resource.id}`" class="hover:bg-gray-50">
          <td class="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
            {{ resource.key }}
          </td>
          <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
            <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
              {{ resource.culture }}
            </span>
          </td>
          <td class="px-6 py-4 text-sm text-gray-900 max-w-md truncate" :title="resource.value">
            {{ resource.value }}
          </td>
          <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
            {{ resource.category || '-' }}
          </td>
          <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
            <button
              v-if="canUpdate"
              @click="$emit('edit', resource)"
              :data-test-id="`edit-btn-${resource.id}`"
              class="text-google-500 hover:text-google-700 mr-4"
            >
              {{ $t('localization.actions.edit') }}
            </button>
            <button
              v-if="canDelete"
              @click="$emit('delete', resource.id)"
              :data-test-id="`delete-btn-${resource.id}`"
              class="text-red-600 hover:text-red-900"
            >
              {{ $t('localization.actions.delete') }}
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
