<script setup>
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'

const { t } = useI18n()

const props = defineProps({
  person: {
    type: Object,
    required: true
  },
  show: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['close', 'transferred'])

const loading = ref(false)
const error = ref(null)
const searchTerm = ref('')
const targetPersons = ref([])
const selectedTargetId = ref(null)
const searching = ref(false)

const handleSearch = async () => {
  if (!searchTerm.value || searchTerm.value.length < 1) return
  
  searching.value = true
  error.value = null
  targetPersons.value = []
  
  try {
    const response = await fetch(`/api/admin/people/search?term=${encodeURIComponent(searchTerm.value)}&take=10`)
    if (!response.ok) throw new Error('Search failed')
    const data = await response.json()
    // Filter out the source person
    targetPersons.value = data.persons.filter(p => p.id !== props.person.id)
  } catch (e) {
    error.value = e.message
  } finally {
    searching.value = false
  }
}

const handleTransfer = async () => {
  if (!selectedTargetId.value) return
  
  if (!confirm(t('persons.transferAssets.confirmMessage', 'Are you sure you want to transfer ALL assets (APIs, Scopes, Clients) to this person? This action cannot be undone.'))) {
    return
  }

  loading.value = true
  error.value = null

  try {
    const response = await fetch(`/api/admin/people/${props.person.id}/transfer-assets`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ targetPersonId: selectedTargetId.value })
    })

    if (!response.ok) {
        const txt = await response.text();
        throw new Error(txt || 'Transfer failed');
    }

    alert(t('persons.transferAssets.success', 'Assets transferred successfully.'))
    emit('transferred')
    emit('close')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

const reset = () => {
    searchTerm.value = ''
    targetPersons.value = []
    selectedTargetId.value = null
    error.value = null
}

watch(() => props.show, (newVal) => {
    if (newVal) reset()
})

</script>

<template>
  <BaseModal
    :show="show"
    :title="t('persons.transferAssets.title', 'Transfer Assets')"
    size="md"
    :show-close-icon="true"
    :close-on-backdrop="false"
    :close-on-esc="true"
    @close="$emit('close')"
  >
    <template #body>
      <div class="space-y-4">
        <p class="text-sm text-gray-500">
            {{ t('persons.transferAssets.description', 'Select a person to transfer all owned resources to.') }}
        </p>
        
        <div class="bg-yellow-50 border-l-4 border-yellow-400 p-4">
            <div class="flex">
                <div class="ml-3">
                    <p class="text-sm text-yellow-700">
                        {{ t('persons.transferAssets.warning', 'Warning: This will transfer ownership of all Scopes, API Resources, and Clients created by the current person.') }}
                    </p>
                </div>
            </div>
        </div>

        <!-- Search -->
        <div>
            <label class="block text-sm font-medium text-gray-700">{{ t('persons.search', 'Search Person') }}</label>
            <div class="mt-1 flex rounded-md shadow-sm">
                <input 
                    v-model="searchTerm" 
                    type="text" 
                    class="block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm px-3 py-2 border"
                    :placeholder="t('persons.searchPlaceholder', 'Name or Employee ID')"
                    @keyup.enter="handleSearch"
                >
            </div>
            <button 
                @click="handleSearch"
                type="button" 
                class="mt-2 inline-flex items-center px-3 py-2 border border-gray-300 shadow-sm text-sm leading-4 font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-google-500 focus:ring-offset-2"
                :disabled="searching"
            >
                {{ searching ? 'Searching...' : t('common.search', 'Search') }}
            </button>
        </div>

        <!-- Results -->
        <div v-if="targetPersons.length > 0" class="max-h-60 overflow-y-auto border rounded-md mt-2">
            <ul class="divide-y divide-gray-200">
                <li 
                    v-for="p in targetPersons" 
                    :key="p.id" 
                    @click="selectedTargetId = p.id"
                    :class="['px-4 py-3 cursor-pointer hover:bg-gray-50', selectedTargetId === p.id ? 'bg-blue-50 border-l-4 border-blue-500' : '']"
                >
                    <div class="flex items-center justify-between">
                        <div>
                            <p class="text-sm font-medium text-gray-900">{{ p.firstName }} {{ p.lastName }}</p>
                            <p class="text-sm text-gray-500">{{ p.employeeId }}</p>
                        </div>
                         <div v-if="selectedTargetId === p.id" class="text-blue-600">
                            <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                                <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd" />
                            </svg>
                        </div>
                    </div>
                </li>
            </ul>
        </div>
        <div v-else-if="!searching && searchTerm && targetPersons.length === 0" class="text-sm text-gray-500 mt-2">
            {{ t('persons.noPersons', 'No persons found.') }}
        </div>
        
        <div v-if="error" class="text-red-600 text-sm mt-2">{{ error }}</div>
      </div>
    </template>
    <template #footer>
        <button @click="$emit('close')" class="mr-3 inline-flex justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-google-500 focus:ring-offset-2">
            {{ t('common.cancel', 'Cancel') }}
        </button>
        <button 
            @click="handleTransfer" 
            :disabled="!selectedTargetId || loading"
            class="inline-flex justify-center rounded-md border border-transparent bg-red-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
            <svg v-if="loading" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            {{ t('persons.transferAssets.verify', 'Transfer Assets') }}
        </button>
    </template>
  </BaseModal>
</template>
