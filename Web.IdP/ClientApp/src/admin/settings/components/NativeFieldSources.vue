<script setup>
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import { nativeFieldPolicyApi, policyError } from '@/services/nativeFieldPolicyApi'

const props = defineProps({ canUpdate: Boolean })
const { t } = useI18n()
const sources = ref([]), loading = ref(true), saving = ref(false), error = ref(''), success = ref(false)
async function load() {
  loading.value = true
  error.value = ''
  try { sources.value = (await nativeFieldPolicyApi.sources()).map(source => ({ ...source, fallbackDraft: source.firstNameDisplayNameFallback === true })) }
  catch (e) { error.value = policyError(t, e) }
  finally { loading.value = false }
}
async function toggle(source) {
  if (!props.canUpdate || !source.enabled || saving.value) return
  saving.value = true
  error.value = ''
  success.value = false
  try {
    await nativeFieldPolicyApi.pause(source.source, { paused: !source.paused, expectedRevision: source.revision })
    await load()
    success.value = !error.value
  } catch (e) { error.value = policyError(t, e) }
  finally { saving.value = false }
}
async function saveFallback(source) {
  if (!props.canUpdate || !source.enabled || !source.providerNamespace || saving.value) return
  saving.value = true
  error.value = ''
  success.value = false
  try {
    await nativeFieldPolicyApi.firstNameFallback(source.source, {
      providerNamespace: source.providerNamespace,
      enabled: source.fallbackDraft,
      expectedRevision: source.revision
    })
    await load()
    success.value = !error.value
  } catch (e) {
    if (e.message === 'conflict') await load()
    error.value = policyError(t, e)
  } finally { saving.value = false }
}
onMounted(load)
</script>

<template>
  <section class="bg-white shadow-sm rounded-lg border border-gray-200">
    <div class="border-b border-gray-200 p-4">
      <h2 class="text-lg font-semibold text-gray-900">{{ t('native-fields.title') }}</h2>
    </div>
    <LoadingPlaceholder v-if="loading" :message="t('native-fields.loading')" />
    <div v-else class="p-4 space-y-4">
      <p v-if="error" role="alert" class="rounded-md bg-red-50 p-3 text-sm text-red-700">{{ error }}</p>
      <p v-if="success" role="status" class="text-sm text-green-700">{{ t('native-fields.saved') }}</p>
      <div v-for="source in sources" :key="source.source">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h3 class="font-medium text-gray-900">{{ t(`native-fields.sources.${source.source}`) }}</h3>
          <div class="flex items-center gap-3">
            <span class="rounded-full bg-gray-100 px-3 py-1 text-sm text-gray-700">{{ t(`native-fields.status.${!source.enabled ? 'disabled' : source.paused ? 'paused' : 'enabled'}`) }}</span>
            <button v-if="canUpdate && source.enabled" data-test="toggle-source" type="button" :disabled="saving" class="rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-google-500 disabled:opacity-50" @click="toggle(source)">
              {{ t(source.paused ? 'native-fields.resume' : 'native-fields.pause') }}
            </button>
          </div>
        </div>
        <p v-if="!source.enabled" class="mt-2 text-sm text-gray-600">{{ t('native-fields.deploymentDisabled') }}</p>
      </div>
      <details data-test="sync-settings" class="border-t border-gray-100 pt-3 text-sm text-gray-600">
        <summary class="cursor-pointer font-medium text-gray-700 focus-visible:outline-google-500">{{ t('native-fields.settingsTitle') }}</summary>
        <div class="mt-4 space-y-4">
          <template v-for="source in sources" :key="source.source">
            <div v-if="source.source === 'Proof' && source.enabled && source.providerNamespace" class="space-y-2">
              <label class="flex items-start gap-2 text-sm text-gray-700">
                <input v-model="source.fallbackDraft" data-test="first-name-fallback" type="checkbox" :disabled="!canUpdate || saving" aria-describedby="first-name-fallback-help" class="mt-1 rounded border-gray-300 focus:ring-2 focus:ring-google-500 disabled:cursor-not-allowed" />
                {{ t('native-fields.firstNameFallback.label') }}
              </label>
              <p id="first-name-fallback-help">{{ t('native-fields.firstNameFallback.help') }}</p>
              <button v-if="canUpdate" data-test="save-first-name-fallback" type="button" :disabled="saving || source.fallbackDraft === (source.firstNameDisplayNameFallback === true)" class="rounded-md bg-google-500 px-4 py-2 text-sm font-medium text-white focus:ring-2 focus:ring-google-500 disabled:opacity-50 disabled:cursor-not-allowed" @click="saveFallback(source)">{{ t('native-fields.firstNameFallback.save') }}</button>
            </div>
          </template>
          <details data-test="source-details">
            <summary class="cursor-pointer font-medium text-gray-700 focus-visible:outline-google-500">{{ t('native-fields.syncRules') }}</summary>
            <div class="mt-3 space-y-3">
              <p>{{ t('native-fields.sourceDescription') }}</p>
              <div v-for="source in sources" :key="source.source">
                <h4 class="font-medium text-gray-900">{{ t(`native-fields.sources.${source.source}`) }}</h4>
                <p class="mt-1">{{ t(`native-fields.sourceHelp.${source.source}`) }}</p>
                <p class="mt-1">{{ t('native-fields.supported') }}: {{ source.supportedFields.map(field => t(`native-fields.fields.${field}`)).join(', ') }}</p>
                <template v-if="source.source === 'Proof'">
                  <p class="mt-1 break-all">{{ t('native-fields.firstNameFallback.namespace') }}: {{ source.providerNamespace || t('native-fields.unavailable') }}</p>
                  <p class="mt-2">{{ t('native-fields.firstNameFallback.mapping') }}</p>
                  <p class="mt-2">{{ t('native-fields.firstNameFallback.authority') }}</p>
                </template>
              </div>
              <p>{{ t('native-fields.preserve') }}</p>
              <p>{{ t('native-fields.noClear') }}</p>
            </div>
          </details>
        </div>
      </details>
      <button v-if="error" type="button" :disabled="saving" class="text-sm text-google-600 underline" @click="load">{{ t('native-fields.reload') }}</button>
    </div>
  </section>
</template>
