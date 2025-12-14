<!-- PermissionGuard Component -->
<!-- Shows content only if user has required permission -->
<!-- Usage: <PermissionGuard :permission="'users.create'">...</PermissionGuard> -->

<template>
  <div v-if="hasPermission">
    <slot></slot>
  </div>
  <div v-else-if="showDenied" class="permission-denied">
    <slot name="denied">
      <!-- Default access denied message -->
      <div class="rounded-md bg-yellow-50 p-4 border border-yellow-200">
        <div class="flex">
          <div class="shrink-0">
            <svg class="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <h3 class="text-sm font-medium text-yellow-800">Access Denied</h3>
            <div class="mt-2 text-sm text-yellow-700">
              <p>You don't have permission to access this feature.</p>
              <p class="mt-1 text-xs" v-if="permission">Required permission: <code class="bg-yellow-100 px-1 rounded">{{ permission }}</code></p>
            </div>
          </div>
        </div>
      </div>
    </slot>
  </div>
</template>

<script setup>
import { computed } from 'vue';
import { permissionService } from '@/utils/permissionService';

const props = defineProps({
  permission: {
    type: String,
    required: false
  },
  permissions: {
    type: Array,
    required: false
  },
  requireAll: {
    type: Boolean,
    default: false
  },
  showDenied: {
    type: Boolean,
    default: false
  }
});

const hasPermission = computed(() => {
  if (props.permission) {
    return permissionService.hasPermission(props.permission);
  }
  
  if (props.permissions && props.permissions.length > 0) {
    return props.requireAll 
      ? permissionService.hasAllPermissions(props.permissions)
      : permissionService.hasAnyPermission(props.permissions);
  }
  
  // No permission specified, allow by default
  return true;
});
</script>
