<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import PreviewEntityPicker from './PreviewEntityPicker.vue'
import ClaimsPreviewResults from './ClaimsPreviewResults.vue'
import { claimsPreviewApi as api } from './claimsPreviewApi'

const { t, te } = useI18n()
const clients = ref([]), accounts = ref([]), scopeOptions = ref([])
const selectedClient = ref(null), selectedAccount = ref(null), scopes = ref([])
const clientSearch = ref(''), accountSearch = ref(''), clientSkip = ref(0), accountSkip = ref(0)
const clientsBusy = ref(false), accountsBusy = ref(false), scopesBusy = ref(false), previewBusy = ref(false)
const clientError = ref(''), accountError = ref(''), error = ref(''), result = ref(null)
const supported = ref(false), cleared = ref(false)
const resultView = ref(null)
const clientId = computed(() => selectedClient.value?.value || '')
const userId = computed(() => selectedAccount.value?.value || '')
const canPreview = computed(() => clientId.value && userId.value && supported.value && !scopesBusy.value && !previewBusy.value)
const usableScopes = computed(() => scopeOptions.value.filter(scope => scope.usable).map(scope => scope.name))
const clientItems = computed(() => clients.value.map(item => ({ value: item.clientId, label: item.displayName,
  detail: item.clientId, note: item.supported ? '' : t('claims-preview.unsupportedShort') })))
const accountItems = computed(() => accounts.value.map(item => ({ value: item.userId, label: item.displayName, detail: item.userId })))
const message = code => te(`claims-preview.errors.${code}`) ? t(`claims-preview.errors.${code}`) : t('claims-preview.errors.request_failed')
const readiness = computed(() => previewBusy.value ? t('claims-preview.previewing') : !clientId.value ? t('claims-preview.needClient') :
  !userId.value ? t('claims-preview.needAccount') : scopesBusy.value ? t('claims-preview.loadingScopes') :
    !supported.value ? t('claims-preview.unsupportedShort') : t('claims-preview.readyCount', { count: scopes.value.length }))
let clientRequest = 0, accountRequest = 0, scopeRequest = 0, previewRequest = 0
let clientTimer, accountTimer
function invalidate() {
  if (result.value || previewBusy.value) cleared.value = true
  previewRequest++
  previewBusy.value = false
  result.value = null
  error.value = ''
}
watch([clientId, userId, scopes], invalidate, { deep: true, flush: 'sync' })
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
  } catch (e) { if (request === scopeRequest) error.value = message(e.message) }
  finally { if (request === scopeRequest) scopesBusy.value = false }
}, { flush: 'sync' })
async function loadClients(skip = 0) {
  const request = ++clientRequest
  clientSkip.value = skip
  clientsBusy.value = true
  clientError.value = ''
  try {
    const data = await api.clients(clientSearch.value.trim(), skip)
    if (request === clientRequest) clients.value = data.items
  } catch (e) { if (request === clientRequest) { clients.value = []; clientError.value = message(e.message) } }
  finally { if (request === clientRequest) clientsBusy.value = false }
}
async function loadAccounts(skip = 0) {
  const request = ++accountRequest
  accountSkip.value = skip
  accountsBusy.value = true
  accountError.value = ''
  try {
    const data = await api.accounts(accountSearch.value.trim(), skip)
    if (request === accountRequest) accounts.value = data.items
  } catch (e) { if (request === accountRequest) { accounts.value = []; accountError.value = message(e.message) } }
  finally { if (request === accountRequest) accountsBusy.value = false }
}
watch(clientSearch, () => {
  clientRequest++
  clientsBusy.value = true
  clearTimeout(clientTimer)
  clientTimer = setTimeout(() => loadClients(), 250)
}, { flush: 'sync' })
watch(accountSearch, () => {
  accountRequest++
  accountsBusy.value = true
  clearTimeout(accountTimer)
  accountTimer = setTimeout(() => loadAccounts(), 250)
}, { flush: 'sync' })
async function preview() {
  if (!canPreview.value) return
  invalidate()
  cleared.value = false
  const request = ++previewRequest
  previewBusy.value = true
  try {
    const data = await api.preview({ clientId: clientId.value, userId: userId.value, scopes: [...scopes.value] })
    if (request !== previewRequest) return
    result.value = data
    await nextTick()
    if (request === previewRequest) {
      resultView.value?.$el?.scrollIntoView?.({ block: 'start', behavior: 'smooth' })
      resultView.value?.$el?.focus?.({ preventScroll: true })
    }
  } catch (e) { if (request === previewRequest) error.value = message(e.message) }
  finally { if (request === previewRequest) previewBusy.value = false }
}
onMounted(() => { loadClients(); loadAccounts() })
onBeforeUnmount(() => {
  clientRequest++; accountRequest++; scopeRequest++; previewRequest++
  clearTimeout(clientTimer); clearTimeout(accountTimer)
})
</script>

