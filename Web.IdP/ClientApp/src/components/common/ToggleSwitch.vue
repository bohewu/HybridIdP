<template>
  <button
    type="button"
    :class="[enabled ? 'bg-indigo-600' : 'bg-gray-200', 'relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2']"
    role="switch"
    :aria-checked="enabled"
    @click="toggle"
  >
    <span
      aria-hidden="true"
      :class="[enabled ? 'translate-x-5' : 'translate-x-0', 'pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out']"
    />
  </button>
</template>

<script setup>
import { computed } from 'vue';

const props = defineProps({
  modelValue: {
    type: Boolean,
    required: true,
  },
});

const emit = defineEmits(['update:modelValue']);

const enabled = computed(() => props.modelValue);

function toggle() {
  emit('update:modelValue', !enabled.value);
}
</script>
