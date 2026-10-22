<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import QueryAccountApproval from './QueryAccountApproval.vue'
import { queryConfigurationApi, queryError, querySourceStatus, queryMappingSupported } from '@/services/queryConfigurationApi'

const props = defineProps({ canUpdate: Boolean, canReadUsers: Boolean, canUpdateUsers: Boolean, initialCapability: { type: String, default: 'native-profile' }, initialSourceId: { type: String, default: '' } })
const emit = defineEmits(['close', 'saved'])
const { t } = useI18n()
const capabilities = ['native-profile', 'profile', 'metadata', 'lifecycle-status', 'recovery-identity-verification', 'password-sync']
const capability = ref(capabilities.includes(props.initialCapability) ? props.initialCapability : capabilities[0]), state = ref(null), rows = ref([]), neutral = ref([])
const loading = ref(true), saving = ref(false), error = ref(''), saved = ref(false), conflict = ref(false), showApproval = ref(false)
const approvalBusy = ref(false), profileSelection = ref(props.initialSourceId)
const busy = computed(() => saving.value || approvalBusy.value)
const clone = value => JSON.parse(JSON.stringify(value))
const selectedRows = computed(() => rows.value.filter(row => row.capability === capability.value))
const row = computed(() => profileSelection.value
  ? selectedRows.value.find(item => item.sourceId === profileSelection.value)
  : selectedRows.value.length === 1 ? selectedRows.value[0] : null)
const isNeutral = computed(() => capability.value === 'profile' && row.value ? row.value.wireMode === 'neutral-v2' : neutral.value.includes(capability.value))
const sources = computed(() => state.value?.sources.filter(source => source.capability === capability.value) || [])
const sourceIndex = computed(() => row.value ? sources.value.findIndex(source => source.sourceId === row.value.sourceId && source.providerNamespace === row.value.providerNamespace) : -1)
const source = computed(() => sources.value[sourceIndex.value])
const sets = computed(() => source.value?.acceptedAttributeSets || [])
const setKey = attributes => [...attributes].sort().join('|')
const selectedSet = computed(() => setKey(row.value?.mappings.map(mapping => mapping.attribute) || []))
const valueSources = attribute => source.value?.availableCandidates.find(candidate => candidate.attribute === attribute)?.valueSources || []
const attributeLabel = attribute => t(`query-fields.attributes.${attribute}`)
const valueLabel = value => t(`query-fields.values.${value.replaceAll('.', '_')}`)
const unchanged = item => state.value?.configurations.some(original => JSON.stringify(original) === JSON.stringify(item))
const supported = item => queryMappingSupported(state.value?.sources, item)

const dirty = computed(() => state.value && JSON.stringify(rows.value) !== JSON.stringify(state.value.configurations))
const valid = computed(() => neutral.value.every(key => rows.value.some(item => item.capability === key && item.wireMode === 'neutral-v2'))
  && rows.value.every(item => unchanged(item) || item.wireMode === 'legacy' || supported(item)))
const approvalAvailable = computed(() => props.canReadUsers && !dirty.value && row.value?.wireMode === 'neutral-v2' && supported(row.value)
  && row.value.mappings.some(mapping => !mapping.valueSource.startsWith('runtime.')))
