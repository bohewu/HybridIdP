import { onBeforeUnmount, ref } from 'vue'

export function useEmailCodeCooldown() {
  const remainingSeconds = ref(0)
  let intervalId

  function stopCooldown() {
    if (intervalId) {
      clearInterval(intervalId)
      intervalId = undefined
    }
    remainingSeconds.value = 0
  }

  function startCooldown(seconds) {
    stopCooldown()
    remainingSeconds.value = Math.max(0, Math.ceil(Number(seconds) || 0))

    if (remainingSeconds.value === 0) return

    intervalId = setInterval(() => {
      remainingSeconds.value = Math.max(0, remainingSeconds.value - 1)
      if (remainingSeconds.value === 0) stopCooldown()
    }, 1000)
  }

  onBeforeUnmount(stopCooldown)

  return {
    remainingSeconds,
    startCooldown,
    stopCooldown
  }
}
