<script setup>
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import { claimsPreviewApi as api } from './claimsPreviewApi'

const { t, te } = useI18n()
const clients = ref([]), accounts = ref([]), scopeOptions = ref([])
const clientId = ref(''), userId = ref(''), scopes = ref([]), search = ref('')
const clientSkip = ref(0), accountSkip = ref(0), supported = ref(false)
const clientsBusy = ref(false), accountsBusy = ref(false), scopesBusy = ref(false), previewBusy = ref(false)
const error = ref(''), result = ref(null), destination = ref('idToken')
let clientRequest = 0, accountRequest = 0, scopeRequest = 0, previewRequest = 0
const busy = computed(() => clientsBusy.value || accountsBusy.value || scopesBusy.value || previewBusy.value)
const canPreview = computed(() => clientId.value && userId.value && supported.value && !busy.value)
const destinations = ['idToken', 'accessToken', 'userInfo']
const reasons = computed(() => result.value?.reasons.filter(reason => reason.destination === destination.value) || [])
const json = computed(() => JSON.stringify(result.value?.[destination.value] || {}, null, 2))
const message = (group, code) => te(`claims-preview.${group}.${code}`) ? t(`claims-preview.${group}.${code}`) : t('claims-preview.unknownCode', { code })
function invalidate() {
  previewRequest++
  previewBusy.value = false
  result.value = null
  error.value = ''
}
watch([clientId, userId, scopes], invalidate, { deep: true, flush: 'sync' })
watch(search, () => {
  accountRequest++
  accountsBusy.value = false
  userId.value = ''
  accounts.value = []
  accountSkip.value = 0
  invalidate()
}, { flush: 'sync' })
watch(clientId, async value => {
  const request = ++scopeRequest
  scopes.value = []
  scopeOptions.value = []
  supported.value = false
  scopesBusy.value = Boolean(value)
  if (!value) return
  try {
    const data = await api.scopes(value)
    if (request !== scopeRequest) return
    supported.value = data.supported
    scopeOptions.value = data.scopes
  } catch (e) {
    if (request === scopeRequest) error.value = message('errors', e.message)
  } finally {
    if (request === scopeRequest) scopesBusy.value = false
  }
}, { flush: 'sync' })
async function loadClients(skip = 0) {
  const request = ++clientRequest
  clientId.value = ''
  clients.value = []
  clientsBusy.value = true
  error.value = ''
  try {
    const data = await api.clients(skip)
    if (request !== clientRequest) return
    clients.value = data.items
    clientSkip.value = skip
  } catch (e) {
    if (request === clientRequest) error.value = message('errors', e.message)
  } finally {
    if (request === clientRequest) clientsBusy.value = false
  }
}
async function loadAccounts(skip = 0) {
  const request = ++accountRequest
  userId.value = ''
  accounts.value = []
  accountsBusy.value = true
  error.value = ''
  try {
    const data = await api.accounts(search.value, skip)
    if (request !== accountRequest) return
    accounts.value = data.items
    accountSkip.value = skip
  } catch (e) {
    if (request === accountRequest) error.value = message('errors', e.message)
  } finally {
    if (request === accountRequest) accountsBusy.value = false
  }
}
async function preview() {
  if (!canPreview.value) return
  invalidate()
  const request = ++previewRequest
  previewBusy.value = true
  try {
    const data = await api.preview({ clientId: clientId.value, userId: userId.value, scopes: [...scopes.value] })
    if (request === previewRequest) result.value = data
  } catch (e) {
    if (request === previewRequest) error.value = message('errors', e.message)
  } finally {
    if (request === previewRequest) previewBusy.value = false
  }
}
onMounted(() => { loadClients(); loadAccounts() })
onBeforeUnmount(() => { clientRequest++; accountRequest++; scopeRequest++; previewRequest++ })
</script>

