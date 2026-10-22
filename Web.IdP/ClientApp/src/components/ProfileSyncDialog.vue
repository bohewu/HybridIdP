<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import { profileSyncApi } from '@/services/profileSyncApi'

const props = defineProps({ accounts: { type: Array, default: () => [] }, personId: { type: String, default: '' }, canSync: Boolean })
const emit = defineEmits(['close', 'changed'])
const { t, te } = useI18n()
const loading = ref(true), busy = ref(false), enabled = ref(false), error = ref(''), disabledReason = ref('')
const linkedAccounts = ref([]), selectedAccountId = ref(''), results = ref([]), progress = ref(0), phase = ref('preview'), total = ref(0)
const accounts = computed(() => props.personId
  ? linkedAccounts.value.filter(account => account.id === selectedAccountId.value)
  : props.accounts.slice(0, 50))
let previewGeneration = 0
watch(() => JSON.stringify([props.accounts.map(account => account.id), props.personId, selectedAccountId.value, props.canSync]), () => {
  previewGeneration++
  results.value = []
}, { flush: 'sync' })
const writable = change => ['fill', 'confirm'].includes(change.action)
const ready = computed(() => results.value.filter(row => row.status === 'ready' && row.previewToken && row.changes.some(writable)))
const fillCount = computed(() => ready.value.reduce((count, row) => count + row.changes.filter(writable).length, 0))
const reasonText = reason => t(te(`profile-sync.reasons.${reason}`) ? `profile-sync.reasons.${reason}` : 'profile-sync.reasons.request_failed')
const actionText = action => t(te(`profile-sync.actions.${action}`) ? `profile-sync.actions.${action}` : 'profile-sync.actions.preserve')
const counts = computed(() => {
  const changes = results.value.flatMap(row => row.changes)
  return { fill: changes.filter(writable).length, preserve: changes.filter(change => change.action === 'preserve').length,
    protected: changes.filter(change => !writable(change) && change.action !== 'preserve').length,
    unavailable: results.value.filter(row => ['blocked', 'failed', 'needs_preview'].includes(row.status)).length }
})
async function load() {
  if (!props.canSync) { loading.value = false; return }
  loading.value = true
  error.value = ''
  try {
    const status = await profileSyncApi.status()
    enabled.value = status.enabled === true
    disabledReason.value = status.reason || 'disabled'
    if (enabled.value && props.personId) linkedAccounts.value = await profileSyncApi.accounts(props.personId)
  } catch (e) { error.value = e.message }
  finally { loading.value = false }
}
async function preview() {
  if (!props.canSync || !enabled.value || busy.value || !accounts.value.length) return
  busy.value = true
  const generation = ++previewGeneration
  phase.value = 'preview'
  progress.value = 0
  results.value = []
  total.value = accounts.value.length
  try {
    for (const account of accounts.value) {
      if (generation !== previewGeneration) break
      const row = reactive({ account, status: 'previewing', changes: [], reason: null, previewToken: null })
      results.value.push(row)
      try { Object.assign(row, await profileSyncApi.preview(account.id)) }
      catch (e) { row.status = 'failed'; row.reason = e.message }
      progress.value++
    }
  } finally { busy.value = false }
}
async function apply() {
  if (!props.canSync || !enabled.value || busy.value || !ready.value.length) return
  busy.value = true
  phase.value = 'apply'
  progress.value = 0
  const pending = [...ready.value]
  total.value = pending.length
  try {
    for (const row of pending) {
      const token = row.previewToken
      row.previewToken = null
      row.status = 'applying'
      try {
        const result = await profileSyncApi.apply(row.account.id, token)
        row.status = result.status
        if (result.changedUserFields?.length || result.changedPersonFields?.length) emit('changed')
      } catch (e) {
        row.status = ['preview_invalid', 'preview_stale'].includes(e.message) ? 'needs_preview' : 'failed'
        row.reason = e.message
      }
      progress.value++
    }
  } finally { busy.value = false }
}
onMounted(load)
</script>

