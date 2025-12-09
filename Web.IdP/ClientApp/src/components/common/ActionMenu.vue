<script setup>
import { ref, nextTick, onUnmounted } from 'vue'

const props = defineProps({
  align: { type: String, default: 'right' }
})
const isOpen = ref(false)
const triggerRef = ref(null)
const menuRef = ref(null)
const position = ref({ top: 0, left: 0 })

const toggle = async () => {
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    await nextTick()
    updatePosition()
    window.addEventListener('scroll', updatePosition, true)
    window.addEventListener('resize', updatePosition)
  } else {
    cleanup()
  }
}

const close = () => {
  isOpen.value = false
  cleanup()
}

const cleanup = () => {
  window.removeEventListener('scroll', updatePosition, true)
  window.removeEventListener('resize', updatePosition)
}

const updatePosition = () => {
  if (!triggerRef.value || !menuRef.value) return
  const triggerRect = triggerRef.value.getBoundingClientRect()
  
  // Create a temporary ref to measure width if not visible yet (nextTick addresses this usually)
  // For fixed positioning, we use clientX/Y from the rect relative to viewport
  
  let left = triggerRect.right - (menuRef.value.offsetWidth || 200) // Align right
  const top = triggerRect.bottom + 4
  
  // Basic guard for viewport
  if (left < 0) left = triggerRect.left
  
  position.value = {
    top: top,
    left: left
  }
}

onUnmounted(cleanup)
</script>

<template>
  <div class="relative inline-block text-left">
    <div ref="triggerRef" @click.stop="toggle" class="cursor-pointer">
      <slot name="trigger"></slot>
    </div>

    <Teleport to="body">
      <div v-if="isOpen" class="fixed inset-0 z-40" @click="close"></div>
      <div
        v-if="isOpen"
        ref="menuRef"
        class="fixed z-50 mt-1 w-48 origin-top-right rounded-md bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none"
        :style="{ top: `${position.top}px`, left: `${position.left}px` }"
      >
        <div class="py-1" role="menu" aria-orientation="vertical">
          <slot name="content" :close="close"></slot>
        </div>
      </div>
    </Teleport>
  </div>
</template>