<template>
  <div class="px-4 py-6 space-y-5">
    <PageHeader :title="t('claims-preview.title')" :subtitle="t('claims-preview.subtitle')" />
    <p class="border-l-4 border-blue-400 bg-blue-50 p-4 text-sm text-blue-900 leading-relaxed">{{ t('claims-preview.boundary') }}</p>
    <div v-if="error" role="alert" class="border-l-4 border-red-400 bg-red-50 p-4 text-sm text-red-800">{{ error }}</div>
    <section class="rounded-lg border border-gray-200 bg-white p-5 space-y-5" :aria-busy="busy">
      <h2 class="text-lg font-semibold text-gray-900">{{ t('claims-preview.inputs') }}</h2>
      <form class="min-w-0 space-y-2" @submit.prevent="loadAccounts()">
        <label for="preview-search" class="block text-sm font-medium text-gray-800">{{ t('claims-preview.searchLabel') }}</label>
        <div class="flex min-w-0 gap-2">
          <input id="preview-search" v-model="search" :placeholder="t('claims-preview.searchPlaceholder')" maxlength="200" type="search" class="min-w-0 flex-1 h-10 border border-gray-300 rounded-md px-3 focus:ring-2 focus:ring-google-500" />
          <button type="submit" :disabled="accountsBusy" class="inline-flex items-center justify-center shrink-0 whitespace-nowrap min-w-20 px-4 h-10 rounded-md border border-gray-300 text-sm font-medium focus:ring-2 focus:ring-google-500 disabled:opacity-50">{{ t('claims-preview.search') }}</button>
        </div>
      </form>
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div class="space-y-3 min-w-0">
          <label for="preview-client" class="block text-sm font-medium text-gray-800">{{ t('claims-preview.client') }}</label>
          <select id="preview-client" v-model="clientId" :disabled="clientsBusy" class="w-full h-10 border border-gray-300 rounded-md px-3 focus:ring-2 focus:ring-google-500">
            <option value="">{{ t('claims-preview.chooseClient') }}</option>
            <option v-for="client in clients" :key="client.clientId" :value="client.clientId">{{ client.displayName }} ({{ client.clientId }})</option>
          </select>
          <div class="flex flex-wrap items-center gap-3 text-sm">
            <button type="button" class="text-google-600 disabled:text-gray-400 min-h-10" :disabled="clientsBusy || clientSkip === 0" @click="loadClients(Math.max(0, clientSkip - 50))">{{ t('claims-preview.previous') }}</button>
            <span class="text-gray-600">{{ t('claims-preview.page', { page: clientSkip / 50 + 1 }) }}</span>
            <button type="button" class="text-google-600 disabled:text-gray-400 min-h-10" :disabled="clientsBusy || clients.length < 50" @click="loadClients(clientSkip + 50)">{{ t('claims-preview.next') }}</button>
            <button v-if="!clients.length && !clientsBusy" type="button" class="text-google-600 min-h-10" @click="loadClients()">{{ t('claims-preview.reloadClients') }}</button>
          </div>
          <p v-if="clientId && !scopesBusy && !supported" role="status" class="text-sm text-amber-900 bg-amber-50 p-3">{{ t('claims-preview.errors.unsupported_client') }}</p>
        </div>
        <div class="space-y-3 min-w-0">
          <label for="preview-account" class="block text-sm font-medium text-gray-800">{{ t('claims-preview.account') }}</label>
          <select id="preview-account" v-model="userId" :disabled="accountsBusy" class="w-full h-10 border border-gray-300 rounded-md px-3 focus:ring-2 focus:ring-google-500">
            <option value="">{{ t('claims-preview.chooseAccount') }}</option>
            <option v-for="account in accounts" :key="account.userId" :value="account.userId">{{ account.displayName }} ({{ account.userId }})</option>
          </select>
          <div class="flex flex-wrap items-center gap-3 text-sm">
            <button type="button" class="text-google-600 disabled:text-gray-400 min-h-10" :disabled="accountsBusy || accountSkip === 0" @click="loadAccounts(Math.max(0, accountSkip - 50))">{{ t('claims-preview.previous') }}</button>
            <span class="text-gray-600">{{ t('claims-preview.page', { page: accountSkip / 50 + 1 }) }}</span>
            <button type="button" class="text-google-600 disabled:text-gray-400 min-h-10" :disabled="accountsBusy || accounts.length < 50" @click="loadAccounts(accountSkip + 50)">{{ t('claims-preview.next') }}</button>
            <span v-if="!accounts.length && !accountsBusy" class="text-gray-600">{{ t('claims-preview.noAccounts') }}</span>
          </div>
        </div>
      </div>
      <fieldset :disabled="scopesBusy || !supported" class="border-t border-gray-200 pt-4">
        <legend class="text-sm font-medium text-gray-800">{{ t('claims-preview.scopes') }}</legend>
        <p class="text-sm text-gray-600 mb-3">{{ t('claims-preview.scopesHelp') }}</p>
        <div class="flex flex-wrap gap-x-6 gap-y-2">
          <label v-for="scope in scopeOptions" :key="scope.name" class="inline-flex items-center gap-2 min-h-10 text-sm break-all">
            <input v-model="scopes" type="checkbox" :value="scope.name" :disabled="!scope.usable" class="h-4 w-4" />
            {{ scope.name }} <span v-if="!scope.usable" class="text-gray-600">({{ t('claims-preview.unusable') }})</span>
          </label>
          <p v-if="clientId && supported && !scopeOptions.length" class="text-sm text-gray-600">{{ t('claims-preview.noScopes') }}</p>
        </div>
      </fieldset>
      <div class="flex flex-wrap items-center gap-4 border-t border-gray-200 pt-4">
        <button type="button" data-test-id="claims-preview-run" :disabled="!canPreview" class="h-10 px-5 bg-google-500 hover:bg-google-600 text-white font-medium text-sm rounded-md focus:ring-2 focus:ring-offset-2 focus:ring-google-500 disabled:opacity-50 disabled:cursor-not-allowed" @click="preview">{{ t('claims-preview.preview') }}</button>
        <span role="status" aria-live="polite" class="text-sm text-gray-600">{{ busy ? t('claims-preview.loading') : t('claims-preview.ready') }}</span>
      </div>
    </section>
    <section v-if="result" data-test-id="claims-preview-result" class="rounded-lg border border-gray-200 bg-white min-w-0">
      <div class="p-5 border-b border-gray-200">
        <h2 class="text-lg font-semibold text-gray-900">{{ t('claims-preview.result') }}</h2>
        <p class="text-sm text-gray-600 mt-1">{{ t('claims-preview.evidence') }}</p>
      </div>
      <div role="tablist" :aria-label="t('claims-preview.destinations')" class="flex overflow-x-auto border-b border-gray-200 px-3 sm:px-5 gap-1 sm:gap-2">
        <button v-for="item in destinations" :id="`tab-${item}`" :key="item" type="button" role="tab" :aria-selected="destination === item" :aria-controls="'preview-payload'" class="inline-flex items-center justify-center shrink-0 whitespace-nowrap h-12 px-3 sm:px-4 border-b-2 text-sm font-medium hover:bg-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-google-500" :class="destination === item ? 'border-google-500 text-google-600' : 'border-transparent text-gray-600'" @click="destination = item">{{ t(`claims-preview.destinationsLabels.${item}`) }}</button>
      </div>
      <div id="preview-payload" role="tabpanel" :aria-labelledby="`tab-${destination}`" class="p-5 space-y-5">
        <pre data-test-id="claims-preview-json" class="overflow-x-auto rounded-md bg-gray-900 text-gray-100 p-4 text-sm leading-relaxed" tabindex="0">{{ json }}</pre>
        <h3 class="font-semibold text-gray-900">{{ t('claims-preview.reasons') }}</h3>
        <div class="overflow-x-auto">
          <table class="w-full text-sm text-left">
            <thead class="bg-gray-50 text-gray-700"><tr><th scope="col" class="p-3">{{ t('claims-preview.claim') }}</th><th scope="col" class="p-3">{{ t('claims-preview.status') }}</th><th scope="col" class="p-3">{{ t('claims-preview.reason') }}</th></tr></thead>
            <tbody class="divide-y divide-gray-200"><tr v-for="(reason, index) in reasons" :key="index"><td class="p-3 font-mono break-all">{{ reason.claimType }}</td><td class="p-3">{{ message('statuses', reason.status) }}</td><td class="p-3">{{ message('codes', reason.code) }}</td></tr></tbody>
          </table>
          <p v-if="!reasons.length" class="text-sm text-gray-600 p-3">{{ t('claims-preview.noReasons') }}</p>
        </div>
      </div>
      <div class="border-t border-gray-200 p-5 space-y-3">
        <h3 class="font-semibold text-gray-900">{{ t('claims-preview.sources') }}</h3>
        <p class="text-sm text-gray-600">{{ t('claims-preview.sourcesHelp') }}</p>
        <p v-if="!result.sources.length" class="text-sm text-gray-600">{{ t('claims-preview.noSources') }}</p>
        <dl v-for="source in result.sources" :key="source.source" class="text-sm border-l-2 border-gray-300 pl-4 space-y-1 break-words">
          <dt class="font-medium text-gray-900">{{ source.source }}: {{ message('sourceStatuses', source.status) }}</dt>
          <dd class="text-gray-600">{{ t('claims-preview.confirmedAt') }}: {{ source.confirmedAt || t('claims-preview.unknown') }}</dd>
        </dl>
      </div>
    </section>
    <p v-else class="text-sm text-gray-600">{{ t('claims-preview.empty') }}</p>
  </div>
</template>
