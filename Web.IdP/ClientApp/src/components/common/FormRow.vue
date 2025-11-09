<template>
  <div class="py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:py-5 sm:px-6">
    <dt class="text-sm font-medium text-gray-500">
      <label :for="forId">{{ label }}</label>
    </dt>
    <dd class="mt-1 text-sm text-gray-900 sm:col-span-2 sm:mt-0">
      <slot></slot>
      <p v-if="helpText" class="mt-2 text-xs text-gray-500">{{ helpText }}</p>
    </dd>
  </div>
</template>

<script setup>
defineProps({
  label: {
    type: String,
    required: true,
  },
  forId: {
    type: String,
    default: null,
  },
  helpText: {
    type: String,
    default: '',
  },
});
</script>
