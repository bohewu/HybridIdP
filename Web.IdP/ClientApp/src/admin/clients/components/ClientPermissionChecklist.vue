<script setup>
defineProps({
  id: { type: String, required: true },
  label: { type: String, required: true },
  permissions: { type: Array, required: true },
  selected: { type: Array, required: true }
})

defineEmits(['toggle'])
</script>

<template>
  <fieldset :id="id">
    <legend class="text-sm font-medium text-gray-700 mb-2">{{ label }}</legend>
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
      <div
        v-for="permission in permissions"
        :key="permission.value"
        class="relative flex items-start"
      >
        <div class="flex h-5 items-center">
          <input
            :id="permission.value"
            type="checkbox"
            :checked="selected.includes(permission.value)"
            class="h-4 w-4 rounded border-gray-300 text-google-500 focus:ring-google-500"
            @change="$emit('toggle', permission.value)"
          />
        </div>
        <label :for="permission.value" class="ml-3 text-sm font-medium text-gray-700">
          {{ $t(`clients.form.permissionLabels.${permission.labelKey}`) }}
        </label>
      </div>
    </div>
  </fieldset>
</template>
