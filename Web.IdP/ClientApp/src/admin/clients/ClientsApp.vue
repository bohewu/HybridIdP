<script setup>
import { ref, onMounted } from 'vue'
import ClientList from './components/ClientList.vue'
import ClientForm from './components/ClientForm.vue'

const clients = ref([])
const loading = ref(true)
const error = ref(null)
const selectedClient = ref(null)
const showForm = ref(false)

const fetchClients = async () => {
  loading.value = true
  error.value = null
  try {
    const response = await fetch('/api/admin/clients')
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    clients.value = await response.json()
  } catch (e) {
    error.value = `Failed to load clients: ${e.message}`
    console.error('Error fetching clients:', e)
  } finally {
    loading.value = false
  }
}

const handleCreate = () => {
  selectedClient.value = null
  showForm.value = true
}

const handleEdit = (client) => {
  selectedClient.value = client
  showForm.value = true
}

const handleDelete = async (clientId) => {
  if (!confirm('Are you sure you want to delete this client?')) {
    return
  }

  try {
    const response = await fetch(`/api/admin/clients/${clientId}`, {
      method: 'DELETE'
    })
    
    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }
    
    await fetchClients()
  } catch (e) {
    error.value = `Failed to delete client: ${e.message}`
    console.error('Error deleting client:', e)
  }
}

const handleFormSubmit = async () => {
  showForm.value = false
  await fetchClients()
}

const handleFormCancel = () => {
  showForm.value = false
  selectedClient.value = null
}

onMounted(() => {
  fetchClients()
})
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <div class="max-w-7xl mx-auto py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="px-4 py-6 sm:px-0">
        <div class="flex justify-between items-center mb-6">
          <div>
            <h1 class="text-3xl font-bold text-gray-900">OIDC Client Management</h1>
            <p class="mt-2 text-sm text-gray-600">
              Manage OpenID Connect clients for your identity provider
            </p>
          </div>
          <button
            @click="handleCreate"
            class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            <svg class="-ml-1 mr-2 h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clip-rule="evenodd" />
            </svg>
            Create New Client
          </button>
        </div>

        <!-- Error Alert -->
        <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-4">
          <div class="flex">
            <div class="flex-shrink-0">
              <svg class="h-5 w-5 text-red-400" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
                <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
              </svg>
            </div>
            <div class="ml-3">
              <p class="text-sm text-red-700">{{ error }}</p>
            </div>
          </div>
        </div>

        <!-- Loading State -->
        <div v-if="loading" class="text-center py-12">
          <svg class="animate-spin h-8 w-8 text-indigo-600 mx-auto" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          <p class="mt-2 text-sm text-gray-600">Loading clients...</p>
        </div>

        <!-- Client Form Modal -->
        <ClientForm
          v-if="showForm"
          :client="selectedClient"
          @submit="handleFormSubmit"
          @cancel="handleFormCancel"
        />

        <!-- Client List -->
        <ClientList
          v-if="!loading && !showForm"
          :clients="clients"
          @edit="handleEdit"
          @delete="handleDelete"
        />
      </div>
    </div>
  </div>
</template>
