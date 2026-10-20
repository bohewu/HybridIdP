<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import permissionService, { Permissions } from '@/utils/permissionService'
import { nativeFieldPolicyApi, policyError } from '@/services/nativeFieldPolicyApi'

const props = defineProps({ target: { type: String, required: true }, targetId: { type: String, required: true }, targetLabel: { type: String, default: '' }, canUpdate: Boolean })
const emit = defineEmits(['close', 'changed'])
const { t } = useI18n()
const data = ref(null), sources = ref([]), loading = ref(true), saving = ref(false), error = ref(''), success = ref(false)
const selectedField = ref('Nickname'), bindingIndex = ref(''), manualValue = ref(''), overwrite = ref(false)
const sourceStatusError = ref(''), canReadSources = ref(false)
const field = computed(() => data.value?.fields.find(item => item.field === selectedField.value))
const binding = computed(() => data.value?.availableBindings[Number(bindingIndex.value)])
function eligible(identity) {
  const source = sources.value.find(item => item.source === identity.source)
  return !!source?.enabled && !source.paused && source.supportedFields.includes(selectedField.value)
    && (!field.value?.owner || field.value.owner.localAccountId === identity.localAccountId)
}
const canAssign = computed(() => bindingIndex.value !== '' && binding.value && eligible(binding.value))
function resetDraft() {
  manualValue.value = field.value?.value ?? ''
  overwrite.value = field.value?.allowOverwrite ?? false
  bindingIndex.value = ''
}
async function load() {
  loading.value = true
  error.value = ''
  sourceStatusError.value = ''
  try {
    await permissionService.loadPermissions()
    canReadSources.value = permissionService.hasPermission(Permissions.Settings.Read)
    data.value = await nativeFieldPolicyApi.target(props.target, props.targetId)
    resetDraft()
    if (canReadSources.value) {
      try { sources.value = await nativeFieldPolicyApi.sources() }
      catch (e) { sources.value = []; sourceStatusError.value = policyError(t, e) }
    }
  } catch (e) { error.value = policyError(t, e) }
  finally { loading.value = false }
}
async function save(operation) {
  if (!props.canUpdate || saving.value || !data.value || (operation === 'AssignSource' && !canAssign.value)) return
  saving.value = true
  error.value = ''
  success.value = false
  try {
    data.value = await nativeFieldPolicyApi.update(props.target, props.targetId, selectedField.value, {
      operation, expectedRevision: data.value.revision,
      ...(operation === 'Manual' ? { manualValue: manualValue.value } : {}),
      ...(operation === 'AssignSource' ? { sourceIdentity: binding.value, allowOverwrite: overwrite.value } : {})
    })
    resetDraft()
    success.value = true
    emit('changed')
  } catch (e) { error.value = policyError(t, e) }
  finally { saving.value = false }
}
onMounted(load)
</script>

