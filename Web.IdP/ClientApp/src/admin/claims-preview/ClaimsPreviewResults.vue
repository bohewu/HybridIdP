<script setup>
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({ result: { type: Object, required: true }, context: { type: Object, required: true } })
const { t, te, locale } = useI18n()
const destination = ref('idToken'), filter = ref('all'), search = ref(''), copyState = ref('')
const destinations = ['idToken', 'accessToken', 'userInfo']
const statuses = ['all', 'included', 'omitted', 'unknown']
const message = (group, code) => te(`claims-preview.${group}.${code}`) ? t(`claims-preview.${group}.${code}`) : t('claims-preview.unknownCode', { code })
const reasons = computed(() => props.result.reasons.filter(reason => reason.destination === destination.value))
const counts = computed(() => Object.fromEntries(statuses.map(status => [status, status === 'all' ? reasons.value.length : reasons.value.filter(r => r.status === status).length])))
const visibleReasons = computed(() => reasons.value.filter(reason => (filter.value === 'all' || reason.status === filter.value) &&
  `${reason.claimType} ${message('codes', reason.code)}`.toLocaleLowerCase().includes(search.value.trim().toLocaleLowerCase())))
const json = computed(() => JSON.stringify(props.result[destination.value], null, 2))
const sourceIssue = computed(() => props.result.sources.some(source => source.status !== 'confirmed'))
const badge = status => status === 'included' ? 'bg-green-50 text-green-800' : status === 'unknown' ? 'bg-amber-50 text-amber-900' : 'bg-gray-100 text-gray-600'
function tabKey(event, index) {
  let target = event.key === 'ArrowRight' ? (index + 1) % 3 : event.key === 'ArrowLeft' ? (index + 2) % 3 : event.key === 'Home' ? 0 : event.key === 'End' ? 2 : null
  if (target === null) return
  event.preventDefault()
  destination.value = destinations[target]
  event.currentTarget.parentElement.querySelectorAll('[role="tab"]')[target].focus()
}
async function copy() {
  try { await navigator.clipboard.writeText(json.value); copyState.value = 'copied' }
  catch { copyState.value = 'copyFailed' }
}
function time(value) {
  if (!value || Number.isNaN(Date.parse(value))) return t('claims-preview.unknown')
  return new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value))
}
watch([destination, () => props.result], () => { filter.value = 'all'; search.value = ''; copyState.value = '' })
</script>

