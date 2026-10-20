<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  id: { type: String, required: true }, label: { type: String, required: true },
  placeholder: { type: String, required: true }, searchPlaceholder: { type: String, required: true },
  items: { type: Array, default: () => [] }, selected: { type: Object, default: null },
  query: { type: String, default: '' }, loading: Boolean, error: { type: String, default: '' },
  skip: { type: Number, default: 0 }, hasNext: Boolean
})
const emit = defineEmits(['select', 'query', 'page', 'retry'])
const { t } = useI18n()
const root = ref(null), trigger = ref(null), input = ref(null), panel = ref(null)
const open = ref(false), active = ref(-1)
const activeId = computed(() => !props.loading && active.value >= 0 && props.items[active.value]
  ? `${props.id}-option-${active.value}` : undefined)
async function expand(last = false) {
  open.value = true
  active.value = props.items.length ? (last ? props.items.length - 1 : 0) : -1
  await nextTick()
  input.value?.focus()
  panel.value?.scrollIntoView?.({ block: 'nearest' })
}
function close(restoreFocus = false) {
  open.value = false
  if (restoreFocus) trigger.value?.focus()
}
function choose(item) { emit('select', item); close(true) }
function page(skip) { input.value?.focus(); emit('page', skip) }
function retry() { input.value?.focus(); emit('retry') }
async function move(direction) {
  if (props.loading || !props.items.length) return
  active.value = Math.max(0, Math.min(props.items.length - 1, active.value + direction))
  await nextTick()
  panel.value?.querySelector(`#${props.id}-option-${active.value}`)?.scrollIntoView?.({ block: 'nearest' })
}
function keydown(event) {
  if (event.isComposing) return
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault(); move(event.key === 'ArrowDown' ? 1 : -1)
  } else if (event.key === 'Enter') {
    event.preventDefault()
    if (!props.loading && props.items[active.value]) choose(props.items[active.value])
  } else if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true) }
}
function outside(event) { if (!root.value?.contains(event.target)) close() }
function focusout(event) { if (!root.value?.contains(event.relatedTarget)) close() }
watch(() => props.items, () => { active.value = props.items.length ? 0 : -1 })
onMounted(() => document.addEventListener('pointerdown', outside))
onBeforeUnmount(() => document.removeEventListener('pointerdown', outside))
</script>

