<template>
  <div v-if="loading" :class="['loading-indicator', overlay ? 'loading-overlay' : 'inline-loading']" :aria-busy="loading" role="status" :aria-label="ariaLabel || defaultLabel" data-testid="loading-indicator">
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
  // inline vs overlay style adjustments handled by parent classes
  return base.join(' ')
})
</script>

<style scoped>
.loading-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
}
.inline-loading {
  /* keep inline spacing consistent */
}
.loading-overlay {
  position: absolute;
  inset: 0; /* top:0 right:0 bottom:0 left:0 */
  background: rgba(255,255,255,0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 40;
}

/* small helper so spinner isn't too large when used inline */
.loading-indicator .spinner-border { display: inline-block; }
</style>
