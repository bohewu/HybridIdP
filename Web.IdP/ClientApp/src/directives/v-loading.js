import { createApp, h } from 'vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import i18n from '@/i18n'

// Usage:
// v-loading="true"  -> show default overlay
// v-loading="{ loading: true, overlay: true, message: 'Loading...' }"

// Helper function to mount the loader
function mountLoader(el, binding) {
  // Ensure the host can be a positioned parent for absolute overlay
  const computed = window.getComputedStyle(el)
  const originalPosition = el.style.position || ''
  if (computed.position === '' || computed.position === 'static') {
    el.style.position = 'relative'
    el.__vLoading_orig_pos = originalPosition
  }

  // container for the mounted LoadingIndicator
  const container = document.createElement('div')
  const opts = binding.value && typeof binding.value === 'object' ? binding.value : {}
  const defaultOverlay = opts.overlay ?? true
  container.className = defaultOverlay
    ? 'absolute inset-0 bg-white/60 z-40 flex items-center justify-center pointer-events-auto v-loading-container'
    : 'flex items-center justify-center v-loading-container'
  
  const initialLoading = typeof binding.value === 'object' ? !!(binding.value.loading ?? binding.value.value) : !!binding.value
  
  // Wrapper component to manage state
  const Wrapper = {
    data() {
      return {
        loading: initialLoading,
        overlay: opts.overlay ?? true,
        message: opts.message ?? '',
        size: opts.size ?? 'md',
        showMessage: !!opts.message
      }
    },
    render() {
      return h(LoadingIndicator, {
        loading: this.loading,
        overlay: this.overlay,
        message: this.message,
        size: this.size,
        showMessage: this.showMessage
      })
    }
  }
  
  const app = createApp(Wrapper)

  try { app.use(i18n) } catch (err) { console.debug('v-loading: failed to apply i18n (ignored)', err) }

  const vm = app.mount(container)

  el.appendChild(container)
  container.style.display = initialLoading ? '' : 'none'

  el.__vLoading = { container, app, vm }
}

export default {
  mounted(el, binding) {
    mountLoader(el, binding)
  },

  updated(el, binding) {
    const ref = el.__vLoading
    if (!ref) {
      // If reference lost, remount
      mountLoader(el, binding)
      return
    }

    const opts = binding.value && typeof binding.value === 'object' ? binding.value : {}
    const newLoading = typeof binding.value === 'object' ? !!(binding.value.loading ?? binding.value.value) : !!binding.value

    try {
      if (ref.vm) {
        ref.vm.loading = newLoading
        if (opts.overlay !== undefined) {
          ref.vm.overlay = opts.overlay
          ref.vm.size = opts.size ?? 'md' // Ensure size is updated
        }
        if (opts.message !== undefined) {
          ref.vm.message = opts.message
          ref.vm.showMessage = !!opts.message
        }
      }
      
      if (ref.container) {
        ref.container.style.display = newLoading ? '' : 'none'
        if (opts.overlay !== undefined) {
          ref.container.className = opts.overlay
            ? 'absolute inset-0 bg-white/60 z-40 flex items-center justify-center pointer-events-auto v-loading-container'
            : 'flex items-center justify-center v-loading-container'
        }
      }
    } catch (err) {
      // fallback: if runtime doesn't allow direct updates, recreate app
      console.warn('[v-loading] update failed, recreating', err)
      try {
          if (ref.app) ref.app.unmount()
          if (ref.container && ref.container.parentNode) ref.container.parentNode.removeChild(ref.container)
      } catch (ignored) {}
      
      el.__vLoading = null
      mountLoader(el, binding)
    }
  },

  beforeUnmount(el) {
    const ref = el.__vLoading
    if (!ref) return
    try {
      ref.app.unmount()
    } catch (err) {}
    if (ref.container && ref.container.parentNode) ref.container.parentNode.removeChild(ref.container)
    
    if (el.__vLoading_orig_pos !== undefined) {
      el.style.position = el.__vLoading_orig_pos
      delete el.__vLoading_orig_pos
    }
    delete el.__vLoading
  }
}
