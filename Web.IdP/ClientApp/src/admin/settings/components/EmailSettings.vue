<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import BaseModal from '@/components/common/BaseModal.vue'
import { SettingKeys } from '@/utils/settingKeys'

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
const testSettings = ref({
  host: '',
  port: 587,
  username: '',
  password: '',
  enableSsl: true,
  fromAddress: '',
  fromName: ''
})
const sendingTest = ref(false)
const testError = ref(null)
const testSuccess = ref(false)
const testValidationVisible = ref(false)

// Track original values
const originals = ref({})

const hasChanges = computed(() => {
  return host.value !== (originals.value.host?.value || '') ||
         port.value !== parseInt(originals.value.port?.value || '587') ||
         username.value !== (originals.value.username?.value || '') ||
         password.value !== (originals.value.password?.value || '') ||
         enableSsl.value !== ((originals.value.enableSsl?.value || 'true') === 'true') ||
         fromAddress.value !== (originals.value.fromAddress?.value || '') ||
         fromName.value !== (originals.value.fromName?.value || '')
})

const getSourceDisplay = (key) => {
  const metadata = originals.value[key]
  if (!metadata) return ''
  return metadata.isOverridden ? t('settings.sourceDb') : t('settings.sourceConfig')
}

const isOverriding = computed(() => {
  // Check if any field that was originally NOT overridden is now changed
  const currentValues = {
    host: host.value,
    port: port.value.toString(),
    username: username.value,
    password: password.value,
    enableSsl: enableSsl.value.toString(),
    fromAddress: fromAddress.value,
    fromName: fromName.value
  }

  return Object.entries(currentValues).some(([field, currentValue]) => {
    const meta = originals.value[field]
    return !meta?.isOverridden && currentValue !== meta?.value
  })
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
    
    const getMeta = (key, defValue) => settings.find(s => s.key === key) || { value: defValue, isOverridden: false, source: 'Configuration' }

    const hostMeta = getMeta(SettingKeys.Email.SmtpHost, '')
    const portMeta = getMeta(SettingKeys.Email.SmtpPort, '587')
    const usernameMeta = getMeta(SettingKeys.Email.SmtpUsername, '')
    const passwordMeta = getMeta(SettingKeys.Email.SmtpPassword, '')
    const enableSslMeta = getMeta(SettingKeys.Email.SmtpEnableSsl, 'true')
    const fromAddressMeta = getMeta(SettingKeys.Email.FromAddress, '')
    const fromNameMeta = getMeta(SettingKeys.Email.FromName, '')

    host.value = hostMeta.value
    port.value = parseInt(portMeta.value)
    username.value = usernameMeta.value
    password.value = passwordMeta.value
    enableSsl.value = enableSslMeta.value === 'true'
    fromAddress.value = fromAddressMeta.value
    fromName.value = fromNameMeta.value

    originals.value = {
      host: hostMeta,
      port: portMeta,
      username: usernameMeta,
      password: passwordMeta,
      enableSsl: enableSslMeta,
      fromAddress: fromAddressMeta,
      fromName: fromNameMeta
    }
  } catch (err) {
    console.error('Failed to load email settings:', err)
    error.value = t('settings.loadingError', { message: err.message })
  } finally {
    loading.value = false
  }
}

const isEmailValid = (email) => {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
}

const testErrors = computed(() => {
  const errs = {}
  const testPort = Number(testSettings.value.port)

  if (!testSettings.value.host?.trim()) errs.host = t('settings.validation.required')
  if (!Number.isInteger(testPort) || testPort < 1 || testPort > 65535) {
    errs.port = t('settings.validation.invalidPort')
  }
  if (testSettings.value.username?.trim() && !testSettings.value.password) {
    errs.password = t('settings.validation.passwordRequiredForUsername')
  }
  if (testSettings.value.password && !testSettings.value.username?.trim()) {
    errs.username = t('settings.validation.usernameRequiredForPassword')
  }
  if (!testSettings.value.fromAddress?.trim()) {
    errs.fromAddress = t('settings.validation.required')
  } else if (!isEmailValid(testSettings.value.fromAddress)) {
    errs.fromAddress = t('settings.validation.invalidEmail')
  }
  if (!testSettings.value.fromName?.trim()) errs.fromName = t('settings.validation.required')
  if (!testRecipient.value?.trim()) {
    errs.recipient = t('settings.validation.required')
  } else if (!isEmailValid(testRecipient.value)) {
    errs.recipient = t('settings.validation.invalidEmail')
  }

  return errs
})

