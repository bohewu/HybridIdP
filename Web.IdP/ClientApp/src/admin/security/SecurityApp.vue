<template>
  <div>
    <div v-if="loading" class="text-center p-8">
      <p>Loading security policies...</p>
    </div>
    <div v-if="error" class="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded relative mb-4" role="alert">
      <strong class="font-bold">Error:</strong>
      <span class="block sm:inline">{{ error }}</span>
    </div>

    <form v-if="!loading && policy" @submit.prevent="savePolicy">
      <div class="space-y-8">
        <!-- Password Requirements -->
        <div class="p-6 bg-white shadow rounded-lg">
          <h2 class="text-xl font-semibold mb-4">Password Requirements</h2>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label for="minPasswordLength" class="block text-sm font-medium text-gray-700">Minimum Length</label>
              <input type="number" id="minPasswordLength" v-model.number="policy.minPasswordLength" min="6" max="128" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
            </div>
            <div class="space-y-4">
              <h3 class="text-sm font-medium text-gray-700">Complexity</h3>
              <div class="flex items-center">
                <input id="requireUppercase" type="checkbox" v-model="policy.requireUppercase" class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500">
                <label for="requireUppercase" class="ml-2 block text-sm text-gray-900">Require Uppercase (A-Z)</label>
              </div>
              <div class="flex items-center">
                <input id="requireLowercase" type="checkbox" v-model="policy.requireLowercase" class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500">
                <label for="requireLowercase" class="ml-2 block text-sm text-gray-900">Require Lowercase (a-z)</label>
              </div>
              <div class="flex items-center">
                <input id="requireDigit" type="checkbox" v-model="policy.requireDigit" class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500">
                <label for="requireDigit" class="ml-2 block text-sm text-gray-900">Require Digit (0-9)</label>
              </div>
              <div class="flex items-center">
                <input id="requireNonAlphanumeric" type="checkbox" v-model="policy.requireNonAlphanumeric" class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500">
                <label for="requireNonAlphanumeric" class="ml-2 block text-sm text-gray-900">Require Non-Alphanumeric (!, $, #, %)</label>
              </div>
            </div>
          </div>
        </div>

        <!-- Password Lifecycle -->
        <div class="p-6 bg-white shadow rounded-lg">
          <h2 class="text-xl font-semibold mb-4">Password Lifecycle</h2>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div>
              <label for="passwordHistoryCount" class="block text-sm font-medium text-gray-700">Password History (0-24)</label>
              <input type="number" id="passwordHistoryCount" v-model.number="policy.passwordHistoryCount" min="0" max="24" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
              <p class="mt-1 text-xs text-gray-500">Number of previous passwords to remember. 0 to disable.</p>
            </div>
            <div>
              <label for="passwordExpirationDays" class="block text-sm font-medium text-gray-700">Password Expiration (Days)</label>
              <input type="number" id="passwordExpirationDays" v-model.number="policy.passwordExpirationDays" min="0" max="365" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
              <p class="mt-1 text-xs text-gray-500">0 to disable expiration.</p>
            </div>
            <div>
              <label for="minPasswordAgeDays" class="block text-sm font-medium text-gray-700">Minimum Password Age (Days)</label>
              <input type="number" id="minPasswordAgeDays" v-model.number="policy.minPasswordAgeDays" min="0" max="365" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
              <p class="mt-1 text-xs text-gray-500">Minimum days before a password can be changed again. 0 to disable.</p>
            </div>
          </div>
        </div>

        <!-- Account Lockout -->
        <div class="p-6 bg-white shadow rounded-lg">
          <h2 class="text-xl font-semibold mb-4">Account Lockout</h2>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label for="maxFailedAccessAttempts" class="block text-sm font-medium text-gray-700">Max Failed Access Attempts</label>
              <input type="number" id="maxFailedAccessAttempts" v-model.number="policy.maxFailedAccessAttempts" min="3" max="20" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
            </div>
            <div>
              <label for="lockoutDurationMinutes" class="block text-sm font-medium text-gray-700">Lockout Duration (Minutes)</label>
              <input type="number" id="lockoutDurationMinutes" v-model.number="policy.lockoutDurationMinutes" min="1" max="1440" class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm">
            </div>
          </div>
        </div>
      </div>

      <!-- Save Button -->
      <div class="mt-8 flex justify-end">
        <button type="submit" :disabled="isSaving" class="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50">
          <span v-if="isSaving">Saving...</span>
          <span v-else>Save Policies</span>
        </button>
      </div>
      <div v-if="saveSuccess" class="mt-4 text-green-600">
        Policies saved successfully!
      </div>
    </form>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';

const policy = ref(null);
const loading = ref(true);
const error = ref(null);
const isSaving = ref(false);
const saveSuccess = ref(false);

const fetchPolicy = async () => {
  loading.value = true;
  error.value = null;
  try {
    const response = await fetch('/api/admin/security/policies');
    if (!response.ok) {
      throw new Error(`Failed to fetch policies: ${response.statusText}`);
    }
    policy.value = await response.json();
  } catch (e) {
    error.value = e.message;
  } finally {
    loading.value = false;
  }
};

const savePolicy = async () => {
  isSaving.value = true;
  error.value = null;
  saveSuccess.value = false;
  try {
    const response = await fetch('/api/admin/security/policies', {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(policy.value),
    });
    if (!response.ok) {
      const errorData = await response.json();
      // Handle validation errors from the API
      const errorMessages = Object.values(errorData.errors).flat().join(', ');
      throw new Error(errorMessages || `Failed to save policies: ${response.statusText}`);
    }
    saveSuccess.value = true;
    setTimeout(() => saveSuccess.value = false, 3000);
  } catch (e) {
    error.value = e.message;
  } finally {
    isSaving.value = false;
  }
};

onMounted(fetchPolicy);
</script>