<template>
  <div ref="root" class="relative min-w-0" @focusout="focusout">
    <label :for="`${id}-trigger`" class="mb-2 block text-sm font-medium text-gray-800">{{ label }}</label>
    <div class="relative">
      <button :id="`${id}-trigger`" ref="trigger" type="button" :aria-label="label" :aria-describedby="`${id}-value`"
        aria-haspopup="dialog" :aria-expanded="open" :aria-controls="open ? `${id}-panel` : undefined"
        class="flex min-h-14 w-full items-center gap-3 rounded-lg border bg-white px-3 py-2.5 text-left transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-google-500 focus-visible:ring-offset-2"
        :class="open ? 'border-google-500' : 'border-gray-300 hover:border-gray-400'"
        @click="open ? close() : expand()" @keydown.down.prevent="expand()" @keydown.up.prevent="expand(true)">
        <span :id="`${id}-value`" class="min-w-0 flex-1 pr-10">
          <span class="block truncate text-sm" :class="selected ? 'font-medium text-gray-900' : 'text-gray-500'">{{ selected?.label || placeholder }}</span>
          <span v-if="selected?.detail" class="mt-0.5 block truncate font-mono text-xs text-gray-500">{{ selected.detail }}</span>
        </span>
        <i class="bi bi-chevron-down shrink-0 text-xs text-gray-400" aria-hidden="true"></i>
      </button>
      <button v-if="selected" type="button" :aria-label="t('claims-preview.lookup.clear', { label })"
        class="absolute right-9 top-1/2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-md text-gray-400 hover:bg-gray-100 hover:text-gray-700 focus-visible:ring-2 focus-visible:ring-google-500"
        @click="choose(null)"><i class="bi bi-x-lg text-xs" aria-hidden="true"></i></button>
    </div>
    <div v-if="open" :id="`${id}-panel`" ref="panel" role="dialog" :aria-label="label"
      class="absolute left-0 right-0 top-full z-40 mt-2 overflow-hidden rounded-lg border border-gray-200 bg-white shadow-lg">
      <div class="border-b border-gray-100 p-2">
        <div class="relative">
          <i class="bi bi-search pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm text-gray-400" aria-hidden="true"></i>
          <input :id="`${id}-search`" ref="input" type="text" role="combobox" autocomplete="off" maxlength="200"
            :aria-label="t('claims-preview.lookup.search', { label })" aria-autocomplete="list" :aria-expanded="true"
            :aria-controls="`${id}-options`" :aria-activedescendant="activeId" :placeholder="searchPlaceholder" :value="query"
            class="h-10 w-full rounded-md border border-gray-300 py-2 pl-9 pr-3 text-sm shadow-sm transition-colors focus:border-google-500 focus:outline-none focus:ring-1 focus:ring-google-500"
            @input="emit('query', $event.target.value)" @keydown="keydown" />
        </div>
      </div>
      <ul :id="`${id}-options`" role="listbox" :aria-label="label" :aria-busy="loading" class="max-h-[min(15rem,35vh)] overflow-y-auto p-1">
        <template v-if="!loading && !error">
          <li v-for="(item, index) in items" :id="`${id}-option-${index}`" :key="item.value" role="option"
            :aria-selected="selected?.value === item.value" class="flex cursor-pointer items-start gap-3 rounded-md px-3 py-2.5"
            :class="active === index ? 'bg-google-100' : 'hover:bg-gray-50'" @pointermove="active = index" @mousedown.prevent @click="choose(item)">
            <span class="min-w-0 flex-1">
              <span class="block break-words text-sm font-medium text-gray-900">{{ item.label }}</span>
              <span v-if="item.detail" class="mt-0.5 block break-all font-mono text-xs text-gray-500">{{ item.detail }}</span>
              <span v-if="item.note" class="mt-1 block text-xs text-amber-800">{{ item.note }}</span>
            </span>
            <i v-if="selected?.value === item.value" class="bi bi-check-lg text-google-600" aria-hidden="true"></i>
          </li>
        </template>
      </ul>
      <p v-if="loading" role="status" class="px-4 py-5 text-sm text-gray-500">{{ t('claims-preview.lookup.loading') }}</p>
      <div v-else-if="error" class="space-y-2 px-4 py-3 text-sm">
        <p role="alert" class="text-red-700">{{ error }}</p>
        <button type="button" class="min-h-9 font-medium text-google-600 hover:underline" @click="retry">{{ t('claims-preview.lookup.retry') }}</button>
      </div>
      <p v-else-if="!items.length" role="status" class="px-4 py-5 text-sm text-gray-500">{{ t('claims-preview.lookup.empty') }}</p>
      <div v-if="skip > 0 || hasNext" class="flex items-center justify-between gap-2 border-t border-gray-100 px-3 py-1 text-xs">
        <button type="button" :aria-label="t('claims-preview.lookup.previous', { label })" :disabled="loading || skip === 0"
          class="min-h-9 px-1 text-google-600 disabled:text-gray-300" @click="page(Math.max(0, skip - 50))">{{ t('claims-preview.previous') }}</button>
        <span class="text-gray-500">{{ t('claims-preview.page', { page: skip / 50 + 1 }) }}</span>
        <button type="button" :aria-label="t('claims-preview.lookup.next', { label })" :disabled="loading || !hasNext"
          class="min-h-9 px-1 text-google-600 disabled:text-gray-300" @click="page(skip + 50)">{{ t('claims-preview.next') }}</button>
      </div>
    </div>
  </div>
</template>