const isDevServer = computed(() => {
  const normalizedHost = host.value?.trim().toLowerCase()
  return normalizedHost === 'localhost' || normalizedHost === '127.0.0.1'
})

const isTestValid = computed(() => Object.keys(testErrors.value).length === 0)

const errors = computed(() => {
  const errs = {}
  if (!host.value?.trim()) errs.host = t('settings.validation.required')
  if (!port.value) errs.port = t('settings.validation.required')
  if (!fromAddress.value?.trim()) {
    errs.fromAddress = t('settings.validation.required')
  } else if (!isEmailValid(fromAddress.value)) {
    errs.fromAddress = t('settings.validation.invalidEmail')
  }
  if (!fromName.value?.trim()) errs.fromName = t('settings.validation.required')
  return errs
})

const isValid = computed(() => Object.keys(errors.value).length === 0)

const saveSettings = async () => {
  if (!hasChanges.value || !props.canUpdate || !isValid.value) return

  if (isOverriding.value && !confirm(t('settings.confirmOverride'))) return

  saving.value = true
  error.value = null
  showSuccess.value = false

  try {
    const updates = [
      { key: SettingKeys.Email.SmtpHost, value: host.value },
      { key: SettingKeys.Email.SmtpPort, value: port.value.toString() },
      { key: SettingKeys.Email.SmtpUsername, value: username.value },
      { key: SettingKeys.Email.SmtpPassword, value: password.value },
      { key: SettingKeys.Email.SmtpEnableSsl, value: enableSsl.value.toString() },
      { key: SettingKeys.Email.FromAddress, value: fromAddress.value },
      { key: SettingKeys.Email.FromName, value: fromName.value }
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
      host: { ...originals.value.host, value: host.value, isOverridden: true },
      port: { ...originals.value.port, value: port.value.toString(), isOverridden: true },
      username: { ...originals.value.username, value: username.value, isOverridden: true },
      password: { ...originals.value.password, value: password.value, isOverridden: true },
      enableSsl: { ...originals.value.enableSsl, value: enableSsl.value.toString(), isOverridden: true },
      fromAddress: { ...originals.value.fromAddress, value: fromAddress.value, isOverridden: true },
      fromName: { ...originals.value.fromName, value: fromName.value, isOverridden: true }
    }

    showSuccess.value = true
    setTimeout(() => showSuccess.value = false, 3000)
  } catch (err) {
    console.error('Failed to save email settings:', err)
    error.value = t('settings.saveError', { message: err.message })
  } finally {
    saving.value = false
  }
}

const cancelChanges = () => {
  if (hasChanges.value && !confirm(t('settings.confirmCancel'))) return
  
  host.value = originals.value.host?.value || ''
  port.value = parseInt(originals.value.port?.value || '587')
  username.value = originals.value.username?.value || ''
  password.value = originals.value.password?.value || ''
  enableSsl.value = (originals.value.enableSsl?.value || 'true') === 'true'
  fromAddress.value = originals.value.fromAddress?.value || ''
  fromName.value = originals.value.fromName?.value || ''
}

const openTestDialog = () => {
  testSettings.value = {
    host: host.value,
    port: port.value,
    username: username.value,
    password: '',
    enableSsl: enableSsl.value,
    fromAddress: fromAddress.value,
    fromName: fromName.value
  }
  testRecipient.value = ''
  testError.value = null
  testSuccess.value = false
  testValidationVisible.value = false
  showTestDialog.value = true
}

