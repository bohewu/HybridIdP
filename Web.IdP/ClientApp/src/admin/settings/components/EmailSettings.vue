<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import BaseModal from '@/components/common/BaseModal.vue'

const props = defineProps({
  canUpdate: {
    type: Boolean,
    default: false
  }
})

const { t } = useI18n()

const loading = ref(true)
const saving = ref(false)
const showSuccess = ref(false)
const error = ref(null)

// Form state
const host = ref('')
const port = ref(587)
const username = ref('')
const password = ref('')
const enableSsl = ref(true)
const fromAddress = ref('')
const fromName = ref('')

// Test email state
const showTestDialog = ref(false)
const testRecipient = ref('')
const sendingTest = ref(false)
const testError = ref(null)
const testSuccess = ref(false)

// Track original values
const originals = ref({})

const hasChanges = computed(() => {
  return host.value !== originals.value.host ||
         port.value !== originals.value.port ||
         username.value !== originals.value.username ||
         password.value !== originals.value.password ||
         enableSsl.value !== originals.value.enableSsl ||
         fromAddress.value !== originals.value.fromAddress ||
         fromName.value !== originals.value.fromName
})

const loadSettings = async () => {
  loading.value = true
  error.value = null
  try {
    const response = await fetch('/api/admin/settings?prefix=Mail.', {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include'
    })

    if (!response.ok) throw new Error(`HTTP ${response.status}`)

    const settings = await response.json()
    
    const getVal = (key, def) => settings.find(s => s.key === key)?.value || def

    host.value = getVal('Mail.Host', '')
    port.value = parseInt(getVal('Mail.Port', '587'))
    username.value = getVal('Mail.Username', '')
    password.value = getVal('Mail.Password', '')
    enableSsl.value = getVal('Mail.EnableSsl', 'true') === 'true'
    fromAddress.value = getVal('Mail.FromAddress', '')
    fromName.value = getVal('Mail.FromName', '')

    originals.value = {
      host: host.value,
      port: port.value,
      username: username.value,
      password: password.value,
      enableSsl: enableSsl.value,
      fromAddress: fromAddress.value,
      fromName: fromName.value
    }
  } catch (err) {
    console.error('Failed to load email settings:', err)
    error.value = t('admin.settings.loadingError', { message: err.message })
  } finally {
    loading.value = false
  }
}

