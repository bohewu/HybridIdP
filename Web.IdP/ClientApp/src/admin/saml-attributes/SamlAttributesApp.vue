<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import PreviewEntityPicker from '../claims-preview/PreviewEntityPicker.vue'
import permissionService, { Permissions } from '@/utils/permissionService'
import { samlAttributesApi as api } from './samlAttributesApi'

const { t, te } = useI18n()
const tr = (key, args) => t(`saml-attributes.${key}`, args)
const error = ref(''), busy = ref(true), enabled = ref(false), partners = ref([]), catalog = ref([])
const entityId = ref(''), mappings = ref([]), saved = ref('[]'), loaded = ref(false), saving = ref(false), savedNotice = ref(false)
const canRead = ref(false), canUpdate = ref(false), canPreview = ref(false)
const accounts = ref([]), account = ref(null), query = ref(''), skip = ref(0), accountsBusy = ref(false), accountError = ref('')
const result = ref(null), previewBusy = ref(false)
const dirty = computed(() => JSON.stringify(mappings.value) !== saved.value)
const locked = computed(() => !canUpdate.value || !loaded.value || saving.value)
const accountItems = computed(() => accounts.value.map(item => ({ value: item.userId, label: item.displayName, detail: item.userId })))
const message = code => te(`saml-attributes.errors.${code}`) ? tr(`errors.${code}`) : tr('errors.request_failed')
const sourceLabel = source => tr(`fields.${source.replace('Person.', '')}`)
const sourceGroup = source => tr(source.startsWith('Person.') ? 'person' : 'accountSource')
let policyRequest = 0, previewRequest = 0, accountRequest = 0, timer
function clearPreview() { previewRequest++; previewBusy.value = false; result.value = null }
watch(mappings, () => { clearPreview(); savedNotice.value = false }, { deep: true, flush: 'sync' })
watch(account, clearPreview, { flush: 'sync' })
watch(entityId, () => loadPolicy(), { flush: 'sync' })
async function loadPolicy() {
  const request = ++policyRequest
  clearPreview(); loaded.value = false; mappings.value = []; saved.value = '[]'; error.value = ''; savedNotice.value = false
  if (!entityId.value) return
  try {
    const policy = await api.policy(entityId.value)
    if (request !== policyRequest) return
    mappings.value = policy.mappings.map(item => ({ ...item })); saved.value = JSON.stringify(mappings.value); loaded.value = true
  } catch (e) { if (request === policyRequest) error.value = e.message }
}
function add() {
  const source = catalog.value.find(item => !mappings.value.some(mapping => mapping.source === item.source))
  if (!locked.value && source) mappings.value.push({ source: source.source, name: source.defaultName })
}
async function save() {
  if (locked.value || !dirty.value) return
  saving.value = true; error.value = ''; clearPreview()
  const request = policyRequest
  try {
    const policy = await api.save({ entityId: entityId.value, mappings: mappings.value.map(item => ({ ...item })) })
    if (request !== policyRequest) return
    mappings.value = policy.mappings.map(item => ({ ...item })); saved.value = JSON.stringify(mappings.value); savedNotice.value = true
  } catch (e) { if (request === policyRequest) error.value = e.message }
  finally { saving.value = false }
}
async function loadAccounts(offset = 0) {
  if (!canPreview.value) return
  const request = ++accountRequest
  skip.value = offset; accountsBusy.value = true; accountError.value = ''
  try {
    const data = await api.accounts(query.value.trim(), offset)
    if (request === accountRequest) accounts.value = data.items
  } catch (e) { if (request === accountRequest) { accounts.value = []; accountError.value = e.message } }
  finally { if (request === accountRequest) accountsBusy.value = false }
}
watch(query, () => {
  accountRequest++; accountsBusy.value = true; clearTimeout(timer); timer = setTimeout(() => loadAccounts(), 250)
}, { flush: 'sync' })
async function preview() {
  if (!canPreview.value || !loaded.value || dirty.value || !account.value || previewBusy.value || saving.value) return
  clearPreview(); error.value = ''; previewBusy.value = true
  const request = ++previewRequest
  try {
    const data = await api.preview({ entityId: entityId.value, userId: account.value.value })
    if (request === previewRequest) result.value = data
  } catch (e) { if (request === previewRequest) error.value = e.message }
  finally { if (request === previewRequest) previewBusy.value = false }
}
onMounted(async () => {
  await permissionService.reloadPermissions()
  canRead.value = permissionService.hasPermission(Permissions.Settings.Read)
  canUpdate.value = permissionService.hasPermission(Permissions.Settings.Update)
  canPreview.value = permissionService.hasPermission(Permissions.Claims.Preview)
  try {
    if (!canRead.value) { error.value = 'forbidden'; return }
    const data = await api.partners()
    enabled.value = data.enabled; partners.value = data.items
    if (enabled.value && partners.value.length) {
      catalog.value = (await api.catalog()).items
      if (canPreview.value) loadAccounts()
    }
  } catch (e) { error.value = e.message }
  finally { busy.value = false }
})
onBeforeUnmount(() => { policyRequest++; previewRequest++; accountRequest++; clearTimeout(timer) })
</script>