function accept(data) {
  state.value = data
  rows.value = clone(data.configurations)
  neutral.value = [...new Set(data.configurations.filter(item => item.wireMode === 'neutral-v2').map(item => item.capability))]
  conflict.value = false
}
async function load() {
  loading.value = true; error.value = ''; saved.value = false; showApproval.value = false
  try { accept(await queryConfigurationApi.get()) }
  catch (e) { error.value = e.message }
  finally { loading.value = false }
}
function modeChanged(event) {
  if (!props.canUpdate || busy.value || conflict.value) return
  if (capability.value === 'profile' && row.value) {
    row.value.wireMode = event.target.value
    row.value.mappings = []
    neutral.value = neutral.value.filter(key => key !== 'profile')
    if (selectedRows.value.some(item => item.wireMode === 'neutral-v2')) neutral.value.push('profile')
    saved.value = false; showApproval.value = false
    return
  }
  rows.value = rows.value.filter(item => item.capability !== capability.value)
  neutral.value = neutral.value.filter(key => key !== capability.value)
  if (event.target.value === 'neutral-v2') neutral.value.push(capability.value)
  saved.value = false; showApproval.value = false
}
function sourceChanged(event) {
  const selected = sources.value[Number(event.target.value)]
  if (busy.value || conflict.value) return
  if (capability.value === 'profile' && selected && selectedRows.value.some(item => item.sourceId === selected.sourceId && item.providerNamespace === selected.providerNamespace)) {
    profileSelection.value = selected.sourceId; showApproval.value = false; return
  }
  if (!props.canUpdate || !selected?.available) return
  rows.value = rows.value.filter(item => item.capability !== capability.value || (capability.value === 'profile' && item.sourceId !== selected.sourceId))
  rows.value.push({ capability: capability.value, sourceId: selected.sourceId, wireMode: 'neutral-v2', providerNamespace: selected.providerNamespace, mappings: [], queryRule: selected.queryRule ?? null })
  profileSelection.value = selected.sourceId
  saved.value = false; showApproval.value = false
}
function setChanged(event) {
  if (!row.value || !props.canUpdate || busy.value || conflict.value || !source.value?.available) return
  const attributes = sets.value.find(set => setKey(set) === event.target.value)
  if (!attributes) return
  row.value.mappings = attributes.map(attribute => ({ attribute, valueSource: row.value.mappings.find(mapping => mapping.attribute === attribute)?.valueSource || '' }))
  saved.value = false
}
function removeProfileSource() {
  if (!props.canUpdate || busy.value || conflict.value || capability.value !== 'profile' || !row.value) return
  rows.value = rows.value.filter(item => item !== row.value)
  profileSelection.value = ''; showApproval.value = false; saved.value = false
  if (!selectedRows.value.some(item => item.wireMode === 'neutral-v2')) neutral.value = neutral.value.filter(key => key !== 'profile')
}
async function save() {
  if (!props.canUpdate || busy.value || conflict.value || !valid.value || !dirty.value) return
  saving.value = true; error.value = ''; saved.value = false; showApproval.value = false
  try { accept(await queryConfigurationApi.save({ expectedRevision: state.value.revision, configurations: clone(rows.value) })); saved.value = true; emit('saved') }
  catch (e) { error.value = e.message; conflict.value = e.message === 'conflict' }
  finally { saving.value = false }
}
onMounted(load)
</script>

