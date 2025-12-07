<script setup>
import { ref, onMounted, computed } from 'vue';
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

const settings = ref({
    monitoringEnabled: true,
    activityInterval: 5,
    securityInterval: 10,
    metricsInterval: 15
});

// Original state to track changes (optional, but good for UX)
const originalSettings = ref({});

const hasChanges = computed(() => {
    return settings.value.monitoringEnabled !== originalSettings.value.monitoringEnabled ||
           settings.value.activityInterval !== originalSettings.value.activityInterval ||
           settings.value.securityInterval !== originalSettings.value.securityInterval ||
           settings.value.metricsInterval !== originalSettings.value.metricsInterval;
});

const loadSettings = async () => {
    loading.value = true;
    error.value = null;
    try {
        const response = await fetch('/api/admin/settings?prefix=Monitoring.', {
            method: 'GET',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include'
        });

        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        
        const data = await response.json();
        
        // Map response array to settings object
        const enabledSetting = data.find(s => s.key === 'Monitoring.Enabled');
        if (enabledSetting) settings.value.monitoringEnabled = enabledSetting.value.toLowerCase() === 'true';

        const activitySetting = data.find(s => s.key === 'Monitoring.ActivityIntervalSeconds');
        if (activitySetting) settings.value.activityInterval = parseInt(activitySetting.value);

        const securitySetting = data.find(s => s.key === 'Monitoring.SecurityIntervalSeconds');
        if (securitySetting) settings.value.securityInterval = parseInt(securitySetting.value);

        const metricsSetting = data.find(s => s.key === 'Monitoring.MetricsIntervalSeconds');
        if (metricsSetting) settings.value.metricsInterval = parseInt(metricsSetting.value);

        // Save original copy
        originalSettings.value = { ...settings.value };

    } catch (err) {
        console.error('Failed to load settings', err);
        error.value = t('admin.settings.loadingError', { message: err.message });
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
        // Helper for fetch updates
        const updateSetting = (key, value) => fetch(`/api/admin/settings/${key}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ value: value })
        });

        const updates = [
            updateSetting('Monitoring.Enabled', settings.value.monitoringEnabled.toString()),
            updateSetting('Monitoring.ActivityIntervalSeconds', settings.value.activityInterval.toString()),
            updateSetting('Monitoring.SecurityIntervalSeconds', settings.value.securityInterval.toString()),
            updateSetting('Monitoring.MetricsIntervalSeconds', settings.value.metricsInterval.toString())
        ];

        const results = await Promise.all(updates);
        
        // Check if any failed
        if (results.some(r => !r.ok)) throw new Error('One or more settings failed to save');

        // Invalidate cache
        await fetch('/api/admin/settings/invalidate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ key: 'Monitoring.' })
        });

        // Update originals
        originalSettings.value = { ...settings.value };
        
        showSuccess.value = true;
        setTimeout(() => {
            showSuccess.value = false;
        }, 3000);

    } catch (err) {
        console.error('Failed to save settings', err);
        error.value = t('admin.settings.saveError', { message: err.message });
    } finally {
        saving.value = false;
    }
};

const cancelChanges = () => {
    if (hasChanges.value && confirm(t('admin.settings.confirmCancel'))) {
         settings.value = { ...originalSettings.value };
    } else if (hasChanges.value) {
        // do nothing if not confirmed
    } else {
         settings.value = { ...originalSettings.value };
    }
}

onMounted(() => {
    loadSettings();
});
</script>

<template>
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <!-- Section Header -->
        <div class="border-b border-gray-200 p-4">
            <h2 class="text-lg font-semibold text-gray-900">{{ t('admin.settings.system.title') }}</h2>
            <p class="mt-1 text-sm text-gray-500">{{ t('admin.settings.system.description') }}</p>
        </div>

        <!-- Loading State -->
        <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('admin.settings.loading')" />

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
                        <p class="text-sm text-green-700">{{ t('admin.settings.saveSuccess') }}</p>
                    </div>
                </div>
            </div>

            <!-- Monitoring Toggle -->
            <div class="form-control mb-6">
                <label class="label cursor-pointer justify-start gap-4">
                    <span class="label-text font-bold text-gray-700">{{ t('admin.settings.monitoring.enable') }}</span>
                    <input type="checkbox" class="toggle toggle-primary" v-model="settings.monitoringEnabled" :disabled="!canUpdate" />
                </label>
                <p class="text-sm text-gray-500 mt-1">{{ t('admin.settings.monitoring.enable_desc') }}</p>
            </div>

            <div class="divider my-4"></div>

            <!-- Intervals Grid -->
            <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
                <!-- Activity Interval -->
                <div class="form-control w-full">
                    <label class="label">
                        <span class="label-text font-medium text-gray-700">{{ t('admin.settings.monitoring.activity_interval') }}</span>
                    </label>
                    <div class="join">
                        <input type="number" class="input input-bordered w-full join-item" v-model.number="settings.activityInterval" min="1" :disabled="!canUpdate" />
                        <span class="btn btn-static join-item bg-base-200">{{ t('admin.settings.monitoring.seconds') }}</span>
                    </div>
                </div>

                <!-- Security Interval -->
                <div class="form-control w-full">
                    <label class="label">
                        <span class="label-text font-medium text-gray-700">{{ t('admin.settings.monitoring.security_interval') }}</span>
                    </label>
                    <div class="join">
                        <input type="number" class="input input-bordered w-full join-item" v-model.number="settings.securityInterval" min="1" :disabled="!canUpdate" />
                        <span class="btn btn-static join-item bg-base-200">{{ t('admin.settings.monitoring.seconds') }}</span>
                    </div>
                </div>

                <!-- Metrics Interval -->
                <div class="form-control w-full">
                    <label class="label">
                        <span class="label-text font-medium text-gray-700">{{ t('admin.settings.monitoring.metrics_interval') }}</span>
                    </label>
                    <div class="join">
                        <input type="number" class="input input-bordered w-full join-item" v-model.number="settings.metricsInterval" min="1" :disabled="!canUpdate" />
                        <span class="btn btn-static join-item bg-base-200">{{ t('admin.settings.monitoring.seconds') }}</span>
                    </div>
                </div>
            </div>

            <!-- Action Buttons -->
            <div v-if="canUpdate" class="mt-8 flex items-center justify-end gap-3">
                 <button
                    type="button"
                    :disabled="!hasChanges || saving"
                    @click="cancelChanges"
                    class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    {{ t('admin.settings.cancelButton') }}
                </button>
                <button 
                    class="btn btn-primary" 
                    @click="saveSettings" 
                    :disabled="saving || loading || !hasChanges">
                    <span v-if="saving" class="loading loading-spinner"></span>
                    {{ t('common.save') }}
                </button>
            </div>
        </div>
    </div>
</template>
