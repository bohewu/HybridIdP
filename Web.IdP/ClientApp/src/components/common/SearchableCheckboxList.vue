<script setup>
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  modelValue: {
    type: Array, // Array of strings (values)
    default: () => []
  },
  items: {
    type: Array, // Array of objects
    default: () => []
  },
  loading: {
    type: Boolean,
    default: false
  },
  labelKey: {
    type: String,
    required: true
  },
  subLabelKey: {
    type: String,
    default: null
  },
  valueKey: {
    type: String,
    required: true
  },
  disabled: {
    type: Boolean,
    default: false
  },
  placeholder: {
    type: String,
    default: 'Search...'
  },
  heightClass: {
    type: String,
    default: 'max-h-60'
  }
})

const emit = defineEmits(['update:modelValue'])

const searchQuery = ref('')
const { t } = useI18n()

// Filter items based on search query
const filteredItems = computed(() => {
  if (!searchQuery.value) return props.items
  
  const query = searchQuery.value.toLowerCase()
  return props.items.filter(item => {
    const label = String(item[props.labelKey] || '').toLowerCase()
    const subLabel = props.subLabelKey ? String(item[props.subLabelKey] || '').toLowerCase() : ''
    return label.includes(query) || subLabel.includes(query)
  })
})

const isSelected = (value) => {
  return props.modelValue.includes(value)
}

const toggleSelection = (value) => {
  if (props.disabled) return
  
  const newValue = [...props.modelValue]
  const index = newValue.indexOf(value)
  
  if (index === -1) {
    newValue.push(value)
  } else {
    newValue.splice(index, 1)
  }
  
  emit('update:modelValue', newValue)
}

const toggleAllVisible = () => {
    if (props.disabled || filteredItems.value.length === 0) return

    // specific logic: if all visible are selected, deselect them. Otherwise select all visible.
    const visibleValues = filteredItems.value.map(i => i[props.valueKey])
    const allVisibleSelected = visibleValues.every(v => props.modelValue.includes(v))

    let newValue = [...props.modelValue]

    if (allVisibleSelected) {
        // Deselect all visible
        newValue = newValue.filter(v => !visibleValues.includes(v))
    } else {
        // Select all visible (only add ones not already present)
        const toAdd = visibleValues.filter(v => !newValue.includes(v))
        newValue = [...newValue, ...toAdd]
    }
    emit('update:modelValue', newValue)
}

const allVisibleSelected = computed(() => {
    if (filteredItems.value.length === 0) return false
    const visibleValues = filteredItems.value.map(i => i[props.valueKey])
    return visibleValues.every(v => props.modelValue.includes(v))
})

</script>

<template>
  <div class="border border-gray-300 rounded-md bg-white overflow-hidden flex flex-col">
    <!-- Search Header -->
    <div class="p-2 border-b border-gray-200 bg-gray-50 flex gap-2">
      <div class="relative flex-1">
        <input 
          v-model="searchQuery"
          type="text"
          :placeholder="placeholder"
          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 text-sm pl-8 py-1.5"
          :disabled="disabled"
        />
        <div class="absolute inset-y-0 left-0 pl-2.5 flex items-center pointer-events-none">
          <i class="bi bi-search text-gray-400 text-xs"></i>
        </div>
      </div>
      <!-- Select/Deselect All Button (Optional utility) -->
       <button 
        type="button"
        v-if="!loading && items.length > 0"
        @click="toggleAllVisible"
        :disabled="disabled"
        class="px-2 py-1 text-xs font-medium text-gray-600 bg-white border border-gray-300 rounded hover:bg-gray-100 focus:outline-none focus:ring-1 focus:ring-google-500 disabled:opacity-50"
        :title="allVisibleSelected ? 'Deselect visible' : 'Select all visible'"
      >
        <i class="bi" :class="allVisibleSelected ? 'bi-check-square-fill text-google-500' : 'bi-square'"></i>
      </button>
    </div>

    <!-- Content -->
    <div 
      class="overflow-y-auto bg-white p-1"
      :class="heightClass"
    >
      <div v-if="loading" class="flex flex-col items-center justify-center py-8 text-gray-500 text-sm">
        <svg class="animate-spin h-5 w-5 text-google-500 mb-2" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        Loading...
      </div>

      <div v-else-if="items.length === 0" class="text-center py-4 text-sm text-gray-500">
        No items available
      </div>

      <div v-else-if="filteredItems.length === 0" class="text-center py-4 text-sm text-gray-500">
        No matching results
      </div>

      <div v-else class="space-y-0.5">
        <label
          v-for="item in filteredItems"
          :key="item[valueKey]"
          class="flex items-start p-2 hover:bg-google-50 rounded cursor-pointer transition-colors"
          :class="[
            disabled ? 'opacity-50 cursor-not-allowed' : '',
            isSelected(item[valueKey]) ? 'bg-blue-50/50' : ''
          ]"
        >
          <div class="flex h-5 items-start">
            <input
              type="checkbox"
              :checked="isSelected(item[valueKey])"
              @change="toggleSelection(item[valueKey])"
              :disabled="disabled"
              class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500 mt-0.5"
            />
          </div>
          <div class="ml-3 flex-1 min-w-0">
            <div 
                class="text-sm font-medium truncate"
                :class="isSelected(item[valueKey]) ? 'text-google-700' : 'text-gray-900'"
            >
              {{ item[labelKey] }}
              <slot name="label-suffix" :item="item"></slot>
            </div>
            <div v-if="subLabelKey && item[subLabelKey]" class="text-xs text-gray-500 truncate mt-0.5">
              {{ item[subLabelKey] }}
            </div>
          </div>
        </label>
      </div>
    </div>
    
    <!-- Footer (Status) -->
    <div class="bg-gray-50 px-3 py-1.5 border-t border-gray-200 text-xs text-gray-500 flex justify-between">
       <span>{{ modelValue.length }} selected</span>
       <span>{{ filteredItems.length }} / {{ items.length }} items</span>
    </div>
  </div>
</template>