<template>
  <BaseModal :show="true" :title="t('query-fields.title')" size="3xl" :show-close-icon="!busy" :close-on-esc="!busy" @close="!busy && emit('close')">
    <template #body>
      <div class="space-y-5 text-sm">
        <p class="text-gray-600">{{ t('query-fields.help') }}</p>
        <p v-if="loading" role="status">{{ t('query-fields.loading') }}</p>
        <div v-if="error" role="alert" class="space-y-2 rounded-md bg-red-50 p-3 text-red-700">
          <p>{{ queryError(t, { message: error }) }}</p>
          <p v-if="conflict">{{ t('query-fields.draftKept') }}</p>
          <button type="button" data-test="reload-query" :disabled="busy" class="underline" @click="load">{{ t('query-fields.reload') }}</button>
        </div>
        <p v-if="saved && !dirty" role="status" class="text-green-700">{{ t('query-fields.saved') }}</p>
        <template v-if="state && !loading">
          <p v-if="!canUpdate" class="text-gray-600">{{ t('query-fields.readOnly') }}</p>
          <label class="block font-medium text-gray-700">{{ t('query-fields.capability') }}
            <select v-model="capability" data-test="query-capability" :disabled="busy" class="query-control mt-1" @change="profileSelection = ''; showApproval = false"><option v-for="key in capabilities" :key="key" :value="key">{{ t(`query-fields.capabilities.${key}`) }}</option></select>
          </label>
          <p class="text-gray-600">{{ t(`query-fields.authority.${capability}`) }}</p>
          <div v-if="capability === 'profile' && selectedRows.length" class="flex flex-wrap gap-2">
            <button v-for="item in selectedRows" :key="item.sourceId" type="button" data-test="select-profile-query" :disabled="busy" :aria-pressed="row?.sourceId === item.sourceId" class="rounded-md border border-gray-300 px-3 py-2 text-google-600" @click="profileSelection = item.sourceId; showApproval = false">{{ item.sourceId }}</button>
          </div>
          <ul v-if="sources.some(item => !item.available)" data-test="query-source-reasons" class="space-y-2 rounded-md bg-amber-50 p-3 text-amber-800">
            <li v-for="item in sources.filter(item => !item.available)" :key="`${item.sourceId}/${item.providerNamespace}`"><span class="font-medium">{{ item.sourceId }}</span>: {{ querySourceStatus(t, item) }}</li>
          </ul>
          <fieldset :disabled="!canUpdate || busy || conflict" class="min-w-0 space-y-4">
            <label class="block text-gray-700">{{ t('query-fields.mode') }}
              <select data-test="query-mode" :value="isNeutral ? 'neutral-v2' : 'legacy'" class="query-control mt-1" @change="modeChanged"><option value="legacy">{{ t('query-fields.legacy') }}</option><option value="neutral-v2">{{ t('query-fields.neutral') }}</option></select>
            </label>
            <p v-if="!isNeutral" class="text-gray-500">{{ t('query-fields.legacyHelp') }}</p>
            <template v-else>
              <p v-if="selectedRows.length > 1 && capability !== 'profile'" role="status" class="text-amber-700">{{ t('query-fields.multipleSources') }}</p>
              <label class="block text-gray-700">{{ t('query-fields.source') }}
                <select data-test="query-source" :value="sourceIndex" class="query-control mt-1" @change="sourceChanged"><option :value="-1" disabled>{{ t('query-fields.chooseSource') }}</option><option v-for="(item, index) in sources" :key="index" :value="index" :disabled="!item.available">{{ item.sourceId }}{{ item.available ? '' : ` (${querySourceStatus(t, item)})` }}</option></select>
              </label>
              <p v-if="capability === 'profile'" class="text-gray-500">{{ t('query-fields.profileSourcesHelp') }}</p>
              <p v-if="!sources.some(item => item.available)" class="text-gray-500">{{ t('query-fields.noSources') }}</p>
              <template v-if="row">
                <p v-if="!supported(row) && unchanged(row)" role="status" class="text-amber-700">{{ t('query-fields.blocked') }}</p>
                <label class="block text-gray-700">{{ t('query-fields.attributesLabel') }}
                  <select data-test="query-attributes" :value="selectedSet" :disabled="!source?.available" class="query-control mt-1" @change="setChanged"><option value="" disabled>{{ t('query-fields.chooseAttributes') }}</option><option v-if="selectedSet && !sets.some(set => setKey(set) === selectedSet)" :value="selectedSet" disabled>{{ row.mappings.map(mapping => attributeLabel(mapping.attribute)).join(' + ') }} ({{ t('query-fields.unavailable') }})</option><option v-for="set in sets" :key="setKey(set)" :value="setKey(set)">{{ set.map(attributeLabel).join(' + ') }}</option></select>
                </label>
                <div v-for="mapping in row.mappings" :key="mapping.attribute" class="grid min-w-0 items-center gap-2 sm:grid-cols-[1fr_2fr]">
                  <span class="text-gray-700">{{ attributeLabel(mapping.attribute) }}</span>
                  <label class="min-w-0"><span class="sr-only">{{ t('query-fields.valueFor', { field: attributeLabel(mapping.attribute) }) }}</span><select v-model="mapping.valueSource" :data-test="`query-value-${mapping.attribute}`" :disabled="!source?.available" class="query-control"><option value="" disabled>{{ t('query-fields.chooseValue') }}</option><option v-if="mapping.valueSource && !valueSources(mapping.attribute).includes(mapping.valueSource)" :value="mapping.valueSource" disabled>{{ valueLabel(mapping.valueSource) }} ({{ t('query-fields.unavailable') }})</option><option v-for="value in valueSources(mapping.attribute)" :key="value" :value="value">{{ valueLabel(value) }}</option></select></label>
                </div>
              </template>
            </template>
          </fieldset>
          <button v-if="canUpdate && capability === 'profile' && row" type="button" data-test="remove-profile-query" :disabled="busy || conflict" class="text-red-700 disabled:opacity-50" @click="removeProfileSource">{{ t('query-fields.removeSource') }}</button>
          <details v-if="selectedRows.length" class="border-t border-gray-100 pt-3 text-gray-500">
            <summary class="cursor-pointer">{{ t('query-fields.details') }}</summary>
            <dl v-for="item in selectedRows" :key="item.sourceId" class="mt-3 grid min-w-0 gap-2 break-all sm:grid-cols-[1fr_2fr]"><dt>{{ t('query-fields.source') }}</dt><dd>{{ item.sourceId }}</dd><dt>{{ t('query-fields.namespace') }}</dt><dd>{{ item.providerNamespace }}</dd><template v-if="item.queryRule"><dt>{{ t('query-fields.rule') }}</dt><dd>{{ item.queryRule }}</dd></template></dl>
          </details>
          <section v-if="isNeutral" class="space-y-3 border-t border-gray-200 pt-4">
            <h4 class="font-medium text-gray-900">{{ t('query-fields.approval.title') }}</h4>
            <p class="text-gray-600">{{ t('query-fields.approval.help') }}</p>
            <p v-if="dirty" class="text-gray-500">{{ t('query-fields.approval.saveFirst') }}</p>
            <p v-else-if="!canReadUsers" class="text-gray-500">{{ t('query-fields.approval.readRequired') }}</p>
            <button v-if="!showApproval" type="button" data-test="open-query-approval" :disabled="!approvalAvailable || busy || conflict" class="rounded-md border border-gray-300 px-4 py-2 text-google-600 disabled:opacity-50" @click="showApproval = true">{{ t('query-fields.approval.chooseAccount') }}</button>
            <QueryAccountApproval v-if="showApproval && approvalAvailable" :key="`${capability}/${row.sourceId}`" :capability="capability" :source-id="row.sourceId" :can-read="canReadUsers" :can-update="canUpdate && canUpdateUsers" @busy="approvalBusy = $event" />
          </section>
        </template>
      </div>
    </template>
    <template #footer><div class="w-full space-y-3"><p class="text-sm text-gray-500">{{ t('query-fields.saveHelp') }}</p><div class="flex flex-wrap justify-end gap-2"><button type="button" :disabled="busy" class="rounded-md border border-gray-300 px-4 py-2 text-sm" @click="emit('close')">{{ t('query-fields.close') }}</button><button v-if="canUpdate" type="button" data-test="save-query" :disabled="loading || busy || !dirty || !valid || conflict" class="rounded-md bg-google-500 px-4 py-2 text-sm text-white disabled:opacity-50" @click="save">{{ t(saving ? 'query-fields.saving' : 'query-fields.save') }}</button></div></div></template>
  </BaseModal>
</template>

<style scoped>
.query-control { width: 100%; min-width: 0; height: 2.5rem; border: 1px solid #d1d5db; border-radius: .375rem; padding: .5rem .75rem; font-size: .875rem; background-color: white; }
.query-control:focus { outline: 2px solid var(--color-google-500, #4285f4); outline-offset: 1px; }
.query-control:disabled, fieldset:disabled .query-control { background-color: #f9fafb; color: #6b7280; }
</style>