<template>
  <div class="px-4 py-6">
    <PageHeader :title="t('claims-preview.title')" :subtitle="t('claims-preview.subtitle')" />
    <div class="space-y-4">
      <div class="flex items-start gap-3 rounded-lg border border-blue-100 bg-blue-50/60 px-4 py-3 text-sm">
        <i class="bi bi-shield-check mt-0.5 shrink-0 text-base text-blue-600" aria-hidden="true"></i>
        <div class="min-w-0 space-y-1">
          <p class="font-medium text-blue-900">{{ t('claims-preview.readOnlySummary') }}</p>
          <details class="text-xs leading-relaxed text-blue-800">
            <summary class="w-fit cursor-pointer py-1 hover:underline">{{ t('claims-preview.boundaryDetails') }}</summary>
            <p class="mt-1 max-w-4xl">{{ t('claims-preview.boundary') }}</p>
          </details>
        </div>
      </div>
      <div v-if="error" role="alert" class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">{{ error }}</div>
      <section class="rounded-xl border border-gray-200 bg-white">
        <div class="border-b border-gray-100 px-5 py-3">
          <h2 class="text-base font-semibold text-gray-900">{{ t('claims-preview.inputs') }}</h2>
          <p class="mt-1 text-xs text-gray-500">{{ t('claims-preview.inputHelp') }}</p>
        </div>
        <div class="space-y-4 px-5 py-4">
          <div class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-2">
            <PreviewEntityPicker id="preview-client" :label="t('claims-preview.client')" :placeholder="t('claims-preview.chooseClient')"
              :search-placeholder="t('claims-preview.clientSearchPlaceholder')" :items="clientItems" :selected="selectedClient"
              :query="clientSearch" :loading="clientsBusy" :error="clientError" :skip="clientSkip" :has-next="clients.length === 50"
              @select="selectedClient = $event" @query="clientSearch = $event" @page="loadClients" @retry="loadClients(clientSkip)" />
            <PreviewEntityPicker id="preview-account" :label="t('claims-preview.account')" :placeholder="t('claims-preview.chooseAccount')"
              :search-placeholder="t('claims-preview.searchPlaceholder')" :items="accountItems" :selected="selectedAccount"
              :query="accountSearch" :loading="accountsBusy" :error="accountError" :skip="accountSkip" :has-next="accounts.length === 50"
              @select="selectedAccount = $event" @query="accountSearch = $event" @page="loadAccounts" @retry="loadAccounts(accountSkip)" />
          </div>
          <p v-if="clientId && !scopesBusy && !supported" role="status" class="rounded-md bg-amber-50 p-3 text-sm leading-relaxed text-amber-900">{{ t('claims-preview.errors.unsupported_client') }}</p>
          <fieldset :disabled="scopesBusy || !supported" class="min-w-0 border-t border-gray-100 pt-4">
            <legend class="text-sm font-medium text-gray-800">{{ t('claims-preview.scopes') }}</legend>
            <div class="mb-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
              <p class="text-xs leading-relaxed text-gray-500">{{ t('claims-preview.scopesHelp') }}</p>
              <div v-if="usableScopes.length" class="flex shrink-0 items-center gap-3 text-xs">
                <button type="button" class="min-h-8 font-medium text-google-600 hover:underline disabled:text-gray-400" @click="scopes = [...usableScopes]">{{ t('claims-preview.selectUsable') }}</button>
                <button type="button" :disabled="!scopes.length" class="min-h-8 text-gray-500 hover:text-gray-800 disabled:text-gray-300" @click="scopes = []">{{ t('claims-preview.clearScopes') }}</button>
              </div>
            </div>
            <p v-if="scopesBusy" role="status" class="py-2 text-sm text-gray-500">{{ t('claims-preview.loadingScopes') }}</p>
            <p v-else-if="!clientId" class="rounded-md bg-gray-50 px-3 py-4 text-sm text-gray-500">{{ t('claims-preview.scopesPending') }}</p>
            <div v-else class="flex flex-wrap gap-2">
              <label v-for="scope in scopeOptions" :key="scope.name" class="inline-flex min-h-10 max-w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                :class="!scope.usable ? 'cursor-not-allowed border-gray-100 bg-gray-50 text-gray-400' : scopes.includes(scope.name) ? 'cursor-pointer border-blue-200 bg-google-100 text-google-700' : 'cursor-pointer border-gray-200 text-gray-700 hover:border-gray-300'">
                <input v-model="scopes" type="checkbox" :value="scope.name" :disabled="!scope.usable" class="h-4 w-4 shrink-0 accent-blue-600" />
                <span class="break-all font-mono text-xs">{{ scope.name }}</span>
                <span v-if="!scope.usable" class="text-xs">{{ t('claims-preview.unusable') }}</span>
              </label>
              <p v-if="supported && !scopeOptions.length" class="text-sm text-gray-500">{{ t('claims-preview.noScopes') }}</p>
            </div>
          </fieldset>
        </div>
        <div class="flex flex-col-reverse gap-3 border-t border-gray-100 bg-gray-50/60 px-5 py-3 sm:flex-row sm:items-center sm:justify-between">
          <span role="status" aria-live="polite" class="text-xs text-gray-500">{{ readiness }}</span>
          <button type="button" data-test-id="claims-preview-run" :disabled="!canPreview"
            class="inline-flex min-h-10 shrink-0 items-center justify-center gap-2 rounded-md bg-google-500 px-5 text-sm font-medium text-white transition-colors hover:bg-google-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-google-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:bg-gray-200 disabled:text-gray-500"
            @click="preview"><i :class="previewBusy ? 'bi bi-arrow-repeat animate-spin' : 'bi bi-eye'" aria-hidden="true"></i>{{ previewBusy ? t('claims-preview.previewing') : t('claims-preview.preview') }}</button>
        </div>
      </section>
      <ClaimsPreviewResults v-if="result" ref="resultView" :result="result" :context="{ client: selectedClient.label, account: selectedAccount.label }" />
      <section v-else class="rounded-xl border border-dashed border-gray-300 px-5 py-7 text-center">
        <i class="bi bi-braces text-2xl text-gray-300" aria-hidden="true"></i>
        <h2 class="mt-2 text-sm font-medium text-gray-700">{{ previewBusy ? t('claims-preview.previewing') : cleared ? t('claims-preview.changedTitle') : t('claims-preview.emptyTitle') }}</h2>
        <p class="mx-auto mt-2 max-w-xl text-xs leading-relaxed text-gray-500">{{ cleared ? t('claims-preview.changedHelp') : t('claims-preview.empty') }}</p>
      </section>
    </div>
  </div>
</template>
