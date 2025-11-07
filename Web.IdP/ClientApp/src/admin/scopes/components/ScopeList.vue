<script setup>
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

defineProps({
  scopes: {
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
  <div class="bg-white shadow overflow-hidden sm:rounded-lg">
    <div v-if="scopes.length === 0" class="px-6 py-12 text-center">
      <svg class="mx-auto h-12 w-12 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="mt-2 text-sm font-medium text-gray-900">{{ $t('scopes.noScopes') }}</h3>
    </div>

    <table v-else class="min-w-full divide-y divide-gray-200">
      <thead class="bg-gray-50">
        <tr>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('scopes.table.name') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('scopes.table.displayName') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('scopes.table.description') }}
          </th>
          <th scope="col" class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            {{ $t('scopes.table.resources') }}
          </th>
          <th scope="col" class="relative px-6 py-3">
            <span class="sr-only">{{ $t('scopes.table.actions') }}</span>
          </th>
        </tr>
      </thead>
      <tbody class="bg-white divide-y divide-gray-200">
        <tr v-for="scope in scopes" :key="scope.name" class="hover:bg-gray-50">
          <td class="px-6 py-4 whitespace-nowrap">
            <div class="flex items-center">
              <div class="flex-shrink-0 h-10 w-10">
                <div class="h-10 w-10 rounded-full bg-green-100 flex items-center justify-center">
                  <svg class="h-6 w-6 text-green-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                </div>
              </div>
              <div class="ml-4">
                <div class="text-sm font-medium text-gray-900">
                  {{ scope.name }}
                </div>
              </div>
            </div>
          </td>
          <td class="px-6 py-4 whitespace-nowrap">
            <div class="text-sm text-gray-900">{{ scope.displayName || $t('scopes.noDisplayName') }}</div>
          </td>
          <td class="px-6 py-4">
            <div class="text-sm text-gray-900 max-w-xs truncate">{{ scope.description || '-' }}</div>
          </td>
          <td class="px-6 py-4 whitespace-nowrap">
            <div class="text-sm text-gray-500">
              <span v-if="scope.resources && scope.resources.length > 0">
                {{ scope.resources.length }} {{ $t('scopes.table.resourceCount', { count: scope.resources.length }) }}
              </span>
              <span v-else>-</span>
            </div>
          </td>
          <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
            <button
              v-if="canUpdate"
              @click="$emit('edit', scope)"
              class="text-indigo-600 hover:text-indigo-900 mr-4"
            >
              {{ $t('scopes.actions.edit') }}
            </button>
            <button
              v-if="canDelete"
              @click="$emit('delete', scope.name)"
              class="text-red-600 hover:text-red-900"
            >
              {{ $t('scopes.actions.delete') }}
            </button>
            <span v-if="!canUpdate && !canDelete" class="text-gray-400 italic">{{ $t('scopes.actions.noActions') }}</span>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
