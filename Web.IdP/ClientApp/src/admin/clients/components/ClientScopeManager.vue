<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import ToggleSwitch from '@/components/common/ToggleSwitch.vue'

const { t } = useI18n()

const props = defineProps({
  modelValue: { // allowedScopes
    type: Array,
    default: () => []
  },
  requiredScopes: {
    type: Array,
    default: () => []
  }
})

const emit = defineEmits(['update:modelValue', 'update:requiredScopes'])

// Local state
const searchQuery = ref('')
const clientScopeSearchQuery = ref('') // New search for right column
const availableScopes = ref([])
const loading = ref(false)
const totalAvailable = ref(0)
const currentPage = ref(1)
const pageSize = 10 // Set to 10 per user request

// Helper to check if scope is selected
const isSelected = (scopeName) => props.modelValue.includes(scopeName)

// Helper to check if scope is required
const isRequired = (scopeName) => props.requiredScopes.includes(scopeName)

// Fetch scopes from server
const fetchScopes = async () => {
  loading.value = true
  try {
    const skip = (currentPage.value - 1) * pageSize
    const query = searchQuery.value ? `&search=${encodeURIComponent(searchQuery.value)}` : ''
    const response = await fetch(`/api/admin/scopes?skip=${skip}&take=${pageSize}${query}`)
    if (response.ok) {
      const data = await response.json()
      availableScopes.value = data.items || []
      totalAvailable.value = data.totalCount || 0
    }
  } catch (e) {
    console.error('Failed to fetch scopes', e)
  } finally {
    loading.value = false
  }
}

// Search handler with debounce
let searchTimeout
const handleSearch = () => {
  clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    currentPage.value = 1
    fetchScopes()
  }, 300)
}

// Pagination handlers
const nextPage = () => {
  if ((currentPage.value * pageSize) < totalAvailable.value) {
    currentPage.value++
    fetchScopes()
  }
}

const prevPage = () => {
  if (currentPage.value > 1) {
    currentPage.value--
    fetchScopes()
  }
}

// Actions
const addScope = (scope) => {
  const newAllowed = [...props.modelValue, scope.name]
  emit('update:modelValue', newAllowed)
}

const removeScope = (scopeName) => {
  const newAllowed = props.modelValue.filter(s => s !== scopeName)
  const newRequired = props.requiredScopes.filter(s => s !== scopeName)
  emit('update:modelValue', newAllowed)
  emit('update:requiredScopes', newRequired)
}

const toggleRequired = (scopeName) => {
  let newRequired
  if (isRequired(scopeName)) {
    newRequired = props.requiredScopes.filter(s => s !== scopeName)
  } else {
    newRequired = [...props.requiredScopes, scopeName]
  }
  emit('update:requiredScopes', newRequired)
}

// Computed for Right Column (Selected Scopes)
// We try to enrich the selected scopes with details if available in 'availableScopes'
// Otherwise fallback to just name
const selectedScopesList = computed(() => {
  const list = props.modelValue.map(name => {
    const found = availableScopes.value.find(s => s.name === name)
    return found || { name, displayName: name, description: '' }
  })
  
  // Filter by search query if present
  if (!clientScopeSearchQuery.value) return list
  
  const q = clientScopeSearchQuery.value.toLowerCase()
  return list.filter(s => 
    s.name.toLowerCase().includes(q) || 
    (s.displayName && s.displayName.toLowerCase().includes(q))
  )
})

// Initial load
onMounted(() => {
  fetchScopes()
})
</script>

