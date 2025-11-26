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
  // Re-uses Bootstrap spinner classes present in the repo while also supporting sizes
  const base = ['spinner-border']
  if (props.size === 'sm') base.push('spinner-border-sm')
  if (props.size === 'lg') base.push('!w-14 !h-14') // a little larger for page-level use; using utilities enhances size when available
  // inline vs overlay style adjustments handled by parent classes
  return base.join(' ')
})
</script>

<style scoped>
/* Keep a tiny CSS fallback for environments that don't process Tailwind classes
   Most visual/layout is handled with Tailwind utility classes applied in the template. */
.loading-indicator { display: flex; align-items: center; justify-content: center; }
.loading-indicator .spinner-border { display: inline-block; }
</style>
