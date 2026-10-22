<script setup>
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import SearchInput from '@/components/common/SearchInput.vue'
import { useProfileAccountPicker } from '@/composables/useProfileAccountPicker'
import { queryConfigurationApi, queryError } from '@/services/queryConfigurationApi'

const props = defineProps({ capability: { type: String, required: true }, sourceId: { type: String, required: true }, canRead: Boolean, canUpdate: Boolean })
const emit = defineEmits(['busy'])
const { t } = useI18n()
const { search, page, items, total, loading: searching, error: searchError, load } = useProfileAccountPicker(() => props.canRead)
const searched = ref(false), account = ref(null), approval = ref(null), loading = ref(false), saving = ref(false)
const error = ref(''), conflict = ref(false), confirmed = ref(false), algorithm = ref(false), saved = ref(false)
let request = 0
const canApprove = computed(() => props.canUpdate && approval.value && confirmed.value
  && (!approval.value.requiresNationalIdAlgorithmApproval || algorithm.value) && !loading.value && !saving.value && !conflict.value)
const valueLabel = value => t(`query-fields.values.${value.replaceAll('.', '_')}`)
async function searchAccounts(reset = false) {
  if (!props.canRead || saving.value) return
  searched.value = true
  await load(reset)
}
async function selectAccount(item) {
  if (!props.canRead || saving.value) return
  account.value = { id: item.id, userName: item.userName }
  approval.value = null; confirmed.value = false; algorithm.value = false; error.value = ''; conflict.value = false; saved.value = false
  const id = ++request
  loading.value = true
  try {
    const data = await queryConfigurationApi.approval(props.capability, props.sourceId, item.id)
    if (id === request) approval.value = data
  } catch (e) { if (id === request) error.value = e.message }
  finally { if (id === request) loading.value = false }
}
async function save(approved) {
  if (!props.canUpdate || !approval.value || loading.value || saving.value || conflict.value || (approved && !canApprove.value)) return
  saving.value = true; emit('busy', true); error.value = ''; saved.value = false
  try {
    approval.value = await queryConfigurationApi.saveApproval(props.capability, props.sourceId, account.value.id, {
      expectedRevision: approval.value.revision, approved, nationalIdAlgorithmApproved: approved && algorithm.value
    })
    confirmed.value = false; algorithm.value = false; saved.value = true
  } catch (e) { error.value = e.message; conflict.value = e.message === 'conflict' }
  finally { saving.value = false; emit('busy', false) }
}
</script>

<template>
  <div class="space-y-4">
    <form class="flex flex-wrap items-end gap-2" @submit.prevent="searchAccounts(true)">
      <label class="min-w-0 flex-1 text-gray-700"><span class="mb-1 block">{{ t('query-fields.approval.search') }}</span><SearchInput v-model="search" data-test="approval-search" :placeholder="t('query-fields.approval.searchPlaceholder')" /></label>
      <button type="submit" data-test="search-approval-accounts" :disabled="!canRead || searching || saving" class="h-10 rounded-md border border-gray-300 px-4 disabled:opacity-50">{{ t('query-fields.approval.search') }}</button>
    </form>
    <p v-if="searchError" role="alert" class="text-red-700">{{ queryError(t, { message: searchError }) }}</p>
    <p v-if="searching" role="status" class="text-gray-600">{{ t('query-fields.loading') }}</p>
    <template v-else-if="searched">
      <p v-if="!items.length" class="text-gray-500">{{ t('query-fields.approval.noAccounts') }}</p>
      <ul v-else class="divide-y divide-gray-100">
        <li v-for="item in items" :key="item.id" class="py-2"><button type="button" data-test="choose-approval-account" :disabled="saving" :aria-pressed="account?.id === item.id" class="w-full rounded-md px-3 py-2 text-left break-all focus:ring-2 focus:ring-google-500" :class="account?.id === item.id ? 'bg-google-50 text-google-700' : 'text-gray-700 hover:bg-gray-50'" @click="selectAccount(item)">{{ item.userName }}</button></li>
      </ul>
      <div class="flex justify-end gap-4"><button type="button" :disabled="searching || saving || page === 0" class="disabled:opacity-50" @click="page--; searchAccounts()">{{ t('query-fields.approval.previous') }}</button><button type="button" :disabled="searching || saving || (page + 1) * 10 >= total" class="disabled:opacity-50" @click="page++; searchAccounts()">{{ t('query-fields.approval.next') }}</button></div>
    </template>
    <section v-if="account" class="space-y-3 rounded-md border border-gray-200 p-4">
      <h5 class="font-medium break-all text-gray-900">{{ account.userName }}</h5>
      <p v-if="loading" role="status">{{ t('query-fields.loading') }}</p>
      <div v-if="error" role="alert" class="space-y-2 text-red-700"><p>{{ queryError(t, { message: error }) }}</p><button type="button" data-test="reload-approval" :disabled="saving" class="underline" @click="selectAccount(account)">{{ t('query-fields.approval.reload') }}</button></div>
      <p v-if="saved" role="status" class="text-green-700">{{ t('query-fields.approval.saved') }}</p>
      <template v-if="approval && !loading">
        <p data-test="approval-status" class="text-gray-700">{{ t(approval.approved ? 'query-fields.approval.approved' : 'query-fields.approval.notApproved') }}</p>
        <p class="text-gray-600">{{ approval.valueSources.map(valueLabel).join(' / ') }}</p>
        <fieldset v-if="canUpdate" :disabled="saving || conflict" class="space-y-3">
          <label class="flex items-start gap-2"><input v-model="confirmed" data-test="approval-confirm" type="checkbox" class="mt-1" /><span>{{ t('query-fields.approval.confirm') }}</span></label>
          <template v-if="approval.requiresNationalIdAlgorithmApproval">
            <label class="flex items-start gap-2"><input v-model="algorithm" data-test="approval-algorithm" type="checkbox" class="mt-1" /><span>{{ t('query-fields.approval.algorithm') }}</span></label>
            <details class="text-gray-500"><summary class="cursor-pointer">{{ t('query-fields.approval.algorithmDetails') }}</summary><p class="mt-2">{{ t('query-fields.approval.algorithmHelp') }}</p><code class="mt-2 block break-all text-xs">SHA256(UTF8(raw.Trim().ToUpperInvariant()))</code></details>
          </template>
          <div class="flex flex-wrap gap-3"><button type="button" data-test="approve-query-account" :disabled="!canApprove" class="rounded-md bg-google-500 px-4 py-2 text-white disabled:opacity-50" @click="save(true)">{{ t('query-fields.approval.approve') }}</button><button v-if="approval.approved" type="button" data-test="revoke-query-account" :disabled="saving || conflict" class="rounded-md border border-gray-300 px-4 py-2 text-gray-700 disabled:opacity-50" @click="save(false)">{{ t('query-fields.approval.revoke') }}</button></div>
        </fieldset>
        <p v-else class="text-gray-500">{{ t('query-fields.approval.writeRequired') }}</p>
      </template>
    </section>
  </div>
</template>
