<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import QueryConfigurationDialog from './QueryConfigurationDialog.vue'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import permissionService, { Permissions } from '@/utils/permissionService'
import { queryConfigurationApi, queryError, querySourceStatus, queryMappingSupported } from '@/services/queryConfigurationApi'

defineProps({ canUpdate: Boolean })
const { t } = useI18n()
const state = ref(null), loading = ref(true), error = ref(''), showQueries = ref(false)
const initialCapability = ref('native-profile'), initialSourceId = ref('')
function openQueries(entry) {
  const selected = entry?.configuration || entry?.source
  initialCapability.value = selected?.capability || 'native-profile'
  initialSourceId.value = selected?.sourceId || ''
  showQueries.value = true
}
const canReadUsers = computed(() => permissionService.hasPermission(Permissions.Users.Read))
const canUpdateUsers = computed(() => permissionService.hasPermission(Permissions.Users.Update))
const entries = computed(() => {
  if (!state.value) return []
  const configurations = state.value.configurations || []
  const sources = state.value.sources || []
  return [
    ...configurations.map(configuration => ({ configuration, source: sources.find(source => source.capability === configuration.capability && source.sourceId === configuration.sourceId && source.providerNamespace === configuration.providerNamespace) })),
    ...sources.filter(source => !configurations.some(configuration => configuration.capability === source.capability && configuration.sourceId === source.sourceId && configuration.providerNamespace === source.providerNamespace)).map(source => ({ source }))
  ]
})
async function load() {
  loading.value = true; error.value = ''
  try { state.value = await queryConfigurationApi.get() }
  catch (e) { error.value = queryError(t, e) }
  finally { loading.value = false }
}
onMounted(load)
</script>

<template>
  <section data-test="external-data-sources" class="rounded-lg border border-gray-200 bg-white shadow-sm">
    <div class="border-b border-gray-200 p-4"><h2 class="text-lg font-semibold text-gray-900">{{ t('query-fields.sectionTitle') }}</h2></div>
    <LoadingPlaceholder v-if="loading" :message="t('query-fields.loading')" />
    <div v-else class="space-y-4 p-4">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <p class="max-w-3xl text-sm text-gray-600">{{ t('query-fields.entryHelp') }}</p>
        <button data-test="open-query-fields" type="button" class="min-h-10 rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-google-600 focus:ring-2 focus:ring-google-500" @click="openQueries()">{{ t('query-fields.manage') }}</button>
      </div>
      <p v-if="error" role="alert" class="rounded-md bg-red-50 p-3 text-sm text-red-700">{{ error }}</p>
      <div v-if="entries.length" class="source-summary">
        <table data-test="query-configuration-summary" class="w-full text-left text-sm">
          <caption class="pb-2 text-left font-medium text-gray-900">{{ t('query-fields.current') }}</caption>
          <thead class="border-b border-gray-200 text-gray-600"><tr><th scope="col" class="px-3 py-2">{{ t('query-fields.capability') }}</th><th scope="col" class="px-3 py-2">{{ t('query-fields.source') }}</th><th scope="col" class="px-3 py-2">{{ t('query-fields.inputs') }}</th><th scope="col" class="px-3 py-2">{{ t('query-fields.status') }}</th><th scope="col" class="px-3 py-2">{{ t('query-fields.actions') }}</th></tr></thead>
          <tbody class="divide-y divide-gray-100">
            <tr v-for="(entry, index) in entries" :key="index" data-test="query-summary-row" :data-capability="(entry.configuration || entry.source).capability" :data-source-id="(entry.configuration || entry.source).sourceId" class="align-top">
              <th scope="row" class="px-3 py-3 font-medium text-gray-900">{{ t(`query-fields.capabilities.${(entry.configuration || entry.source).capability}`) }}</th>
              <td class="break-words px-3 py-3 text-gray-700">{{ (entry.configuration || entry.source).sourceId }}<span v-if="entry.source" class="mt-1 block text-xs text-gray-500">{{ t(`query-fields.sourceTypes.${entry.source.sourceType === 'directory' ? 'directory' : 'http'}`) }}</span></td>
              <td class="px-3 py-3 text-gray-600">
                <template v-if="entry.configuration?.wireMode === 'neutral-v2'"><p v-for="mapping in entry.configuration.mappings" :key="mapping.attribute">{{ t(`query-fields.attributes.${mapping.attribute}`) }} ← {{ t(`query-fields.values.${mapping.valueSource.replaceAll('.', '_')}`) }}</p></template>
                <span v-else>{{ t(entry.configuration ? 'query-fields.legacy' : 'query-fields.notConfigured') }}</span>
              </td>
              <td class="px-3 py-3" :class="entry.source?.available ? 'text-gray-600' : 'text-amber-800'">{{ entry.configuration?.wireMode === 'legacy' ? t('query-fields.legacyStatus') : querySourceStatus(t, entry.source) }}<p v-if="entry.configuration?.wireMode === 'neutral-v2' && !queryMappingSupported(state.sources, entry.configuration)" class="mt-1">{{ t('query-fields.blocked') }}</p></td>
              <td class="whitespace-nowrap px-3 py-3"><button type="button" data-test="edit-query-summary" class="min-h-10 rounded-md border border-gray-300 px-3 py-2 text-google-600 focus:ring-2 focus:ring-google-500" @click="openQueries(entry)">{{ t(canUpdate ? 'query-fields.edit' : 'query-fields.view') }}</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-else-if="!error" class="text-sm text-gray-500">{{ t('query-fields.empty') }}</p>
      <button v-if="error" type="button" class="min-h-10 text-sm text-google-600 underline" @click="load">{{ t('query-fields.refresh') }}</button>
      <p class="text-sm text-gray-500">{{ t('query-fields.saveHelp') }}</p>
    </div>
    <QueryConfigurationDialog v-if="showQueries" :initial-capability="initialCapability" :initial-source-id="initialSourceId" :can-update="canUpdate" :can-read-users="canReadUsers" :can-update-users="canUpdateUsers" @close="showQueries = false" @saved="load" />
  </section>
</template>

<style scoped>
@media (max-width: 767px) {
  .source-summary table, .source-summary tbody { display: block; }
  .source-summary caption { display: block; }
  .source-summary thead { display: none; }
  .source-summary tr { display: grid; grid-template-columns: minmax(0, 1fr); border: 1px solid #e5e7eb; border-radius: .5rem; margin-bottom: 1rem; padding: .75rem; overflow-wrap: anywhere; }
  .source-summary th, .source-summary td { display: block; padding: .375rem; white-space: normal; }
  .source-summary td:last-child { margin-top: .25rem; }
}
</style>