<template>
  <div class="grid grid-cols-1 md:grid-cols-2 gap-4"> <!-- Removed fixed height from root to fix overlap -->
    <!-- Left Column: Available Scopes -->
    <div class="border rounded-md flex flex-col bg-white shadow-sm h-100"> <!-- Added fixed height here -->
      <div class="p-3 border-b bg-gray-50">
        <h4 class="font-medium text-gray-700 mb-2" data-test="csm-available-header">{{ t('clients.form.scopeManager.availableScopes') }}</h4>
        <div class="relative">
          <input
            type="text"
            v-model="searchQuery"
            @input="handleSearch"
            :placeholder="t('clients.form.scopeManager.searchAvailable')"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 text-sm pl-9 h-10 py-2"
            data-test="csm-available-search"
          />
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
            <svg class="h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
          </div>
        </div>
      </div>
      
      <div class="flex-1 overflow-y-auto p-2 space-y-1 relative min-h-0">
        <LoadingIndicator 
          v-if="loading" 
          :loading="loading" 
          :message="t('clients.form.scopeManager.loading')" 
          show-message 
          overlay 
        />
        
        <div v-else-if="availableScopes.length === 0" class="text-center py-4 text-gray-500 text-sm">
          {{ t('clients.form.scopeManager.noAvailableScopes') }}
        </div>

        <div
          v-else
          v-for="scope in availableScopes"
          :key="scope.name"
          class="flex items-center justify-between p-2 hover:bg-gray-50 rounded border border-transparent hover:border-gray-200 group"
          data-test="csm-available-item"
        >
          <div class="flex-1 min-w-0">
            <p class="text-sm font-medium text-gray-900 truncate" :title="scope.displayName || scope.name">
              {{ scope.displayName || scope.name }}
            </p>
            <p class="text-xs text-gray-500 truncate" :title="scope.description">
              {{ scope.description || scope.name }}
            </p>
            <div class="flex items-center mt-1 gap-1">
                 <span v-if="scope.category" class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-800">
                   {{ scope.category }}
                 </span>
            </div>
          </div>
          <button
            type="button"
            @click="addScope(scope)"
            :disabled="isSelected(scope.name)"
            class="ml-2 inline-flex items-center p-1.5 border border-transparent rounded-full shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:bg-gray-300 disabled:cursor-not-allowed"
          >
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Pagination -->
      <div class="p-2 border-t bg-gray-50 flex justify-between items-center text-xs text-gray-500">
        <button type="button" @click="prevPage" :disabled="currentPage === 1" class="px-2 py-1 rounded hover:bg-gray-200 disabled:opacity-50">
          &lt;
        </button>
        <span>{{ currentPage }} / {{ Math.ceil(totalAvailable / pageSize) || 1 }} (Total: {{ totalAvailable }})</span>
        <button type="button" @click="nextPage" :disabled="(currentPage * pageSize) >= totalAvailable" class="px-2 py-1 rounded hover:bg-gray-200 disabled:opacity-50">
          &gt;
        </button>
      </div>
    </div>

    <!-- Right Column: Selected Scopes -->
    <div class="border rounded-md flex flex-col bg-white shadow-sm h-100" data-test="csm-selected">
      <div class="p-3 border-b bg-gray-50">
        <div class="flex justify-between items-center mb-2">
          <h4 class="font-medium text-gray-700">{{ t('clients.form.scopeManager.clientScopes') }}</h4>
          <span class="text-xs text-gray-500 bg-white px-2 py-1 rounded border">
            {{ t('clients.form.scopeManager.assignedCount', { count: modelValue.length }) }}
          </span>
        </div>
        <div class="relative">
          <input
            type="text"
            v-model="clientScopeSearchQuery"
            :placeholder="t('clients.form.scopeManager.searchClient')"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 text-sm pl-9 h-10 py-2"
            data-test="csm-selected-search"
          />
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
            <svg class="h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
          </div>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto p-2 space-y-1 min-h-0">
        <div v-if="modelValue.length === 0" class="text-center py-4 text-gray-500 text-sm">
          {{ t('clients.form.scopeManager.noClientScopes') }}
        </div>
        <div v-else-if="selectedScopesList.length === 0" class="text-center py-4 text-gray-500 text-sm">
          {{ t('common.noSearchResults') || 'No matching scopes found' }}
        </div>

        <div
          v-for="scope in selectedScopesList"
          :key="scope.name"
          class="flex items-start justify-between p-2 hover:bg-gray-50 rounded border border-transparent hover:border-gray-200"
          data-test="csm-selected-item"
        >
          <div class="flex-1 min-w-0">
            <p class="text-sm font-medium text-gray-900 truncate">
              {{ scope.displayName || scope.name }}
            </p>
            <p class="text-xs text-gray-500 truncate">
               {{ scope.name }}
            </p>
          </div>
          
          <div class="flex items-center ml-4 space-x-3">
            <!-- Required Toggle Component -->
            <ToggleSwitch
              :model-value="isRequired(scope.name)"
              @update:model-value="toggleRequired(scope.name)"
              :label="t('clients.form.scopeManager.required')"
              :title="t('clients.form.scopeManager.requiredHelp')"
            />

            <!-- Remove Button -->
            <button
              type="button"
              @click="removeScope(scope.name)"
              class="text-gray-400 hover:text-red-500 focus:outline-none"
              :title="t('clients.form.scopeManager.remove')"
            >
              <svg class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>