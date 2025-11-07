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
  <div class="bg-white shadow overflow-hidden sm:rounded-md">
    <ul role="list" class="divide-y divide-gray-200">
      <li v-if="scopes.length === 0" class="px-6 py-8 text-center text-gray-500">
        {{ $t('scopes.noScopes') }}
      </li>
      <li v-for="scope in scopes" :key="scope.name" class="px-6 py-4 hover:bg-gray-50">
        <div class="flex items-center justify-between">
          <div class="flex-1 min-w-0">
            <div class="flex items-center space-x-3">
              <div class="flex-shrink-0">
                <div class="h-10 w-10 rounded-full bg-green-100 flex items-center justify-center">
                  <svg class="h-6 w-6 text-green-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                </div>
              </div>
              <div class="flex-1 min-w-0">
                <p class="text-sm font-medium text-gray-900 truncate">
                  {{ scope.name }}
                </p>
                <p class="text-sm text-gray-500 truncate">
                  {{ scope.displayName || $t('scopes.noDisplayName') }}
                </p>
              </div>
            </div>
            <div v-if="scope.description" class="mt-2 text-sm text-gray-600">
              {{ scope.description }}
            </div>
            <div class="mt-1 flex items-center text-xs text-gray-500">
              <span v-if="scope.resources && scope.resources.length > 0">
                {{ $t('scopes.resources', { resources: scope.resources.join(', ') }) }}
              </span>
            </div>
          </div>
          <div class="flex space-x-2">
            <button
              v-if="canUpdate"
              @click="$emit('edit', scope)"
              class="inline-flex items-center px-3 py-2 border border-gray-300 shadow-sm text-sm leading-4 font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
            >
              <svg class="-ml-0.5 mr-2 h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
              </svg>
              {{ $t('scopes.actions.edit') }}
            </button>
            <button
              v-if="canDelete"
              @click="$emit('delete', scope.name)"
              class="inline-flex items-center px-3 py-2 border border-transparent shadow-sm text-sm leading-4 font-medium rounded-md text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
            >
              <svg class="-ml-0.5 mr-2 h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
              {{ $t('scopes.actions.delete') }}
            </button>
            <span v-if="!canUpdate && !canDelete" class="text-xs text-gray-400 italic">{{ $t('scopes.actions.noActions') }}</span>
          </div>
        </div>
      </li>
    </ul>
  </div>
</template>