<template>
  <BaseModal :show="true" :title="t('profile-sync.title')" size="xl" :show-close-icon="!busy && !loading" :close-on-esc="!busy && !loading" @close="!busy && !loading && emit('close')">
    <template #body>
      <p v-if="loading" role="status" class="text-sm text-gray-600">{{ t('profile-sync.loading') }}</p>
      <p v-else-if="!canSync" class="text-sm text-gray-600">{{ t('profile-sync.permission') }}</p>
      <div v-else-if="error" role="alert" class="space-y-3 text-sm text-red-700">
        <p>{{ reasonText(error) }}</p>
        <button type="button" class="text-google-600 underline" @click="load">{{ t('profile-sync.retry') }}</button>
      </div>
      <p v-else-if="!enabled" data-test="sync-disabled" class="text-sm text-gray-600">{{ reasonText(disabledReason) }}</p>
      <div v-else class="space-y-4">
        <p class="text-sm text-gray-600">{{ t('profile-sync.help') }}</p>
        <label v-if="personId" class="block text-sm font-medium text-gray-700">{{ t('profile-sync.chooseAccount') }}
          <select v-model="selectedAccountId" data-test="sync-account" :disabled="busy" class="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2" @change="results = []">
            <option value="">{{ t('profile-sync.chooseAccountPlaceholder') }}</option>
            <option v-for="account in linkedAccounts" :key="account.id" :value="account.id">{{ account.userName }}{{ account.email ? ` (${account.email})` : '' }}</option>
          </select>
        </label>
        <p v-if="personId && !linkedAccounts.length" class="text-sm text-gray-600">{{ t('profile-sync.noAccounts') }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <button data-test="preview-sync" type="button" :disabled="busy || !accounts.length" class="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium focus:ring-2 focus:ring-google-500 disabled:opacity-50" @click="preview">{{ t(results.length ? 'profile-sync.previewAgain' : 'profile-sync.preview') }}</button>
          <span class="text-sm text-gray-600">{{ t('profile-sync.accountCount', { count: accounts.length }) }}</span>
        </div>
        <p v-if="busy" role="status" aria-live="polite" class="text-sm text-google-700">{{ t(`profile-sync.progress.${phase}`, { count: progress, total: total }) }}</p>
        <template v-if="results.length">
          <p data-test="sync-summary" class="rounded-md bg-gray-50 p-3 text-sm text-gray-700">{{ t('profile-sync.summary', counts) }}</p>
          <div class="space-y-3">
            <details v-for="row in results" :key="row.account.id" class="rounded-md border border-gray-200 p-3">
              <summary class="cursor-pointer text-sm font-medium text-gray-900">
                {{ row.account.userName }} / {{ t(`profile-sync.status.${row.status}`) }}
                <span v-if="row.reason" class="mt-1 block font-normal text-red-700">{{ reasonText(row.reason) }}</span>
              </summary>
              <div class="mt-3 space-y-3">
                <div v-for="(change, index) in row.changes" :key="index" class="border-t border-gray-100 pt-2 text-sm">
                  <p class="font-medium text-gray-900">{{ t(`profile-sync.targets.${change.target}`) }} / {{ t(`profile-sync.fields.${change.field}`) }} / {{ actionText(change.action) }}</p>
                  <dl class="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
                    <div v-for="key in ['currentValue', 'sourceValue', 'proposedValue']" :key="key" class="min-w-0">
                      <dt class="text-gray-500">{{ t(`profile-sync.values.${key}`) }}</dt>
                      <dd class="whitespace-pre-wrap break-words text-gray-900">{{ change[key] === '' || change[key] == null ? t('profile-sync.blank') : change[key] }}</dd>
                    </div>
                  </dl>
                </div>
                <details v-if="row.providerNamespace" class="text-xs text-gray-500"><summary class="cursor-pointer">{{ t('profile-sync.sourceDetails') }}</summary><p class="mt-1 break-all">{{ row.providerNamespace }}</p></details>
              </div>
            </details>
          </div>
          <p class="text-sm text-gray-500">{{ t('profile-sync.expiry') }}</p>
          <button v-if="fillCount" data-test="apply-sync" type="button" :disabled="busy" class="rounded-md bg-google-500 px-4 py-2 text-sm font-medium text-white focus:ring-2 focus:ring-google-500 disabled:opacity-50" @click="apply">{{ t('profile-sync.apply', { count: fillCount }) }}</button>
        </template>
      </div>
    </template>
    <template #footer><button type="button" :disabled="busy || loading" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm disabled:opacity-50" @click="emit('close')">{{ t('profile-sync.close') }}</button></template>
  </BaseModal>
</template>
