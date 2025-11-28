<template>
  <div
    v-if="loading"
    :class="[
      overlay ? 'absolute inset-0 bg-white/60 z-40 flex items-center justify-center' : 'flex items-center justify-center',
      'loading-indicator'
    ]"
    :aria-busy="loading"
    role="status"
    :aria-label="ariaLabel || defaultLabel"
    data-testid="loading-indicator">
    <div :class="spinnerClasses" aria-hidden="true"></div>
    <div v-if="showMessage" class="ml-3 text-sm text-gray-600" aria-hidden="true">
      <slot>{{ message }}</slot>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  loading: { type: Boolean, default: true },
  size: { type: String, default: 'md' }, // sm | md | lg
  overlay: { type: Boolean, default: false },
  message: { type: String, default: '' },
  ariaLabel: { type: String, default: '' },
  showMessage: { type: Boolean, default: false }
})

const { t } = useI18n()

const defaultLabel = t('loading') || 'Loading'

const spinnerClasses = computed(() => {
  const base = ['animate-spin', 'rounded-full', 'border-b-2', 'border-blue-600']
  if (props.size === 'sm') {
    base.push('h-8', 'w-8')
  } else if (props.size === 'lg') {
    base.push('h-16', 'w-16')
  } else {
    // md is default
    base.push('h-12', 'w-12')
  }
  return base.join(' ')
})

const showMessage = computed(() => {
  return props.showMessage || !!props.message
})
</script>

<style scoped>
.loading-indicator { display: flex; align-items: center; justify-content: center; }
</style>
