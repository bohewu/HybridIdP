<script setup>
defineProps({ message: { type: String, required: true } })
</script>

<template>
  <div class="min-h-32 rounded-lg bg-gray-50 p-4" role="status" aria-live="polite" aria-busy="true">
    <p class="text-sm text-gray-600">{{ message }}</p>
    <div class="mt-4 space-y-3" aria-hidden="true">
      <div class="h-3 w-2/3 rounded bg-gray-200"></div>
      <div class="h-3 w-full rounded bg-gray-200"></div>
      <div class="h-3 w-1/2 rounded bg-gray-200"></div>
    </div>
  </div>
</template>