<template>
  <BaseModal :show="true" :title="t('native-fields.title')" size="xl" :loading="saving" @close="emit('close')">
    <template #body>
      <p class="text-sm font-medium text-gray-900">{{ t(`native-fields.targets.${target}`) }}: {{ targetLabel }}</p>
      <p class="mt-1 break-all font-mono text-xs text-gray-500">{{ targetId }}</p>
      <p class="mt-3 text-sm text-gray-600">{{ t('native-fields.ownership') }}</p>
      <LoadingPlaceholder v-if="loading" :message="t('native-fields.loading')" />
      <p v-if="error" role="alert" class="mt-4 rounded-md bg-red-50 p-3 text-sm text-red-700">{{ error }}</p>
      <p v-if="success" role="status" class="mt-4 text-sm text-green-700">{{ t('native-fields.saved') }}</p>
      <div v-if="!loading && data" class="mt-4 space-y-4">
        <label class="block text-sm font-medium text-gray-700">{{ t('native-fields.field') }}
          <select v-model="selectedField" :disabled="saving" class="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:ring-2 focus:ring-google-500" @change="resetDraft(); success = false">
            <option v-for="item in data.fields" :key="item.field" :value="item.field">{{ t(`native-fields.fields.${item.field}`) }}</option>
          </select>
        </label>
        <div v-if="field" class="space-y-2 rounded-md bg-gray-50 p-3 text-sm text-gray-700">
          <p class="whitespace-pre-wrap break-words"><span class="font-medium">{{ t('native-fields.current') }}:</span> {{ field.value || t('native-fields.blank') }}</p>
          <p>{{ t(field.manualFixed ? 'native-fields.fixed' : 'native-fields.unlocked') }} · {{ t(field.attributed ? 'native-fields.attributed' : 'native-fields.unattributed') }}</p>
          <p v-if="field.owner" class="break-all">{{ t('native-fields.owner') }}: {{ t(`native-fields.sources.${field.owner.source}`) }} / {{ field.owner.localAccountId }} / {{ field.owner.providerNamespace }} / {{ field.owner.stableSubject }}</p>
          <p>{{ t(field.allowOverwrite ? 'native-fields.overwriteEnabled' : 'native-fields.preserve') }}</p>
        </div>
        <fieldset :disabled="!canUpdate || saving" class="space-y-3 border-t border-gray-200 pt-4">
          <legend class="text-sm font-semibold text-gray-900">{{ t('native-fields.manualTitle') }}</legend>
          <label class="block text-sm text-gray-700">{{ t('native-fields.manualValue') }}
            <input v-model="manualValue" data-test="manual-value" type="text" :placeholder="t(`native-fields.manualPlaceholders.${selectedField}`)" aria-describedby="native-field-manual-help" class="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:ring-2 focus:ring-google-500" />
          </label>
          <p id="native-field-manual-help" class="text-sm text-gray-600">{{ t('native-fields.manualHelp') }}</p>
          <div v-if="canUpdate" class="flex flex-wrap gap-2">
            <button type="button" data-test="fix" class="rounded-md bg-google-500 px-4 py-2 text-sm font-medium text-white focus:ring-2 focus:ring-google-500" @click="save('Manual')">{{ t('native-fields.fix') }}</button>
            <button type="button" data-test="unlock" :disabled="!field?.manualFixed" class="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium disabled:opacity-50" @click="save('Unlock')">{{ t('native-fields.unlock') }}</button>
          </div>
        </fieldset>
        <fieldset :disabled="!canUpdate || saving" class="space-y-3 border-t border-gray-200 pt-4">
          <legend class="text-sm font-semibold text-gray-900">{{ t('native-fields.assignment') }}</legend>
          <p class="text-sm text-gray-600">{{ t('native-fields.assignmentHelp') }}</p>
          <p v-if="!canReadSources" class="text-sm text-gray-600">{{ t('native-fields.sourcePermission') }}</p>
          <p v-if="sourceStatusError" role="alert" class="text-sm text-red-700">{{ sourceStatusError }}</p>
          <label class="block text-sm text-gray-700">{{ t('native-fields.binding') }}
            <select v-model="bindingIndex" data-test="binding" class="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:ring-2 focus:ring-google-500">
              <option value="">{{ t('native-fields.chooseBinding') }}</option>
              <option v-for="(identity, index) in data.availableBindings" :key="index" :value="String(index)" :disabled="!eligible(identity)">
                {{ t(`native-fields.sources.${identity.source}`) }} / {{ identity.localAccountId }} / {{ identity.providerNamespace }} / {{ identity.stableSubject }}{{ !eligible(identity) ? ` (${t('native-fields.unavailable')})` : '' }}
              </option>
            </select>
          </label>
          <p v-if="!data.availableBindings.length" class="text-sm text-gray-600">{{ t('native-fields.noBindings') }}</p>
          <label class="flex items-start gap-2 text-sm text-gray-700"><input v-model="overwrite" data-test="overwrite" type="checkbox" class="mt-1" />{{ t('native-fields.overwrite') }}</label>
          <label class="flex items-start gap-2 text-sm text-gray-500"><input type="checkbox" disabled class="mt-1" />{{ t('native-fields.sourceClear') }}</label>
          <p class="text-sm text-gray-600">{{ t('native-fields.noClear') }}</p>
          <button v-if="canUpdate" type="button" data-test="assign" :disabled="!canAssign" class="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium disabled:opacity-50 disabled:cursor-not-allowed" @click="save('AssignSource')">{{ t('native-fields.assign') }}</button>
        </fieldset>
        <p class="text-sm text-gray-600">{{ t('native-fields.timing') }}</p>
        <p v-if="!canUpdate" class="text-sm text-gray-600">{{ t('native-fields.readOnly') }}</p>
      </div>
      <button v-if="error || sourceStatusError" type="button" :disabled="loading || saving" class="mt-3 text-sm text-google-600 underline" @click="load">{{ t('native-fields.reload') }}</button>
    </template>
    <template #footer><button type="button" :disabled="saving" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium" @click="emit('close')">{{ t('native-fields.close') }}</button></template>
  </BaseModal>
</template>