<template>
  <div class="px-4 py-6">
    <PageHeader :title="tr('title')" :subtitle="tr('subtitle')" />
    <div class="space-y-4">
      <div v-if="error" role="alert" class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">{{ message(error) }}</div>
      <p v-if="busy" role="status" class="p-5 text-sm text-gray-500">{{ tr('loading') }}</p>
      <template v-else-if="canRead">
        <p v-if="!enabled" role="status" class="rounded-lg border border-gray-200 bg-white p-5 text-sm text-gray-600">{{ tr('disabled') }}</p>
        <p v-else-if="!partners.length" role="status" class="rounded-lg border border-gray-200 bg-white p-5 text-sm text-gray-600">{{ tr('noPartners') }}</p>
        <template v-else>
          <section class="rounded-xl border border-gray-200 bg-white p-5">
            <label for="saml-partner" class="mb-2 block text-sm font-medium text-gray-800">{{ tr('application') }}</label>
            <select id="saml-partner" v-model="entityId" :disabled="saving" class="min-h-10 w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:ring-2 focus:ring-google-500">
              <option value="">{{ tr('chooseApplication') }}</option>
              <option v-for="partner in partners" :key="partner.entityId" :value="partner.entityId">{{ partner.entityId }}</option>
            </select>
            <p v-if="dirty" class="mt-2 text-xs text-amber-800">{{ tr('partnerHelp') }}</p>
          </section>
          <template v-if="entityId">
            <section class="rounded-xl border border-gray-200 bg-white">
              <div class="border-b border-gray-100 px-5 py-3"><h2 class="text-base font-semibold text-gray-900">{{ tr('selection') }}</h2></div>
              <div class="space-y-4 p-5">
                <details class="rounded-md bg-gray-50 p-3 text-sm text-gray-700">
                  <summary class="min-h-8 cursor-pointer font-medium">{{ tr('fixedIdentity') }}</summary>
                  <p class="mt-1 break-words">{{ tr('fixedHelp') }}</p>
                  <code class="mt-1 block break-all text-xs">urn:hybrididp:claims:subject</code>
                </details>
                <p v-if="!loaded" role="status" class="text-sm text-gray-500">{{ error ? tr('loadFailed') : tr('loading') }} <button v-if="error" type="button" class="min-h-10 text-google-600 underline" @click="loadPolicy">{{ tr('retry') }}</button></p>
                <template v-else>
                  <p v-if="!mappings.length" class="text-sm text-gray-500">{{ tr('noAttributes') }}</p>
                  <fieldset :disabled="locked" class="space-y-3">
                    <legend class="sr-only">{{ tr('selection') }}</legend>
                    <div v-for="(mapping, index) in mappings" :key="index" class="grid min-w-0 grid-cols-1 items-end gap-3 rounded-lg border border-gray-200 p-3 md:grid-cols-[1fr_1fr_auto]">
                      <div class="min-w-0">
                        <label :for="`source-${index}`" class="mb-1 block text-sm font-medium text-gray-700">{{ tr('source') }}</label>
                        <select :id="`source-${index}`" v-model="mapping.source" class="min-h-10 w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:ring-2 focus:ring-google-500">
                          <optgroup v-for="person in [false, true]" :key="String(person)" :label="tr(person ? 'person' : 'accountSource')">
                            <option v-for="item in catalog.filter(item => item.source.startsWith('Person.') === person)" :key="item.source" :value="item.source" :disabled="mappings.some((other, i) => i !== index && other.source === item.source)">{{ sourceLabel(item.source) }}</option>
                          </optgroup>
                        </select>
                      </div>
                      <div class="min-w-0">
                        <label :for="`name-${index}`" class="mb-1 block text-sm font-medium text-gray-700">{{ tr('outputName') }}</label>
                        <input :id="`name-${index}`" v-model="mapping.name" maxlength="128" :placeholder="tr('namePlaceholder')" class="min-h-10 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:ring-2 focus:ring-google-500" />
                      </div>
                      <button type="button" :aria-label="tr('removeAttribute', { number: index + 1 })" class="min-h-10 px-3 text-sm text-red-600 hover:underline disabled:text-gray-400" @click="mappings.splice(index, 1)">{{ tr('remove') }}</button>
                    </div>
                  </fieldset>
                  <details v-if="mappings.length" class="text-xs text-gray-500">
                    <summary class="min-h-8 cursor-pointer">{{ tr('nameRules') }}</summary>
                    <p>{{ tr('nameHelp') }}</p>
                  </details>
                  <div class="flex flex-wrap items-center gap-3">
                    <button type="button" data-test="add" :disabled="locked || mappings.length >= catalog.length" class="min-h-10 rounded-md border border-gray-300 px-4 py-2 text-sm hover:bg-gray-50 disabled:opacity-50" @click="add">{{ tr('add') }}</button>
                    <button type="button" data-test="save" :disabled="locked || !dirty" class="min-h-10 rounded-md bg-google-500 px-4 py-2 text-sm font-medium text-white hover:bg-google-600 disabled:opacity-50" @click="save">{{ tr(saving ? 'saving' : 'save') }}</button>
                    <button v-if="dirty" type="button" :disabled="saving" class="min-h-10 px-2 text-sm text-gray-600 underline" @click="loadPolicy">{{ tr('discard') }}</button>
                    <p v-if="dirty || savedNotice" role="status" class="text-sm" :class="dirty ? 'text-amber-800' : 'text-gray-500'">{{ dirty ? tr('unsaved') : tr('saved') }}</p>
                  </div>
                </template>
              </div>
            </section>
            <section class="rounded-xl border border-gray-200 bg-white">
              <div class="border-b border-gray-100 px-5 py-3"><h2 class="text-base font-semibold text-gray-900">{{ tr('previewTitle') }}</h2><p class="mt-1 text-xs text-gray-500">{{ tr('previewHelp') }}</p></div>
              <div class="space-y-4 p-5">
                <p v-if="!canPreview" class="text-sm text-gray-500">{{ tr('previewDenied') }}</p>
                <template v-else>
                  <p v-if="dirty" role="status" class="rounded-md bg-amber-50 p-3 text-sm text-amber-900">{{ tr('dirtyPreview') }}</p>
                  <div class="max-w-xl">
                    <PreviewEntityPicker id="saml-account" :label="tr('account')" :placeholder="tr('chooseAccount')" :search-placeholder="tr('searchAccount')" :items="accountItems" :selected="account" :query="query" :loading="accountsBusy" :error="accountError ? message(accountError) : ''" :skip="skip" :has-next="accounts.length === 50" @select="account = $event" @query="query = $event" @page="loadAccounts" @retry="loadAccounts(skip)" />
                  </div>
                  <button type="button" data-test="preview" :disabled="!loaded || dirty || !account || previewBusy || saving" class="min-h-10 rounded-md bg-google-500 px-4 py-2 text-sm font-medium text-white hover:bg-google-600 disabled:opacity-50" @click="preview">{{ tr(previewBusy ? 'previewing' : 'preview') }}</button>
                  <div v-if="result" data-test="result" class="space-y-3 border-t border-gray-100 pt-4">
                    <h3 class="text-sm font-semibold text-gray-900">{{ tr('savedPreview') }}</h3>
                    <div class="overflow-x-auto"><table class="w-full table-fixed text-left text-sm sm:table-auto"><caption class="sr-only">{{ tr('savedPreview') }}</caption><thead class="bg-gray-50 text-xs text-gray-500"><tr><th scope="col" class="px-3 py-2">{{ tr('outputName') }}</th><th scope="col" class="px-3 py-2">{{ tr('value') }}</th></tr></thead><tbody class="divide-y divide-gray-100"><tr v-for="attribute in result.attributes" :key="attribute.name"><td class="max-w-xs break-all px-3 py-3 font-mono text-xs">{{ attribute.name }}</td><td class="max-w-md whitespace-pre-wrap break-words px-3 py-3">{{ attribute.value }}</td></tr></tbody></table></div>
                    <ul v-if="result.omitted.length" class="space-y-1 text-sm text-amber-800"><li v-for="item in result.omitted" :key="item.source">{{ tr('missing', { name: item.name, source: `${sourceGroup(item.source)} / ${sourceLabel(item.source)}` }) }}</li></ul>
                    <details class="text-xs text-gray-500"><summary class="min-h-8 cursor-pointer">{{ tr('details') }}</summary><dl class="space-y-2 break-all"><dt>{{ tr('application') }}</dt><dd>{{ result.entityId }}</dd><dt>{{ tr('account') }}</dt><dd>{{ account.label }}</dd><dt>{{ tr('nameId') }}</dt><dd class="font-mono">{{ result.nameId }}</dd><dt>NameID Format</dt><dd>{{ result.nameIdFormat }}</dd><dt>NameQualifier</dt><dd>{{ result.nameQualifier }}</dd><dt>SPNameQualifier</dt><dd>{{ result.spNameQualifier }}</dd></dl></details>
                  </div>
                </template>
              </div>
            </section>
          </template>
        </template>
      </template>
    </div>
  </div>
</template>
