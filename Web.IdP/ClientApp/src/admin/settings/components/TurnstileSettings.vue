<script setup>
import { ref, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import LoadingIndicator from '@/components/common/LoadingIndicator.vue';
import { SettingKeys } from '@/utils/settingKeys';

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
const turnstileEnabled = ref(false);
const originalEnabled = ref(false);

const hasChanges = ref(false);

const loadSettings = async () => {
    loading.value = true;
    error.value = null;
    try {
        const response = await fetch('/api/admin/settings?prefix=Turnstile.', {
            method: 'GET',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include'
        });
        if (!response.ok) throw new Error('Failed to load settings');
        const data = await response.json();
        
        const enabledSetting = data.find(s => s.key === SettingKeys.Turnstile.Enabled);
        turnstileEnabled.value = enabledSetting?.value?.toLowerCase() === 'true';
        originalEnabled.value = turnstileEnabled.value;
        hasChanges.value = false;
    } catch (err) {
        console.error('Failed to load Turnstile settings', err);
        error.value = t('settings.loadingError', { message: err.message });
    } finally {
        loading.value = false;
    }
};

const saveSettings = async () => {
    if (!hasChanges.value) return;

    saving.value = true;
    error.value = null;
    showSuccess.value = false;

    try {
        const response = await fetch(`/api/admin/settings/${SettingKeys.Turnstile.Enabled}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ value: turnstileEnabled.value.toString() })
        });

        if (!response.ok) {
            const msg = await response.text();
            throw new Error(msg || 'Failed to save setting');
        }

        // Invalidate cache
        await fetch('/api/admin/settings/invalidate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ key: 'Turnstile.' })
        });

        originalEnabled.value = turnstileEnabled.value;
        hasChanges.value = false;
        showSuccess.value = true;
        setTimeout(() => showSuccess.value = false, 3000);

    } catch (err) {
        console.error('Failed to save Turnstile settings', err);
        error.value = t('settings.saveError', { message: err.message });
    } finally {
        saving.value = false;
    }
};

const onToggleChange = () => {
    hasChanges.value = turnstileEnabled.value !== originalEnabled.value;
};

const cancelChanges = () => {
    if (hasChanges.value) {
        turnstileEnabled.value = originalEnabled.value;
        hasChanges.value = false;
    }
};

onMounted(() => {
    loadSettings();
});
</script>

<template>
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <!-- Section Header -->
        <div class="border-b border-gray-200 p-4">
            <h2 class="text-lg font-semibold text-gray-900">{{ t('settings.turnstile.title') }}</h2>
            <p class="mt-1 text-sm text-gray-500">{{ t('settings.turnstile.description') }}</p>
        </div>

        <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('settings.loading')" />

        <div v-else class="p-4">
             <!-- Error Alert -->
            <div v-if="error" class="mb-4 bg-red-50 border border-red-200 rounded-lg p-3">
                <div class="flex">
                    <div class="flex-shrink-0">
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
                    <div class="flex-shrink-0">
                         <svg class="h-5 w-5 text-green-400" viewBox="0 0 20 20" fill="currentColor">
                            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
                        </svg>
                    </div>
                    <div class="ml-3">
                        <p class="text-sm text-green-700">{{ t('settings.saveSuccess') }}</p>
                    </div>
                </div>
            </div>

            <!-- Important Hint -->
            <div class="mb-4 bg-amber-50 border border-amber-200 rounded-lg p-3">
                <div class="flex">
                    <div class="flex-shrink-0">
                        <svg class="h-5 w-5 text-amber-400" viewBox="0 0 20 20" fill="currentColor">
                            <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                        </svg>
                    </div>
                    <div class="ml-3">
                        <p class="text-sm text-amber-700">{{ t('settings.turnstile.hint') }}</p>
                    </div>
                </div>
            </div>

            <!-- Enable Toggle -->
            <div class="form-control">
                <label class="label cursor-pointer justify-start gap-4">
                    <span class="label-text font-bold text-gray-700 mr-4">{{ t('settings.turnstile.enable') }}</span>
                    <input 
                        type="checkbox" 
                        class="toggle toggle-primary" 
                        v-model="turnstileEnabled" 
                        :disabled="!canUpdate"
                        @change="onToggleChange" 
                    />
                </label>
                <p class="text-sm text-gray-500 mt-1">{{ t('settings.turnstile.enableDesc') }}</p>
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
                    @click="saveSettings" 
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
