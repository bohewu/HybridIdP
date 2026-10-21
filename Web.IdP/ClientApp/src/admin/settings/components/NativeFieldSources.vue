<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ProfileMappingDialog from './ProfileMappingDialog.vue'
import ProfileSyncAccountPicker from '@/components/ProfileSyncAccountPicker.vue'
import ProfileSyncDialog from '@/components/ProfileSyncDialog.vue'
import permissionService, { Permissions } from '@/utils/permissionService'
import { profileSyncApi } from '@/services/profileSyncApi'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import { nativeFieldPolicyApi, policyError } from '@/services/nativeFieldPolicyApi'

const props = defineProps({ canUpdate: Boolean })
const { t } = useI18n()
const showMappings = ref(false), showPicker = ref(false), syncAccounts = ref(null), nativeEnabled = ref(false)
const canReadUsers = computed(() => permissionService.hasPermission(Permissions.Users.Read))
const canSync = computed(() => nativeEnabled.value && canReadUsers.value && permissionService.hasPermission(Permissions.Users.Update) && permissionService.hasPermission(Permissions.Persons.Update))
function openPicker() { showMappings.value = false; showPicker.value = true }
function reviewAccounts(accounts) { if (!canSync.value || !accounts.length) return; showPicker.value = false; syncAccounts.value = accounts.slice(0, 50) }
const sources = ref([]), loading = ref(true), saving = ref(false), error = ref(''), success = ref(false)
async function load() {
  loading.value = true
  error.value = ''
  try {
    sources.value = await nativeFieldPolicyApi.sources()
    if (permissionService.hasPermission(Permissions.Users.Update) && permissionService.hasPermission(Permissions.Persons.Update)) {
      try { nativeEnabled.value = (await profileSyncApi.status()).enabled === true } catch { nativeEnabled.value = false }
    }
  }
  catch (e) { error.value = policyError(t, e) }
  finally { loading.value = false }
}
async function mappingsSaved() { await load(); success.value = true }
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
onMounted(async () => {
  await permissionService.loadPermissions()
  await load()
})
</script>

<template>
  <section class="bg-white shadow-sm rounded-lg border border-gray-200">
    <div class="border-b border-gray-200 p-4">
      <h2 class="text-lg font-semibold text-gray-900">{{ t('native-fields.mapping.title') }}</h2>
    </div>
    <LoadingPlaceholder v-if="loading" :message="t('native-fields.loading')" />
    <div v-else class="p-4 space-y-4">
      <p v-if="error" role="alert" class="rounded-md bg-red-50 p-3 text-sm text-red-700">{{ error }}</p>
      <p v-if="success" role="status" class="text-sm text-green-700">{{ t('native-fields.saved') }}</p>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <p class="text-sm text-gray-600">{{ t('native-fields.mapping.help') }}</p>
        <button data-test="open-mappings" type="button" class="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-google-600 focus:ring-2 focus:ring-google-500" @click="showMappings = true">{{ t('native-fields.mapping.manage') }}</button>
      </div>
      <details class="border-t border-gray-100 pt-3 text-sm text-gray-600">
        <summary class="cursor-pointer font-medium text-gray-700">{{ t('native-fields.syncRules') }}</summary>
        <div class="mt-4 space-y-4">
          <p>{{ t('native-fields.mapping.sourceHelp') }}</p>
          <div v-for="source in sources" :key="source.source" class="flex flex-wrap items-center justify-between gap-3">
            <span class="font-medium text-gray-900">{{ t(`native-fields.sources.${source.source}`) }}</span>
            <div class="flex flex-wrap items-center gap-3"><span class="rounded-full bg-gray-100 px-3 py-1">{{ t(`native-fields.status.${!source.enabled ? 'disabled' : source.paused ? 'paused' : 'enabled'}`) }}</span><button v-if="canUpdate && source.enabled" data-test="toggle-source" type="button" :disabled="saving" class="rounded-md border border-gray-300 px-3 py-2 disabled:opacity-50" @click="toggle(source)">{{ t(source.paused ? 'native-fields.resume' : 'native-fields.pause') }}</button></div>
          </div>
        </div>
      </details>
      <button v-if="error" type="button" :disabled="saving" class="text-sm text-google-600 underline" @click="load">{{ t('native-fields.reload') }}</button>
    </div>
    <ProfileMappingDialog v-if="showMappings" :can-update="canUpdate" :can-sync="canSync" @close="showMappings = false" @saved="mappingsSaved" @sync="openPicker" />
    <ProfileSyncAccountPicker v-if="showPicker" :can-read="canReadUsers" @close="showPicker = false" @selected="reviewAccounts" />
    <ProfileSyncDialog v-if="syncAccounts" :accounts="syncAccounts" :can-sync="canSync" @close="syncAccounts = null" />
  </section>
</template>