const closeTestDialog = () => {
  if (sendingTest.value) return
  showTestDialog.value = false
  testError.value = null
  testSuccess.value = false
  testValidationVisible.value = false
}

const resolveTestError = (data) => {
  if (data?.code === 'smtp_rejected') {
    return t('settings.testErrors.rejected', {
      status: data.smtpStatusCode || t('settings.testErrors.unknownStatus')
    })
  }
  if (data?.code === 'smtp_not_configured') {
    return t('settings.testErrors.notConfigured')
  }
  if (data?.code === 'smtp_delivery_failed') {
    return t('settings.testErrors.deliveryFailed')
  }
  return data?.error || t('settings.testErrors.unknown')
}

const sendTestEmail = async () => {
  testValidationVisible.value = true
  if (!isTestValid.value) return
  
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
          host: testSettings.value.host,
          port: Number(testSettings.value.port),
          username: testSettings.value.username,
          password: testSettings.value.password,
          enableSsl: testSettings.value.enableSsl,
          fromAddress: testSettings.value.fromAddress,
          fromName: testSettings.value.fromName
        },
        to: testRecipient.value.trim()
      })
    })
    
    if (!response.ok) {
      const data = await response.json().catch(() => ({}))
      throw new Error(resolveTestError(data))
    }
    
    testSuccess.value = true
    setTimeout(() => {
      closeTestDialog()
      testRecipient.value = ''
    }, 2000)
  } catch (err) {
    testError.value = t('settings.testError', { message: err.message })
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
        <h2 class="text-lg font-semibold text-gray-900">{{ t('settings.emailSection') }}</h2>
        <p class="mt-1 text-sm text-gray-500">{{ t('settings.emailSectionDesc') }}</p>
      </div>
      <button 
        v-if="canUpdate"
        type="button"
        data-testid="open-email-test"
        @click="openTestDialog"
        class="px-3 py-1.5 text-sm font-medium text-blue-700 bg-blue-50 rounded-md hover:bg-blue-100"
      >
        {{ t('settings.testEmail') }}
      </button>
    </div>

    <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('settings.loading')" />

    <div v-else class="p-4">
      <!-- Alerts -->
      <div v-if="isDevServer" class="mb-4 bg-yellow-50 border border-yellow-200 rounded-lg p-3 flex items-start">
        <svg class="h-5 w-5 text-yellow-400 mt-0.5 mr-2" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
          <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd" />
        </svg>
        <span class="text-sm text-yellow-700">{{ t('settings.usingDevServer') }} (Mailpit)</span>
      </div>
      
      <div v-if="error" class="mb-4 bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">{{ error }}</div>

      <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 rounded-lg p-3 text-sm text-green-700">{{ t('settings.saveSuccess') }}</div>

      <!-- Form -->
      <div class="grid grid-cols-1 gap-y-6 gap-x-4 sm:grid-cols-6">
        <div class="sm:col-span-4">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.host') }}</label>
            <span :class="originals.host?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('host') }}
            </span>
          </div>
          <input v-model="host" data-testid="email-settings-host" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="smtp.example.com" />
        </div>

        <div class="sm:col-span-2">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.port') }}</label>
            <span :class="originals.port?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('port') }}
            </span>
          </div>
          <input v-model.number="port" :disabled="!canUpdate" type="number" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="587" />
        </div>

        <div class="sm:col-span-3">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.username') }}</label>
            <span :class="originals.username?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('username') }}
            </span>
          </div>
          <input v-model="username" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="user@example.com" />
        </div>

        <div class="sm:col-span-3">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.password') }}</label>
            <span :class="originals.password?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('password') }}
            </span>
          </div>
          <input v-model="password" :disabled="!canUpdate" type="password" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" :placeholder="password === '(set)' ? t('settings.maskedPasswordHint') : '••••••••'" />
        </div>

        <div class="sm:col-span-6">
            <div class="flex items-center justify-between">
              <div class="flex items-center">
                <input id="enableSsl" v-model="enableSsl" :disabled="!canUpdate" type="checkbox" class="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded" />
                <label for="enableSsl" class="ml-2 block text-sm text-gray-900">{{ t('settings.enableSsl') }}</label>
              </div>
              <span :class="originals.enableSsl?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
                {{ getSourceDisplay('enableSsl') }}
              </span>
            </div>
        </div>

        <div class="sm:col-span-3">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.fromAddress') }}</label>
            <span :class="originals.fromAddress?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('fromAddress') }}
            </span>
          </div>
          <input v-model="fromAddress" :disabled="!canUpdate" type="email" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="no-reply@example.com" />
        </div>

        <div class="sm:col-span-3">
          <div class="flex justify-between items-center mb-1">
            <label class="block text-sm font-medium text-gray-700">{{ t('settings.fromName') }}</label>
            <span :class="originals.fromName?.isOverridden ? 'text-blue-600 bg-blue-50' : 'text-gray-500 bg-gray-100'" class="text-[10px] uppercase font-bold px-1.5 rounded border border-current opacity-70">
              {{ getSourceDisplay('fromName') }}
            </span>
          </div>
          <input v-model="fromName" :disabled="!canUpdate" type="text" class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed" placeholder="HybridAuth IdP" />
        </div>
      </div>

      <div v-if="canUpdate" class="mt-6 flex justify-end gap-3">
        <button @click="cancelChanges" :disabled="!hasChanges || saving" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 disabled:opacity-50">
          {{ t('settings.cancelButton') }}
        </button>
        <button @click="saveSettings" :disabled="!hasChanges || saving" class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md shadow-sm hover:bg-blue-700 disabled:opacity-50 flex items-center">
          <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          {{ saving ? t('settings.saving') : t('settings.saveButton') }}
        </button>
      </div>
    </div>

    <!-- Test Email Modal -->
    <BaseModal 
      :show="showTestDialog" 
      :title="t('settings.testEmailTitle')"
      size="lg"
      :show-close-icon="true"
      :close-on-backdrop="false"
      :close-on-esc="true"
      :loading="sendingTest"
      @close="closeTestDialog"
    >
      <template #body>
        <div class="space-y-5">
          <div class="rounded-md border border-blue-100 bg-blue-50 px-3 py-2 text-sm text-blue-800">
            {{ t('settings.testEmailDesc') }}
          </div>

          <fieldset class="space-y-4">
            <legend class="text-sm font-semibold text-gray-900">{{ t('settings.testConnectionSettings') }}</legend>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-6">
              <div class="sm:col-span-4">
                <label for="test-smtp-host" class="block text-sm font-medium text-gray-700">{{ t('settings.host') }}</label>
                <input
                  id="test-smtp-host"
                  v-model="testSettings.host"
                  data-testid="test-smtp-host"
                  type="text"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.host)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="smtp.example.com"
                />
                <p v-if="testValidationVisible && testErrors.host" class="mt-1 text-xs text-red-600">{{ testErrors.host }}</p>
              </div>

              <div class="sm:col-span-2">
                <label for="test-smtp-port" class="block text-sm font-medium text-gray-700">{{ t('settings.port') }}</label>
                <input
                  id="test-smtp-port"
                  v-model.number="testSettings.port"
                  data-testid="test-smtp-port"
                  type="number"
                  min="1"
                  max="65535"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.port)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                />
                <p v-if="testValidationVisible && testErrors.port" class="mt-1 text-xs text-red-600">{{ testErrors.port }}</p>
              </div>

              <div class="sm:col-span-3">
                <label for="test-smtp-username" class="block text-sm font-medium text-gray-700">{{ t('settings.username') }}</label>
                <input
                  id="test-smtp-username"
                  v-model="testSettings.username"
                  data-testid="test-smtp-username"
                  type="text"
                  autocomplete="off"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.username)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="user@example.com"
                />
                <p v-if="testValidationVisible && testErrors.username" class="mt-1 text-xs text-red-600">{{ testErrors.username }}</p>
              </div>

              <div class="sm:col-span-3">
                <label for="test-smtp-password" class="block text-sm font-medium text-gray-700">{{ t('settings.password') }}</label>
                <input
                  id="test-smtp-password"
                  v-model="testSettings.password"
                  data-testid="test-smtp-password"
                  type="password"
                  autocomplete="new-password"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.password)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="••••••••"
                />
                <p v-if="testValidationVisible && testErrors.password" class="mt-1 text-xs text-red-600">{{ testErrors.password }}</p>
                <p v-else class="mt-1 text-xs text-gray-500">{{ t('settings.testPasswordHelp') }}</p>
              </div>

              <div class="sm:col-span-6">
                <label class="inline-flex items-center gap-2 text-sm text-gray-900">
                  <input
                    v-model="testSettings.enableSsl"
                    data-testid="test-smtp-enable-ssl"
                    type="checkbox"
                    :disabled="sendingTest"
                    class="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                  />
                  {{ t('settings.enableSsl') }}
                </label>
              </div>
            </div>
          </fieldset>

          <fieldset class="space-y-4 border-t border-gray-200 pt-4">
            <legend class="text-sm font-semibold text-gray-900">{{ t('settings.testMessageSettings') }}</legend>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-6">
              <div class="sm:col-span-3">
                <label for="test-from-address" class="block text-sm font-medium text-gray-700">{{ t('settings.fromAddress') }}</label>
                <input
                  id="test-from-address"
                  v-model="testSettings.fromAddress"
                  data-testid="test-from-address"
                  type="email"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.fromAddress)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="no-reply@example.com"
                />
                <p v-if="testValidationVisible && testErrors.fromAddress" class="mt-1 text-xs text-red-600">{{ testErrors.fromAddress }}</p>
              </div>

              <div class="sm:col-span-3">
                <label for="test-from-name" class="block text-sm font-medium text-gray-700">{{ t('settings.fromName') }}</label>
                <input
                  id="test-from-name"
                  v-model="testSettings.fromName"
                  data-testid="test-from-name"
                  type="text"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.fromName)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="HybridAuth IdP"
                />
                <p v-if="testValidationVisible && testErrors.fromName" class="mt-1 text-xs text-red-600">{{ testErrors.fromName }}</p>
              </div>

              <div class="sm:col-span-6">
                <label for="test-recipient" class="block text-sm font-medium text-gray-700">{{ t('settings.recipient') }}</label>
                <input
                  id="test-recipient"
                  v-model="testRecipient"
                  data-testid="test-recipient"
                  type="email"
                  :disabled="sendingTest"
                  :aria-invalid="testValidationVisible && Boolean(testErrors.recipient)"
                  class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
                  placeholder="user@example.com"
                />
                <p v-if="testValidationVisible && testErrors.recipient" class="mt-1 text-xs text-red-600">{{ testErrors.recipient }}</p>
              </div>
            </div>
          </fieldset>

          <div v-if="testError" role="alert" class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{{ testError }}</div>
          <div v-if="testSuccess" role="status" class="rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-700">{{ t('settings.testSuccess') }}</div>
        </div>
      </template>

      <template #footer>
        <button 
          @click="sendTestEmail" 
          data-testid="send-email-test"
          :disabled="sendingTest"
          class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-blue-600 text-base font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 sm:ml-3 sm:w-auto sm:text-sm disabled:opacity-50"
        >
          {{ sendingTest ? t('settings.sending') : t('settings.send') }}
        </button>
        <button 
          type="button"
          @click="closeTestDialog"
          data-testid="cancel-email-test"
          :disabled="sendingTest"
          class="mt-2.5 w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-google-500 sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm"
        >
          {{ t('common.cancel') }}
        </button>
      </template>
    </BaseModal>
  </div>
</template>
