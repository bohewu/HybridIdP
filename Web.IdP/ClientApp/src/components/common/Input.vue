<template>
  <div class="relative">
    <div v-if="hasPrefix" class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
      <slot name="prefix" />
    </div>
    <input
      :type="type"
      :value="modelValue"
      @input="$emit('update:modelValue', $event.target.value)"
      :class="[inputClasses, { 'pl-10': hasPrefix, 'pr-10': hasSuffix }]"
      :placeholder="placeholder"
    />
    <div v-if="hasSuffix" class="absolute inset-y-0 right-0 pr-3 flex items-center">
      <slot name="suffix" />
    </div>
  </div>
</template>

<script setup>
import { computed, useSlots } from 'vue'

defineProps({
  modelValue: String,
  type: {
    type: String,
    default: 'text'
  },
  placeholder: String
})

defineEmits(['update:modelValue'])

const slots = useSlots()

const hasPrefix = computed(() => !!slots.prefix)
const hasSuffix = computed(() => !!slots.suffix)

const inputClasses = computed(() => {
  return 'block w-full rounded-md border-gray-300 shadow-sm focus:border-google-500 focus:ring-google-500 sm:text-sm transition-colors h-10'
})
</script>
