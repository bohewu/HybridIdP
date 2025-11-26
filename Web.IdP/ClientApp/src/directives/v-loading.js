import { createApp } from 'vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import i18n from '@/i18n'

// Usage:
// v-loading="true"  -> show default overlay
// v-loading="{ loading: true, overlay: true, message: 'Loading...' }"

export default {
  mounted(el, binding) {
    // Ensure the host can be a positioned parent for absolute overlay
    const computed = window.getComputedStyle(el)
    const originalPosition = el.style.position || ''
    if (computed.position === '' || computed.position === 'static') {
      el.style.position = 'relative'
      el.__vLoading_orig_pos = originalPosition
    }

    // container for the mounted LoadingIndicator
    const container = document.createElement('div')
    // get options early (we used to read 'opts' below but referenced it before
    // initialization — move it up so we can safely choose the default overlay)
    const opts = binding.value && typeof binding.value === 'object' ? binding.value : {}
    // choose tailwind classes depending on overlay/inline (default overlay = true)
    const defaultOverlay = opts.overlay ?? true
    container.className = defaultOverlay
      ? 'absolute inset-0 bg-white/60 z-40 flex items-center justify-center pointer-events-auto v-loading-container'
      : 'flex items-center justify-center v-loading-container'
    // mount a small Vue app with the LoadingIndicator component so behavior stays consistent
    const initialLoading = typeof binding.value === 'object' ? !!(binding.value.loading ?? binding.value.value) : !!binding.value
    const app = createApp(LoadingIndicator, {
      loading: initialLoading,
      overlay: opts.overlay ?? true,
      message: opts.message ?? '',
      size: opts.size ?? 'md'
    })

    // ensure i18n is available inside the small app so LoadingIndicator can use useI18n()
    try { app.use(i18n) } catch (err) { console.debug('v-loading: failed to apply i18n (ignored)', err) }

    // expose instance proxy after mount
    const vm = app.mount(container)

    el.appendChild(container)
    // ensure visual visibility matches initialLoading
    container.style.display = initialLoading ? '' : 'none'

    // store references for updates / cleanup
    el.__vLoading = { container, app, vm }
    console.debug('[v-loading] mounted', { el, initialLoading })
  },

  updated(el, binding) {
    const ref = el.__vLoading
    if (!ref) return

    const opts = binding.value && typeof binding.value === 'object' ? binding.value : {}
    const newLoading = typeof binding.value === 'object' ? !!(binding.value.loading ?? binding.value.value) : !!binding.value

    // vm is the component instance proxy; update props directly
    console.debug('[v-loading] updated', { bindingValue: binding.value })
    try {
      if (ref.vm) {
        ref.vm.loading = newLoading
      }
      // ensure container is visible/hidden even if vm updates aren't applied synchronously
      if (ref.container) {
        ref.container.style.display = newLoading ? '' : 'none'
        if (opts.overlay !== undefined) {
          ref.vm.overlay = opts.overlay
          // update classes on the container to match overlay vs inline styles
          ref.container.className = opts.overlay
            ? 'absolute inset-0 bg-white/60 z-40 flex items-center justify-center pointer-events-auto v-loading-container'
            : 'flex items-center justify-center v-loading-container'
        }
        if (opts.message !== undefined) ref.vm.message = opts.message
        if (opts.size !== undefined) ref.vm.size = opts.size
      }
    } catch (err) {
      // fallback: if runtime doesn't allow direct updates, recreate app
      ref.app.unmount()
      ref.container.remove()
      el.__vLoading = null
      // call mounted to recreate
      this.mounted(el, binding)
    }
  },

  beforeUnmount(el) {
    const ref = el.__vLoading
    if (!ref) return
    try {
      ref.app.unmount()
    } catch (err) {
      // ignore
    }
    if (ref.container && ref.container.parentNode) ref.container.parentNode.removeChild(ref.container)
    console.debug('[v-loading] beforeUnmount cleaned', el)

    // restore original position style if we modified it
    if (el.__vLoading_orig_pos !== undefined) {
      el.style.position = el.__vLoading_orig_pos
      delete el.__vLoading_orig_pos
    }
    delete el.__vLoading
  }
}
