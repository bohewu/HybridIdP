<template>
  <label class="flex items-center cursor-pointer" :title="title">
    <div class="relative">
      <input 
        type="checkbox" 
        class="sr-only peer" 
        :checked="modelValue"
        @change="$emit('update:modelValue', $event.target.checked)"
      >
      <!-- Track -->
      <div class="w-10 h-5 rounded-full shadow-inner transition-colors bg-gray-200 peer-checked:bg-amber-400"></div>
      <!-- Dot -->
      <div class="dot absolute left-1 top-1 bg-white w-3 h-3 rounded-full shadow transition-transform transform peer-checked:translate-x-5"></div>
    </div>
    <span v-if="label" class="ml-2 text-xs font-medium" :class="modelValue ? 'text-amber-600' : 'text-gray-500'">
      {{ label }}
    </span>
  </label>
</template>

<script setup>
import { defineProps, defineEmits } from 'vue';

defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  label: {
    type: String,
    default: ''
  },
  title: {
    type: String,
    default: ''
  }
});

defineEmits(['update:modelValue']);
</script>