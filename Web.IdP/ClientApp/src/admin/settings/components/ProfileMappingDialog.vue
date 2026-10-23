<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import { useProfileMappings } from '@/composables/useProfileMappings'
import { policyError } from '@/services/nativeFieldPolicyApi'
const props = defineProps({ canUpdate: Boolean, canSync: Boolean })
const emit = defineEmits(['close', 'saved', 'sync'])
const { t } = useI18n()
const { rows, fallback, userNameSources, changeUserNamePolicy, loading, saving, error, conflict, editing, editDraft, querySources, availableSources, properties, available, fieldsFor, allMapped, load, add, edit, complete, remove, save } = useProfileMappings(() => props.canUpdate)
const sourceLabel = source => {
  const catalog = source.source === 'NeutralQuery' && querySources.value.filter(item => item.providerNamespace === source.providerNamespace)
  return catalog?.length ? catalog.map(item => `${item.sourceId} (${t(`query-fields.sourceTypes.${item.sourceType === 'directory' ? 'directory' : 'http'}`)})`).join(', ') : t(`native-fields.sources.${source.source}`)
}
const sourceIndex = ref('')
const draft = reactive({ field: '', sourceProperty: '' })
const selectedSource = computed(() => availableSources.value[Number(sourceIndex.value)])
const addFields = computed(() => sourceIndex.value === '' ? [] : fieldsFor(selectedSource.value))
const canAdd = computed(() => sourceIndex.value !== '' && draft.field && draft.sourceProperty && editing.value === -1 && !saving.value)
watch(sourceIndex, () => { draft.field = ''; draft.sourceProperty = '' })
function addRow() { if (!canAdd.value) return; add({ ...draft, source: selectedSource.value.source, providerNamespace: selectedSource.value.providerNamespace }); draft.field = ''; draft.sourceProperty = '' }
function changeEditSource(event) {
  const source = availableSources.value[Number(event.target.value)]
  Object.assign(editDraft.value, { source: source.source, providerNamespace: source.providerNamespace, sourceProperty: '' })
}
const editSourceIndex = computed(() => availableSources.value.findIndex(s => s.source === editDraft.value?.source && s.providerNamespace === editDraft.value?.providerNamespace))
const editValid = computed(() => editDraft.value && available(editDraft.value) && properties.value.includes(editDraft.value.sourceProperty) && fieldsFor(editDraft.value, editing.value).includes(editDraft.value.field))
const supportsFallback = row => ['Proof', 'NeutralQuery'].includes(row.source) && row.field === 'FirstName' && row.sourceProperty === 'givenName' && available(row)
function changeFallback(row, event) {
  if (!props.canUpdate || saving.value || editing.value !== -1 || !supportsFallback(row)) return
  fallback.value = { providerNamespace: row.providerNamespace, enabled: event.target.checked }
}
async function submit(now) { if (now && !props.canSync) return; if (await save()) { emit('saved'); emit(now ? 'sync' : 'close') } }
onMounted(load)
</script>
<template>
  <BaseModal :show="true" :title="t('native-fields.mapping.title')" size="3xl" :show-close-icon="!saving" :close-on-esc="!saving" @close="!saving && emit('close')">
    <template #body>
      <p class="mb-5 text-sm text-gray-600">{{ t('native-fields.mapping.help') }}</p>
      <p v-if="loading" role="status" class="text-sm text-gray-600">{{ t('native-fields.loading') }}</p>
      <template v-else>
        <div v-if="error" role="alert" class="mb-4 space-y-2 rounded-md bg-red-50 p-3 text-sm text-red-700"><p>{{ policyError(t, { message: error }) }}</p><p v-if="conflict">{{ t('native-fields.mapping.draftKept') }}</p><button type="button" :disabled="saving" class="underline" @click="load">{{ t('native-fields.mapping.reloadDiscard') }}</button></div>
        <p v-if="!canUpdate" class="mb-4 text-sm text-gray-600">{{ t('native-fields.mapping.readOnly') }}</p>
        <div class="mb-3 flex items-center justify-between gap-2"><h4 class="text-sm font-medium text-gray-900">{{ t('native-fields.mapping.current') }}</h4><span class="text-xs text-gray-500">{{ t('native-fields.mapping.count', { count: rows.length }) }}</span></div>
        <div class="mapping-table min-w-0 text-sm" role="table" :aria-label="t('native-fields.mapping.current')">
          <div class="mapping-row hidden bg-gray-50 text-xs text-gray-500 sm:grid" role="row"><span role="columnheader">{{ t('native-fields.mapping.field') }}</span><span role="columnheader">{{ t('native-fields.source') }}</span><span role="columnheader">{{ t('native-fields.mapping.property') }}</span><span class="sr-only" role="columnheader">{{ t('native-fields.mapping.actions') }}</span></div>
          <div v-for="(row, index) in rows" :key="index" class="mapping-row border-t border-gray-200" role="row">
            <template v-if="editing !== index">
              <span role="cell" class="font-medium text-gray-900">{{ t(`native-fields.fields.${row.field}`) }}</span>
              <div role="cell" class="min-w-0"><span>{{ sourceLabel(row) }}</span><span v-if="!available(row)" class="mt-1 block text-xs text-amber-700">{{ t('native-fields.mapping.unavailable') }}</span><details class="mt-1 text-xs text-gray-500"><summary class="cursor-pointer">{{ t('profile-sync.sourceDetails') }}</summary><span class="break-all">{{ row.providerNamespace }}</span></details></div>
              <div role="cell"><span>{{ t(`native-fields.mapping.properties.${row.sourceProperty}`) }}</span><details v-if="supportsFallback(row)" class="mt-2 text-xs text-gray-600"><summary class="cursor-pointer text-google-700">{{ t('native-fields.mapping.advanced') }}</summary><label class="mt-2 flex items-start gap-2"><input :checked="fallback?.providerNamespace === row.providerNamespace && fallback?.enabled" data-test="mapping-fallback" type="checkbox" :disabled="!canUpdate || saving || editing !== -1 || !available(row)" @change="changeFallback(row, $event)" /><span>{{ t('native-fields.firstNameFallback.label') }}</span></label></details></div>
              <div role="cell" v-if="canUpdate" class="flex flex-wrap gap-3"><button data-test="edit-mapping" type="button" :disabled="saving || editing !== -1" class="text-google-600 disabled:opacity-50" @click="edit(index)">{{ t('native-fields.mapping.edit') }}</button><button data-test="remove-mapping" type="button" :aria-label="t('native-fields.mapping.removeField', { field: t(`native-fields.fields.${row.field}`) })" :disabled="saving || editing !== -1" class="text-red-700 disabled:opacity-50" @click="remove(index)">{{ t('native-fields.mapping.remove') }}</button></div>
            </template>
            <template v-else>
              <span role="cell" class="font-medium">{{ t(`native-fields.fields.${row.field}`) }}</span>
              <label role="cell" class="min-w-0"><span class="sr-only">{{ t('native-fields.source') }}</span><select data-test="edit-source" :value="editSourceIndex" class="w-full rounded-md border border-gray-300 px-2 py-2" @change="changeEditSource"><option value="-1" disabled>{{ t('native-fields.mapping.chooseSource') }}</option><option v-for="(source, i) in availableSources" :value="i" :key="i">{{ sourceLabel(source) }}</option></select></label>
              <label role="cell" class="min-w-0"><span class="sr-only">{{ t('native-fields.mapping.property') }}</span><select v-model="editDraft.sourceProperty" data-test="edit-property" class="w-full rounded-md border border-gray-300 px-2 py-2"><option value="">{{ t('native-fields.mapping.chooseProperty') }}</option><option v-for="property in properties" :key="property" :value="property">{{ t(`native-fields.mapping.properties.${property}`) }}</option></select></label>
              <div role="cell" class="flex flex-wrap gap-3"><button data-test="complete-mapping" type="button" :disabled="!editValid" class="text-google-600 disabled:opacity-50" @click="complete">{{ t('native-fields.mapping.complete') }}</button><button data-test="cancel-edit" type="button" class="text-gray-600" @click="editing = -1">{{ t('native-fields.mapping.cancel') }}</button></div>
            </template>
          </div>
        </div>
        <p v-if="!rows.length" class="py-5 text-sm text-gray-500">{{ t('native-fields.mapping.empty') }}</p>
        <fieldset v-if="canUpdate" :disabled="saving || editing !== -1 || allMapped || !availableSources.length" class="mb-6 min-w-0 space-y-3">
          <legend class="mb-3 text-sm font-medium text-gray-900">{{ t('native-fields.mapping.addTitle') }}</legend>
          <div class="grid min-w-0 grid-cols-1 items-end gap-3 sm:grid-cols-[1fr_1.2fr_1fr_auto]">
            <label class="min-w-0 text-sm text-gray-700">{{ t('native-fields.mapping.field') }}<select v-model="draft.field" data-test="add-field" :disabled="sourceIndex === ''" class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 disabled:bg-gray-50"><option value="">{{ t('native-fields.mapping.chooseField') }}</option><option v-for="field in addFields" :key="field" :value="field">{{ t(`native-fields.fields.${field}`) }}</option></select></label>
            <label class="min-w-0 text-sm text-gray-700">{{ t('native-fields.source') }}<select v-model="sourceIndex" data-test="add-source" class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2"><option value="">{{ t('native-fields.mapping.chooseSource') }}</option><option v-for="(source, i) in availableSources" :key="i" :value="String(i)">{{ sourceLabel(source) }}</option></select></label>
            <label class="min-w-0 text-sm text-gray-700">{{ t('native-fields.mapping.property') }}<select v-model="draft.sourceProperty" data-test="add-property" :disabled="sourceIndex === ''" class="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 disabled:bg-gray-50"><option value="">{{ t(sourceIndex === '' ? 'native-fields.mapping.chooseSourceFirst' : 'native-fields.mapping.chooseProperty') }}</option><option v-for="property in properties" :key="property" :value="property">{{ t(`native-fields.mapping.properties.${property}`) }}</option></select></label>
            <button type="button" data-test="add-mapping" :disabled="!canAdd" class="rounded-md bg-google-50 px-4 py-2 text-sm font-medium text-google-700 disabled:opacity-50" @click="addRow">{{ t('native-fields.mapping.add') }}</button>
          </div>
        </fieldset>
        <p v-if="canUpdate" class="mb-5 text-sm text-gray-500">{{ t(allMapped ? 'native-fields.mapping.allMapped' : !availableSources.length ? 'native-fields.mapping.noSources' : 'native-fields.mapping.availableHelp') }}</p>
        <section class="mt-6 min-w-0 space-y-3 border-t border-gray-200 pt-5" aria-labelledby="new-account-username-title">
          <h4 id="new-account-username-title" class="text-sm font-medium text-gray-900">{{ t('native-fields.userName.title') }}</h4>
          <p class="text-sm text-gray-600">{{ t('native-fields.userName.help') }}</p>
          <p v-if="!userNameSources.length" class="text-sm text-gray-500">{{ t('native-fields.userName.noSources') }}</p>
          <div v-for="source in userNameSources" :key="source.providerNamespace" class="grid min-w-0 gap-3 sm:grid-cols-[1fr_1.5fr]">
            <div class="min-w-0 text-sm text-gray-700">
              <span>{{ t('native-fields.sources.Proof') }}</span>
              <span v-if="!source.userNamePolicyAvailable" class="mt-1 block text-xs text-amber-700">{{ t('native-fields.mapping.unavailable') }}</span>
              <details class="mt-1 text-xs text-gray-500"><summary class="cursor-pointer">{{ t('native-fields.userName.details') }}</summary><span class="break-all">{{ source.providerNamespace }}</span><p class="mt-1">{{ t('native-fields.userName.properties') }}</p></details>
            </div>
            <label class="min-w-0 text-sm text-gray-700">{{ t('native-fields.userName.label') }}
              <select data-test="username-strategy" :value="source.strategy" :disabled="!canUpdate || saving || editing !== -1 || conflict || !source.userNamePolicyAvailable" class="mt-1 w-full min-w-0 rounded-md border border-gray-300 px-3 py-2 disabled:bg-gray-50" @change="changeUserNamePolicy(source, $event.target.value)">
                <option value="CanonicalAccount">{{ t('native-fields.userName.canonical') }}</option>
                <option value="Email">{{ t('native-fields.userName.email') }}</option>
              </select>
              <span v-if="source.strategy === 'Email'" class="mt-2 block text-xs text-gray-600">{{ t('native-fields.userName.emailHelp') }}</span>
            </label>
          </div>
          <p class="text-xs text-gray-500">{{ t('native-fields.userName.authority') }}</p>
        </section>
      </template>
    </template>
    <template #footer><div class="w-full space-y-3"><div class="flex flex-wrap justify-end gap-2"><button type="button" :disabled="saving" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm disabled:opacity-50" @click="emit('close')">{{ t('native-fields.mapping.cancel') }}</button><template v-if="canUpdate"><button data-test="save-mappings" type="button" :disabled="loading || saving || editing !== -1 || conflict" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm disabled:opacity-50" @click="submit(false)">{{ t('native-fields.mapping.save') }}</button><button data-test="save-sync-mappings" type="button" :disabled="!canSync || loading || saving || editing !== -1 || conflict" class="rounded-md bg-google-500 px-4 py-2 text-sm text-white disabled:opacity-50" @click="submit(true)">{{ t('native-fields.mapping.saveNow') }}</button></template></div><p class="text-sm text-gray-500 sm:text-right">{{ t(canSync ? 'native-fields.mapping.syncHelp' : 'native-fields.mapping.syncUnavailable') }}</p></div></template>
  </BaseModal>
</template>
<style scoped>
.mapping-row { display: grid; grid-template-columns: minmax(0, 1fr); gap: .75rem; padding: 1rem .5rem; overflow-wrap: anywhere; }
@media (min-width: 640px) { .mapping-row { grid-template-columns: minmax(0, 1fr) minmax(0, 1.3fr) minmax(0, 1.2fr) minmax(0, .7fr); align-items: start; gap: 1rem; } }
.mapping-row.hidden { display: none; }
@media (min-width: 640px) { .mapping-row.hidden { display: grid; } }
</style>