const saveSettings = async () => {
  if (!hasChanges.value || !props.canUpdate) return

  saving.value = true
  error.value = null
  showSuccess.value = false

  try {
    const updates = [
      { key: 'Mail.Host', value: host.value },
      { key: 'Mail.Port', value: port.value.toString() },
      { key: 'Mail.Username', value: username.value },
      { key: 'Mail.Password', value: password.value },
      { key: 'Mail.EnableSsl', value: enableSsl.value.toString() },
      { key: 'Mail.FromAddress', value: fromAddress.value },
      { key: 'Mail.FromName', value: fromName.value }
    ]

    // Simply update all keys to ensure consistency
    const promises = updates
      .map(u => fetch(`/api/admin/settings/${u.key}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ value: u.value, dataType: 'String' })
      }))

    const results = await Promise.all(promises)
    if (!results.every(r => r.ok)) throw new Error('Some settings failed to save')

    await fetch('/api/admin/settings/invalidate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ key: 'Mail.' }) // Invalidate prefix
    })

    originals.value = {
      host: host.value,
      port: port.value,
      username: username.value,
      password: password.value,
      enableSsl: enableSsl.value,
      fromAddress: fromAddress.value,
      fromName: fromName.value
    }

    showSuccess.value = true
    setTimeout(() => showSuccess.value = false, 3000)
  } catch (err) {
    console.error('Failed to save email settings:', err)
    error.value = t('admin.settings.saveError', { message: err.message })
  } finally {
    saving.value = false
  }
}

const cancelChanges = () => {
  if (hasChanges.value && !confirm(t('admin.settings.confirmCancel'))) return
  
  host.value = originals.value.host
  port.value = originals.value.port
  username.value = originals.value.username
  password.value = originals.value.password
  enableSsl.value = originals.value.enableSsl
  fromAddress.value = originals.value.fromAddress
  fromName.value = originals.value.fromName
}

const sendTestEmail = async () => {
  if (!testRecipient.value) return
  
  sendingTest.value = true
  testError.value = null
  testSuccess.value = false
  
  try {
    const response = await fetch('/api/admin/settings/email/test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({
        settings: {
          host: host.value,
          port: port.value,
          username: username.value,
          password: password.value,
          enableSsl: enableSsl.value,
          fromAddress: fromAddress.value,
          fromName: fromName.value
        },
        to: testRecipient.value
      })
    })
    
    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.error || 'Unknown error')
    }
    
    testSuccess.value = true
    setTimeout(() => {
        showTestDialog.value = false
        testSuccess.value = false
        testRecipient.value = ''
    }, 2000)
  } catch (err) {
    testError.value = t('admin.settings.testError', { message: err.message })
  } finally {
    sendingTest.value = false
  }
}

onMounted(loadSettings)
</script>

<template>
  <div class="bg-white shadow-sm rounded-lg border border-gray-200 mt-6">
    <div class="border-b border-gray-200 p-4 flex justify-between items-center">
      <div>
        <h2 class="text-lg font-semibold text-gray-900">{{ t('admin.settings.emailSection') }}</h2>
        <p class="mt-1 text-sm text-gray-500">{{ t('admin.settings.emailSectionDesc') }}</p>
      </div>
      <button 
        v-if="canUpdate"
        @click="showTestDialog = true"
        class="px-3 py-1.5 text-sm font-medium text-blue-700 bg-blue-50 rounded-md hover:bg-blue-100"
      >
        {{ t('admin.settings.testEmail') }}
      </button>
    </div>

    <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('admin.settings.loading')" />

    <div v-else class="p-4">
      <!-- Alerts -->
      <div v-if="error" class="mb-4 bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">{{ error }}</div>
      <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 rounded-lg p-3 text-sm text-green-700">{{ t('admin.settings.saveSuccess') }}</div>

      <!-- Form -->
      <div class="grid grid-cols-1 gap-y-6 gap-x-4 sm:grid-cols-6">
        <div class="sm:col-span-4">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.host') }}</label>
          <input v-model="host" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="smtp.example.com" />
        </div>

        <div class="sm:col-span-2">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.port') }}</label>
          <input v-model.number="port" :disabled="!canUpdate" type="number" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="587" />
        </div>

        <div class="sm:col-span-3">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.username') }}</label>
          <input v-model="username" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="user@example.com" />
        </div>

        <div class="sm:col-span-3">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.password') }}</label>
          <input v-model="password" :disabled="!canUpdate" type="password" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="••••••••" />
        </div>

        <div class="sm:col-span-6">
            <div class="flex items-center">
                <input id="enableSsl" v-model="enableSsl" :disabled="!canUpdate" type="checkbox" class="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded" />
                <label for="enableSsl" class="ml-2 block text-sm text-gray-900">{{ t('admin.settings.enableSsl') }}</label>
            </div>
        </div>

        <div class="sm:col-span-3">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.fromAddress') }}</label>
          <input v-model="fromAddress" :disabled="!canUpdate" type="email" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="no-reply@example.com" />
        </div>

        <div class="sm:col-span-3">
          <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.fromName') }}</label>
          <input v-model="fromName" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="HybridAuth IdP" />
        </div>
      </div>

      <div v-if="canUpdate" class="mt-6 flex justify-end gap-3">
        <button @click="cancelChanges" :disabled="!hasChanges || saving" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 disabled:opacity-50">
          {{ t('admin.settings.cancelButton') }}
        </button>
        <button @click="saveSettings" :disabled="!hasChanges || saving" class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md shadow-sm hover:bg-blue-700 disabled:opacity-50 flex items-center">
          <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          {{ saving ? t('admin.settings.saving') : t('admin.settings.saveButton') }}
        </button>
      </div>
    </div>

    <!-- Test Email Modal -->
    <BaseModal 
      :show="showTestDialog" 
      :title="t('admin.settings.testEmailTitle')"
      size="md"
      :show-close-icon="true"
      :close-on-backdrop="false"
      :close-on-esc="true"
      :loading="sendingTest"
      @close="showTestDialog = false"
    >
      <template #body>
        <div class="space-y-4">
          <p class="text-sm text-gray-500">{{ t('admin.settings.testEmailDesc') }}</p>
          <div>
            <label class="block text-sm font-medium text-gray-700">{{ t('admin.settings.recipient') }}</label>
            <input 
              v-model="testRecipient" 
              type="email" 
              class="mt-1 w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" 
              placeholder="user@example.com" 
            />
          </div>
          <div v-if="testError" class="text-sm text-red-600 bg-red-50 p-2 rounded">{{ testError }}</div>
          <div v-if="testSuccess" class="text-sm text-green-600 bg-green-50 p-2 rounded">{{ t('admin.settings.testSuccess') }}</div>
        </div>
      </template>

      <template #footer>
        <button 
          @click="sendTestEmail" 
          :disabled="!testRecipient || sendingTest" 
          class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-blue-600 text-base font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50"
        >
          {{ sendingTest ? t('admin.settings.sending') : t('admin.settings.send') }}
        </button>
        <button 
          @click="showTestDialog = false" 
          class="mt-2.5 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm"
        >
          {{ t('common.cancel') }}
        </button>
      </template>
    </BaseModal>
  </div>
</template>