<template>
  <section data-test-id="claims-preview-result" role="region" aria-labelledby="preview-result-title" tabindex="-1" class="min-w-0 scroll-mt-20 overflow-hidden rounded-xl border border-gray-200 bg-white focus:outline-none">
    <header class="space-y-3 border-b border-gray-100 p-5">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h2 id="preview-result-title" class="text-lg font-semibold text-gray-900">{{ t('claims-preview.result') }}</h2>
        <span class="rounded-full bg-blue-50 px-2.5 py-1 text-xs font-medium text-blue-800">{{ t('claims-preview.readOnly') }}</span>
      </div>
      <p class="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-gray-500">
        <span class="break-all font-medium text-gray-800">{{ context.client }}</span><span aria-hidden="true">/</span>
        <span class="break-all text-gray-800">{{ context.account }}</span>
        <span>{{ t('claims-preview.selectedScopes', { count: result.scopes.length }) }}</span>
      </p>
      <details class="text-xs leading-relaxed text-gray-500">
        <summary class="w-fit cursor-pointer py-1 text-gray-600 hover:text-gray-900">{{ t('claims-preview.previewLimits') }}</summary>
        <p class="mt-2">{{ t('claims-preview.evidence') }}</p>
      </details>
    </header>
    <div class="border-b border-gray-100 px-4 py-3 sm:px-5">
      <div role="tablist" :aria-label="t('claims-preview.destinations')" class="flex gap-1 rounded-lg bg-gray-100 p-1">
        <button v-for="(item, index) in destinations" :id="`tab-${item}`" :key="item" type="button" role="tab"
          :aria-selected="destination === item" aria-controls="preview-payload" :tabindex="destination === item ? 0 : -1"
          class="inline-flex min-h-10 min-w-0 flex-1 items-center justify-center gap-2 whitespace-nowrap rounded-md px-2 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-google-500"
          :class="destination === item ? 'bg-white text-google-700 shadow-sm' : 'text-gray-500 hover:text-gray-800'"
          @click="destination = item" @keydown="tabKey($event, index)">
          {{ t(`claims-preview.destinationsLabels.${item}`) }}
          <span class="hidden rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-500 sm:inline">{{ Object.keys(result[item]).length }}</span>
        </button>
      </div>
    </div>
    <div id="preview-payload" role="tabpanel" :aria-labelledby="`tab-${destination}`" tabindex="0" class="min-w-0 p-4 focus:outline-none sm:p-5">
      <div class="grid min-w-0 gap-5 xl:grid-cols-2">
        <div class="min-w-0">
          <div class="mb-3 flex min-h-9 items-center justify-between gap-3">
            <h3 class="text-sm font-semibold text-gray-800">{{ t('claims-preview.payload') }}</h3>
            <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-md border border-gray-200 px-3 text-xs font-medium text-gray-600 hover:bg-gray-50 focus-visible:ring-2 focus-visible:ring-google-500" @click="copy">
              <i class="bi bi-copy" aria-hidden="true"></i>{{ t('claims-preview.copyJson') }}
            </button>
          </div>
          <pre data-test-id="claims-preview-json" tabindex="0" class="max-h-[30rem] overflow-auto rounded-lg bg-gray-900 p-4 font-mono text-xs leading-6 text-gray-100 sm:text-sm">{{ json }}</pre>
          <p v-if="copyState" role="status" aria-live="polite" class="mt-2 text-xs text-gray-600">{{ t(`claims-preview.${copyState}`) }}</p>
        </div>
        <div class="min-w-0">
          <div class="mb-3 flex min-h-9 items-center justify-between gap-2">
            <h3 class="text-sm font-semibold text-gray-800">{{ t('claims-preview.reasons') }}</h3>
            <span class="text-xs text-gray-400">{{ t('claims-preview.reasonCount', { count: reasons.length }) }}</span>
          </div>
          <div class="rounded-lg border border-gray-200">
            <div class="space-y-3 border-b border-gray-100 p-3">
              <div class="flex flex-wrap gap-1.5" :aria-label="t('claims-preview.filterReasons')" role="group">
                <button v-for="status in statuses" :key="status" type="button" :aria-pressed="filter === status"
                  class="inline-flex min-h-8 items-center gap-1.5 rounded-md px-2.5 text-xs font-medium focus-visible:ring-2 focus-visible:ring-google-500"
                  :class="filter === status ? 'bg-gray-800 text-white' : 'text-gray-600 hover:bg-gray-100'" @click="filter = status">
                  {{ status === 'all' ? t('claims-preview.allReasons') : message('statuses', status) }}<span class="opacity-70">{{ counts[status] }}</span>
                </button>
              </div>
              <input v-model="search" type="search" :aria-label="t('claims-preview.searchReasons')" :placeholder="t('claims-preview.searchReasons')"
                class="h-10 w-full rounded-md border border-gray-300 px-3 text-sm shadow-sm transition-colors focus:border-google-500 focus:outline-none focus:ring-1 focus:ring-google-500" />
            </div>
            <ul class="max-h-96 divide-y divide-gray-100 overflow-y-auto" :aria-label="t('claims-preview.reasons')">
              <li v-for="reason in visibleReasons" :key="reason.claimType" class="space-y-1.5 p-3">
                <div class="flex items-start justify-between gap-3">
                  <span class="min-w-0 break-all font-mono text-xs font-medium text-gray-900">{{ reason.claimType }}</span>
                  <span class="shrink-0 rounded px-2 py-0.5 text-xs font-medium" :class="badge(reason.status)">{{ message('statuses', reason.status) }}</span>
                </div>
                <p class="text-xs leading-relaxed text-gray-500">{{ message('codes', reason.code) }}</p>
              </li>
            </ul>
            <p v-if="!visibleReasons.length" role="status" class="p-4 text-sm text-gray-500">{{ t('claims-preview.noMatchingReasons') }}</p>
          </div>
        </div>
      </div>
    </div>
    <details :open="sourceIssue" class="border-t border-gray-100 px-5 py-3">
      <summary class="cursor-pointer text-sm font-medium" :class="sourceIssue ? 'text-amber-900' : 'text-gray-700'">
        {{ t('claims-preview.sources') }}
        <span class="ml-2 text-xs font-normal text-gray-500">{{ result.sources.length ? t('claims-preview.sourceCount', { count: result.sources.length }) : t('claims-preview.noSourcesShort') }}</span>
      </summary>
      <p class="mt-3 text-xs leading-relaxed text-gray-500">{{ t('claims-preview.sourcesHelp') }}</p>
      <p v-if="!result.sources.length" class="mt-3 text-sm text-gray-600">{{ t('claims-preview.noSources') }}</p>
      <dl v-for="source in result.sources" :key="source.source" class="mt-3 space-y-1 break-words rounded-md bg-gray-50 p-3 text-sm">
        <dt class="font-medium text-gray-900">{{ source.source }}: {{ message('sourceStatuses', source.status) }}</dt>
        <dd class="text-xs text-gray-500">{{ t('claims-preview.confirmedAt') }}: <time :datetime="source.confirmedAt || undefined">{{ time(source.confirmedAt) }}</time></dd>
      </dl>
    </details>
  </section>
</template>
