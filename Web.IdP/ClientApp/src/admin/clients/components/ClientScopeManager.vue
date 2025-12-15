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
  <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
    <!-- Left Column: Available Scopes -->
    <div class="bg-white border border-gray-200 rounded-lg flex flex-col h-[600px] shadow-sm">
      <div class="p-4 border-b border-gray-100 bg-white rounded-t-lg z-10">
        <div class="flex items-center justify-between mb-3">
          <h4 class="font-normal text-lg text-gray-800" data-test="csm-available-header">{{ t('clients.form.scopeManager.availableScopes') }}</h4>
          <span class="text-xs text-gray-500 bg-gray-50 px-2 py-1 rounded-full border border-gray-100">
             Total: {{ totalAvailable }}
          </span>
        </div>
        <div class="relative">
          <input
            type="text"
            v-model="searchQuery"
            @input="handleSearch"
            :placeholder="t('clients.form.scopeManager.searchAvailable')"
            class="block w-full rounded-md border-gray-200 bg-gray-50 text-gray-900 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm pl-10 h-10 transition-colors hover:bg-white"
            data-test="csm-available-search"
          />
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
            <i class="bi bi-search text-gray-400"></i>
          </div>
        </div>
      </div>
      
      <div class="flex-1 overflow-y-auto p-2 space-y-1 relative bg-white">
        <LoadingIndicator 
          v-if="loading" 
          :loading="loading" 
          :message="t('clients.form.scopeManager.loading')" 
          show-message 
          overlay 
        />
        
        <div v-else-if="availableScopes.length === 0" class="flex flex-col items-center justify-center h-full text-gray-500 text-sm">
          <i class="bi bi-inbox text-2xl mb-2 text-gray-300"></i>
          {{ t('clients.form.scopeManager.noAvailableScopes') }}
        </div>

        <div
          v-else
          v-for="scope in availableScopes"
          :key="scope.name"
          class="flex items-center justify-between p-3 hover:bg-blue-50 rounded-md border border-transparent transition-colors group cursor-default"
          data-test="csm-available-item"
        >
          <div class="flex-1 min-w-0 pr-3">
            <div class="flex items-center gap-2">
                <p class="text-sm font-medium text-gray-900 truncate" :title="scope.displayName || scope.name">
                  {{ scope.displayName || scope.name }}
                </p>
                 <span v-if="scope.category" class="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] uppercase font-bold tracking-wider bg-gray-100 text-gray-600 border border-gray-200">
                   {{ scope.category }}
                 </span>
            </div>
            <p class="text-xs text-gray-500 truncate mt-0.5" :title="scope.description">
              {{ scope.description || scope.name }}
            </p>
          </div>
          <button
            type="button"
            @click="addScope(scope)"
            :disabled="isSelected(scope.name)"
            class="inline-flex items-center justify-center w-8 h-8 rounded-full text-blue-600 hover:bg-blue-100 focus:outline-none transition-colors disabled:opacity-30 disabled:hover:bg-transparent"
            :title="isSelected(scope.name) ? t('common.added') : t('common.add')"
          >
            <i class="bi bi-plus-lg text-lg"></i>
          </button>
        </div>
      </div>

      <!-- Pagination -->
      <div class="p-3 border-t border-gray-100 bg-gray-50 rounded-b-lg flex justify-between items-center text-xs text-gray-500">
        <button 
            type="button" 
            @click="prevPage" 
            :disabled="currentPage === 1" 
            class="w-8 h-8 flex items-center justify-center rounded-full hover:bg-gray-200 disabled:opacity-30 disabled:hover:bg-transparent transition-colors"
        >
          <i class="bi bi-chevron-left"></i>
        </button>
        <span class="font-medium text-gray-600">Page {{ currentPage }} / {{ Math.ceil(totalAvailable / pageSize) || 1 }}</span>
        <button 
            type="button" 
            @click="nextPage" 
            :disabled="(currentPage * pageSize) >= totalAvailable" 
            class="w-8 h-8 flex items-center justify-center rounded-full hover:bg-gray-200 disabled:opacity-30 disabled:hover:bg-transparent transition-colors"
        >
           <i class="bi bi-chevron-right"></i>
        </button>
      </div>
    </div>

    <!-- Right Column: Selected Scopes -->
    <div class="bg-white border border-gray-200 rounded-lg flex flex-col h-[600px] shadow-sm" data-test="csm-selected">
      <div class="p-4 border-b border-gray-100 bg-white rounded-t-lg z-10">
        <div class="flex items-center justify-between mb-3">
          <h4 class="font-normal text-lg text-gray-800">{{ t('clients.form.scopeManager.clientScopes') }}</h4>
          <span class="text-xs font-medium text-blue-600 bg-blue-50 px-2 py-1 rounded-full border border-blue-100">
            {{ t('clients.form.scopeManager.assignedCount', { count: modelValue.length }) }}
          </span>
        </div>
        <div class="relative">
          <input
            type="text"
            v-model="clientScopeSearchQuery"
            :placeholder="t('clients.form.scopeManager.searchClient')"
            class="block w-full rounded-md border-gray-200 bg-gray-50 text-gray-900 shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm pl-10 h-10 transition-colors hover:bg-white"
            data-test="csm-selected-search"
          />
          <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
             <i class="bi bi-filter text-gray-400"></i>
          </div>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto p-2 space-y-1 bg-white">
        <div v-if="modelValue.length === 0" class="flex flex-col items-center justify-center h-full text-gray-500 text-sm">
           <i class="bi bi-clipboard-x text-2xl mb-2 text-gray-300"></i>
          {{ t('clients.form.scopeManager.noClientScopes') }}
        </div>
        <div v-else-if="selectedScopesList.length === 0" class="flex flex-col items-center justify-center h-full text-gray-500 text-sm">
          {{ t('common.noSearchResults') || 'No matching scopes found' }}
        </div>

        <div
          v-else
          v-for="scope in selectedScopesList"
          :key="scope.name"
          class="flex items-start justify-between p-3 hover:bg-gray-50 rounded-md border border-transparent hover:border-gray-200 transition-colors group"
          data-test="csm-selected-item"
        >
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2">
                 <p class="text-sm font-medium text-gray-900 truncate">
                  {{ scope.displayName || scope.name }}
                </p>
                <div v-if="isRequired(scope.name)" class="flex-shrink-0">
                    <i class="bi bi-asterisk text-[8px] text-red-500 align-top" title="Required"></i>
                </div>
            </div>
           
            <p class="text-xs text-gray-500 truncate mt-0.5 font-mono">
               {{ scope.name }}
            </p>
          </div>
          
          <div class="flex items-center ml-4 gap-2 opacity-60 group-hover:opacity-100 transition-opacity">
            <!-- Required Toggle Component - Simplified Layout -->
            <div class="transform scale-90 origin-right">
                <ToggleSwitch
                :model-value="isRequired(scope.name)"
                @update:model-value="toggleRequired(scope.name)"
                :label="t('clients.form.scopeManager.required')"
                :title="t('clients.form.scopeManager.requiredHelp')"
                />
            </div>

            <!-- Remove Button -->
            <button
              type="button"
              @click="removeScope(scope.name)"
              class="w-8 h-8 flex items-center justify-center rounded-full text-gray-400 hover:text-red-600 hover:bg-red-50 focus:outline-none transition-colors"
              :title="t('clients.form.scopeManager.remove')"
            >
              <i class="bi bi-trash3"></i>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>