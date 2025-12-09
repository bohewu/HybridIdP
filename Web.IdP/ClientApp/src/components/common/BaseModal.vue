<template>
  <Teleport to="body">
    <Transition name="modal">
      <div
        v-if="show"
        :class="['fixed inset-0 overflow-y-auto', zIndexClass]"
        :aria-labelledby="ariaLabelledby"
        :aria-describedby="ariaDescribedby"
        role="dialog"
        aria-modal="true"
      >
        <!-- Backdrop -->
        <div
          class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"
          :class="{ 'cursor-pointer': closeOnBackdrop }"
          @click="handleBackdropClick"
        ></div>

        <!-- Modal container -->
        <div class="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
          <!-- Modal panel -->
          <div
            ref="modalPanel"
            :class="[
              'relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full',
              sizeClass
            ]"
          >
            <!-- Close icon -->
            <button
              v-if="showCloseIcon"
              type="button"
              @click="handleClose"
              :disabled="loading"
              :aria-label="closeAriaLabel"
              tabindex="-1"
              class="absolute top-4 right-4 text-gray-400 hover:text-gray-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 rounded-sm disabled:opacity-50 disabled:cursor-not-allowed z-10"
            >
              <svg class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>

            <!-- Header slot -->
            <div v-if="$slots.header || title" class="px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
              <slot name="header">
                <h3 v-if="title" :id="headingId" class="text-lg font-semibold leading-6 text-gray-900">
                  {{ title }}
                </h3>
              </slot>
            </div>

            <!-- Body slot -->
            <div class="px-4 pb-4 sm:px-6 sm:pb-4">
              <slot name="body"></slot>
            </div>

            <!-- Footer slot -->
            <div v-if="$slots.footer" class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6">
              <slot name="footer"></slot>
            </div>

            <!-- Loading overlay -->
            <div
              v-if="loading"
              class="absolute inset-0 bg-white bg-opacity-75 rounded-lg flex items-center justify-center"
            >
              <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  title: {
    type: String,
    default: ''
  },
  size: {
    type: String,
    default: 'md',
    validator: (value) => ['xs', 'sm', 'md', 'lg', 'xl', '2xl', '3xl', '4xl', '5xl'].includes(value)
  },
  showCloseIcon: {
    type: Boolean,
    default: true
  },
  closeOnBackdrop: {
    type: Boolean,
    default: false
  },
  closeOnEsc: {
    type: Boolean,
    default: true
  },
  loading: {
    type: Boolean,
    default: false
  },
  initialFocusRef: {
    type: Object,
    default: null
  },
  zIndex: {
    type: String,
    default: 'modal',
    validator: (value) => ['modal', 'modal-nested', 'alert'].includes(value)
  },
  ariaLabelledby: {
    type: String,
    default: ''
  },
  ariaDescribedby: {
    type: String,
    default: ''
  }
})

const emit = defineEmits(['close'])

const { t } = useI18n()
const modalPanel = ref(null)
const headingId = computed(() => props.ariaLabelledby || 'modal-heading')

// Size classes mapping
const sizeClass = computed(() => {
  const sizes = {
    xs: 'sm:max-w-xs',    // ~320px - very small dialogs
    sm: 'sm:max-w-sm',    // ~384px - small dialogs
    md: 'sm:max-w-lg',    // ~512px - medium dialogs (default)
    lg: 'sm:max-w-2xl',   // ~672px - large forms
    xl: 'sm:max-w-3xl',   // ~768px - extra large forms
    '2xl': 'sm:max-w-4xl', // ~896px - very large forms
    '3xl': 'sm:max-w-5xl', // ~1024px - extra extra large
    '4xl': 'sm:max-w-6xl', // ~1152px - huge forms
    '5xl': 'sm:max-w-7xl'  // ~1280px - full-width tables
  }
  return sizes[props.size] || sizes.md
})

// Z-index classes mapping
const zIndexClass = computed(() => {
  const zIndexes = {
    modal: 'z-modal',
    'modal-nested': 'z-modal-nested',
    alert: 'z-alert'
  }
  return zIndexes[props.zIndex] || zIndexes.modal
})

// Close button aria-label
const closeAriaLabel = computed(() => {
  return t('common.modal.close', 'Close')
})

// Handle ESC key
const handleEscKey = (event) => {
  if (event.key === 'Escape' && props.show && props.closeOnEsc && !props.loading) {
    handleClose()
  }
}

// Handle backdrop click
const handleBackdropClick = () => {
  if (props.closeOnBackdrop && !props.loading) {
    handleClose()
  }
}

// Handle close
const handleClose = () => {
  if (!props.loading) {
    emit('close')
  }
}

// Focus management
const setInitialFocus = async () => {
  await nextTick()
  
  if (props.initialFocusRef?.value) {
    props.initialFocusRef.value.focus()
    return
  }
  
  if (modalPanel.value) {
    // Add small delay to ensure DOM is fully rendered
    await nextTick()
    
    // Prioritize form inputs, then other focusable elements (excluding close button with tabindex="-1")
    const inputElements = modalPanel.value.querySelectorAll(
      'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled])'
    )
    console.log('[BaseModal] Found input elements:', inputElements.length, inputElements)
    if (inputElements.length > 0) {
      console.log('[BaseModal] Focusing first input:', inputElements[0])
      inputElements[0].focus()
      return
    }
    
    // If no inputs, find other focusable elements
    const focusableElements = modalPanel.value.querySelectorAll(
      'button:not([disabled]):not([tabindex="-1"]), [href], [tabindex]:not([tabindex="-1"])'
    )
    console.log('[BaseModal] Found focusable elements:', focusableElements.length, focusableElements)
    if (focusableElements.length > 0) {
      console.log('[BaseModal] Focusing first focusable:', focusableElements[0])
      focusableElements[0].focus()
    }
  }
}

// Focus trap
const handleTabKey = (event) => {
  if (!props.show || !modalPanel.value) return

  const focusableElements = modalPanel.value.querySelectorAll(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
  )

  const firstElement = focusableElements[0]
  const lastElement = focusableElements[focusableElements.length - 1]

  if (event.shiftKey && document.activeElement === firstElement) {
    event.preventDefault()
    lastElement?.focus()
  } else if (!event.shiftKey && document.activeElement === lastElement) {
    event.preventDefault()
    firstElement?.focus()
  }
}

const handleKeyDown = (event) => {
  if (event.key === 'Tab') {
    handleTabKey(event)
  }
}

// Watch show prop to manage focus
watch(() => props.show, (newVal) => {
  console.log('[BaseModal] show changed to:', newVal)
  if (newVal) {
    setInitialFocus()
  }
}, { immediate: true })

// Lifecycle hooks
onMounted(() => {
  document.addEventListener('keydown', handleEscKey)
  document.addEventListener('keydown', handleKeyDown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', handleEscKey)
  document.removeEventListener('keydown', handleKeyDown)
})
</script>

<style scoped>
/* Modal transition animations */
.modal-enter-active,
.modal-leave-active {
  transition: opacity 0.3s ease;
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

.modal-enter-active .transform,
.modal-leave-active .transform {
  transition: transform 0.3s ease;
}

.modal-enter-from .transform,
.modal-leave-to .transform {
  transform: scale(0.95);
}
</style>
