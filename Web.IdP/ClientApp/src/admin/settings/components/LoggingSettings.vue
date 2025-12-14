<script setup>
import { ref, onMounted, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import LoadingIndicator from '@/components/common/LoadingIndicator.vue';

const props = defineProps({
  canUpdate: {
    type: Boolean,
    default: false
  }
});

const { t } = useI18n();
const saving = ref(false);
const loading = ref(true);
const showSuccess = ref(false);
const error = ref(null);
const currentLevel = ref('');
const originalLevel = ref('');

const logLevels = [
    { value: 'Verbose', label: 'Verbose' },
    { value: 'Debug', label: 'Debug' },
    { value: 'Information', label: 'Information' },
    { value: 'Warning', label: 'Warning' },
    { value: 'Error', label: 'Error' },
    { value: 'Fatal', label: 'Fatal' }
];

const hasChanges = ref(false);

watch(currentLevel, (newVal) => {
    hasChanges.value = newVal !== originalLevel.value;
});

const loadLevel = async () => {
    loading.value = true;
    error.value = null;
    try {
        const response = await fetch('/api/admin/logging/level', {
            method: 'GET',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include'
        });
        if (!response.ok) throw new Error('Failed to load log level');
        const data = await response.json();
        currentLevel.value = data.level;
        originalLevel.value = data.level;
        hasChanges.value = false;
    } catch (err) {
        console.error('Failed to load logging settings', err);
        error.value = t('settings.loadingError', { message: err.message });
    } finally {
        loading.value = false;
    }
};

const saveLevel = async () => {
    if (!hasChanges.value) return;

    saving.value = true;
    error.value = null;
    showSuccess.value = false;

    try {
        const response = await fetch('/api/admin/logging/level', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ level: currentLevel.value })
        });

        if (!response.ok) {
            const msg = await response.text();
            throw new Error(msg || 'Failed to save log level');
        }

        originalLevel.value = currentLevel.value;
        hasChanges.value = false;
        showSuccess.value = true;
        setTimeout(() => showSuccess.value = false, 3000);

    } catch (err) {
        console.error('Failed to save logging settings', err);
        error.value = t('settings.saveError', { message: err.message });
    } finally {
        saving.value = false;
    }
};

const cancelChanges = () => {
    if (hasChanges.value) {
        currentLevel.value = originalLevel.value;
    }
};

onMounted(() => {
    loadLevel();
});
</script>

<template>
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <!-- Section Header -->
        <div class="border-b border-gray-200 p-4">
            <h2 class="text-lg font-semibold text-gray-900">{{ t('settings.logging.title') }}</h2>
            <p class="mt-1 text-sm text-gray-500">{{ t('settings.logging.description') }}</p>
        </div>

        <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('settings.loading')" />

        <div v-else class="p-4">
             <!-- Error Alert -->
            <div v-if="error" class="mb-4 bg-red-50 border border-red-200 rounded-lg p-3">
                <div class="flex">
                    <div class="shrink-0">
                        <svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                             <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
                        </svg>
                    </div>
                    <div class="ml-3">
                        <p class="text-sm text-red-700">{{ error }}</p>
                    </div>
                </div>
            </div>

            <!-- Success Alert -->
            <div v-if="showSuccess" class="mb-4 bg-green-50 border border-green-200 rounded-lg p-3">
                <div class="flex">
                    <div class="shrink-0">
                         <svg class="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
                            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
                        </svg>
                    </div>
                    <div class="ml-3">
                        <p class="text-sm text-green-700">{{ t('settings.saveSuccess') }}</p>
                    </div>
                </div>
            </div>

            <div class="form-control w-full max-w-sm">
                <label for="globalLevel" class="block text-sm font-medium text-gray-700 mb-1">
                    {{ t('settings.logging.globalLevel') }}
                </label>
                <select 
                    id="globalLevel"
                    v-model="currentLevel" 
                    :disabled="!canUpdate"
                    class="block w-full rounded-md border-gray-300 focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100 h-10 px-3"
                >
                    <option v-for="level in logLevels" :key="level.value" :value="level.value">
                        {{ level.label }}
                    </option>
                </select>
                <p class="mt-1 text-sm text-gray-500">
                    {{ t('settings.logging.globalLevelDesc') }}
                </p>
            </div>

            <!-- Action Buttons -->
            <div v-if="canUpdate" class="mt-8 flex items-center justify-end gap-3">
                 <button
                    type="button"
                    :disabled="!hasChanges || saving"
                    @click="cancelChanges"
                    class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    {{ t('settings.cancelButton') }}
                </button>
                <button 
                    class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md shadow-sm hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed flex items-center" 
                    @click="saveLevel" 
                    :disabled="saving || loading || !hasChanges">
                    <svg v-if="saving" class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    {{ saving ? t('settings.saving') : t('settings.saveButton') }}
                </button>
            </div>
        </div>
    </div>
</template>
