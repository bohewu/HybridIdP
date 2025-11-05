<!-- AccessDeniedDialog Component -->
<!-- Shows when user tries to access a feature without permission -->

<template>
  <!-- Fixed fullscreen modal overlay with highest z-index -->
  <div 
    v-if="show" 
    class="fixed inset-0 z-[9999] overflow-y-auto" 
    aria-labelledby="modal-title" 
    role="dialog" 
    aria-modal="true"
  >
    <!-- Background overlay with blur -->
    <div class="fixed inset-0 bg-gray-900 bg-opacity-50 backdrop-blur-sm transition-opacity" @click="close"></div>

    <!-- Center container -->
    <div class="flex min-h-full items-center justify-center p-4">
      <!-- Modal panel -->
      <div class="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:w-full sm:max-w-lg">
        <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
          <div class="sm:flex sm:items-start">
            <!-- Warning icon -->
            <div class="mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full bg-yellow-100 sm:mx-0 sm:h-10 sm:w-10">
              <svg class="h-6 w-6 text-yellow-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
            </div>

            <!-- Content -->
            <div class="mt-3 text-center sm:ml-4 sm:mt-0 sm:text-left">
              <h3 class="text-lg font-medium leading-6 text-gray-900" id="modal-title">
                Access Denied
              </h3>
              <div class="mt-2">
                <p class="text-sm text-gray-500">
                  {{ message || "You don't have permission to access this feature." }}
                </p>
                <p v-if="requiredPermission" class="mt-2 text-xs text-gray-400">
                  Required permission: <code class="rounded bg-gray-100 px-2 py-1 text-gray-700">{{ requiredPermission }}</code>
                </p>
                <p v-if="contactAdmin" class="mt-2 text-xs text-gray-500">
                  Please contact your administrator if you believe you should have access.
                </p>
              </div>
            </div>
          </div>
        </div>

        <!-- Actions -->
        <div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6">
          <button 
            type="button" 
            @click="close"
            class="inline-flex w-full justify-center rounded-md bg-indigo-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:ml-3 sm:w-auto sm:text-sm"
          >
            OK
          </button>
          <button 
            v-if="showCancel"
            type="button" 
            @click="close"
            class="mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 sm:mt-0 sm:w-auto sm:text-sm"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  show: {
    type: Boolean,
    required: true
  },
  message: {
    type: String,
    default: ''
  },
  requiredPermission: {
    type: String,
    default: ''
  },
  contactAdmin: {
    type: Boolean,
    default: true
  },
  showCancel: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(['close']);

const close = () => {
  emit('close');
};
</script>
