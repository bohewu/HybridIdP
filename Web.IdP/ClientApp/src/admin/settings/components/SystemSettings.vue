<script setup>
import { ref, onMounted, computed } from 'vue';
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

const settings = ref({
    monitoringEnabled: true,
    activityInterval: 5,
    securityInterval: 10,
    metricsInterval: 15,
    auditRetentionDays: 30
});

// Original state to track changes (optional, but good for UX)
const originalSettings = ref({});

const hasChanges = computed(() => {
    return settings.value.monitoringEnabled !== originalSettings.value.monitoringEnabled ||
           settings.value.activityInterval !== originalSettings.value.activityInterval ||
           settings.value.securityInterval !== originalSettings.value.securityInterval ||
           settings.value.metricsInterval !== originalSettings.value.metricsInterval ||
           settings.value.auditRetentionDays !== originalSettings.value.auditRetentionDays;
});

const loadSettings = async () => {
    loading.value = true;
    error.value = null;
    try {
        // Load Monitoring Settings
        const monitoringPromise = fetch('/api/admin/settings?prefix=Monitoring.', {
            method: 'GET',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include'
        }).then(r => r.json());

        // Load Audit Settings (using prefix Audit.)
        const auditPromise = fetch('/api/admin/settings?prefix=Audit.', {
            method: 'GET',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include'
        }).then(r => r.json());

        const [monitoringData, auditData] = await Promise.all([monitoringPromise, auditPromise]);
        
        // Map Monitoring Data
        const getVal = (data, key, def) => data.find(s => s.key === key)?.value || def;

        settings.value.monitoringEnabled = getVal(monitoringData, 'Monitoring.Enabled', 'true').toLowerCase() === 'true';
        settings.value.activityInterval = parseInt(getVal(monitoringData, 'Monitoring.ActivityIntervalSeconds', '5'));
        settings.value.securityInterval = parseInt(getVal(monitoringData, 'Monitoring.SecurityIntervalSeconds', '10'));
        settings.value.metricsInterval = parseInt(getVal(monitoringData, 'Monitoring.MetricsIntervalSeconds', '15'));

        // Map Audit Data
        settings.value.auditRetentionDays = parseInt(getVal(auditData, SettingKeys.Audit.RetentionDays, '30'));

        // Save original copy
        originalSettings.value = { ...settings.value };

    } catch (err) {
        console.error('Failed to load settings', err);
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
            updateSetting('Monitoring.MetricsIntervalSeconds', settings.value.metricsInterval.toString()),
            updateSetting(SettingKeys.Audit.RetentionDays, settings.value.auditRetentionDays.toString())
        ];

        const results = await Promise.all(updates);
        
        // Check if any failed
        if (results.some(r => !r.ok)) throw new Error('One or more settings failed to save');

        // Invalidate cache
        await Promise.all([
            fetch('/api/admin/settings/invalidate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ key: 'Monitoring.' })
            }),
            fetch('/api/admin/settings/invalidate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ key: 'Audit.' })
            })
        ]);

        // Update originals
        originalSettings.value = { ...settings.value };
        
        showSuccess.value = true;
        setTimeout(() => {
            showSuccess.value = false;
        }, 3000);

    } catch (err) {
        console.error('Failed to save settings', err);
        error.value = t('settings.saveError', { message: err.message });
    } finally {
        saving.value = false;
    }
};

const cancelChanges = () => {
    if (hasChanges.value && confirm(t('settings.confirmCancel'))) {
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
            <h2 class="text-lg font-semibold text-gray-900">{{ t('settings.system.title') }}</h2>
            <p class="mt-1 text-sm text-gray-500">{{ t('settings.system.description') }}</p>
        </div>

        <!-- Loading State -->
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

            <!-- Audit Retention Section -->
            <div class="mb-6">
                <h3 class="text-md font-medium text-gray-900 mb-2">{{ t('settings.audit.title') }}</h3>
                <div class="form-control w-full max-w-sm">
                    <label class="block text-sm font-medium text-gray-700 mb-1">
                        {{ t('settings.audit.retention_days') }}
                    </label>
                    <input
                        type="number"
                        v-model.number="settings.auditRetentionDays"
                        min="0"
                        :disabled="!canUpdate"
                        class="block w-full rounded-md border-gray-300 focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed h-10 px-3"
                    />
                    <p class="mt-1 text-sm text-gray-500">{{ t('settings.audit.retention_days_desc') }}</p>
                </div>
            </div>

            <div class="divider my-4"></div>

            <!-- Monitoring Section -->
             <h3 class="text-md font-medium text-gray-900 mb-2">{{ t('settings.system.title') }}</h3>

            <!-- Monitoring Toggle -->
            <div class="form-control mb-6">
                <label class="label cursor-pointer justify-start gap-4">
                    <span class="label-text font-bold text-gray-700 mr-4">{{ t('settings.monitoring.enable') }}</span>
                    <input type="checkbox" class="toggle toggle-primary" v-model="settings.monitoringEnabled" :disabled="!canUpdate" />
                </label>
                <p class="text-sm text-gray-500 mt-1">{{ t('settings.monitoring.enable_desc') }}</p>
            </div>

            <!-- Intervals Grid -->
            <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
                <!-- Activity Interval -->
                <div class="form-control w-full">
                    <label class="block text-sm font-medium text-gray-700 mb-1">
                        {{ t('settings.monitoring.activity_interval') }}
                    </label>
                    <div class="flex items-center gap-2">
                        <input
                            type="number"
                            v-model.number="settings.activityInterval"
                            min="1"
                            :disabled="!canUpdate"
                            class="block w-full rounded-md border-gray-300 focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed h-10 px-3"
                        />
                        <span class="text-gray-500 sm:text-sm whitespace-nowrap">{{ t('settings.monitoring.seconds') }}</span>
                    </div>
                </div>

                <!-- Security Interval -->
                <div class="form-control w-full">
                    <label class="block text-sm font-medium text-gray-700 mb-1">
                        {{ t('settings.monitoring.security_interval') }}
                    </label>
                    <div class="flex items-center gap-2">
                        <input
                            type="number"
                            v-model.number="settings.securityInterval"
                            min="1"
                            :disabled="!canUpdate"
                            class="block w-full rounded-md border-gray-300 focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed h-10 px-3"
                        />
                        <span class="text-gray-500 sm:text-sm whitespace-nowrap">{{ t('settings.monitoring.seconds') }}</span>
                    </div>
                </div>

                <!-- Metrics Interval -->
                <div class="form-control w-full">
                    <label class="block text-sm font-medium text-gray-700 mb-1">
                        {{ t('settings.monitoring.metrics_interval') }}
                    </label>
                     <div class="flex items-center gap-2">
                        <input
                            type="number"
                            v-model.number="settings.metricsInterval"
                            min="1"
                            :disabled="!canUpdate"
                            class="block w-full rounded-md border-gray-300 focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed h-10 px-3"
                        />
                        <span class="text-gray-500 sm:text-sm whitespace-nowrap">{{ t('settings.monitoring.seconds') }}</span>
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
                    {{ t('settings.cancelButton') }}
                </button>
                <button 
                    data-testid="save-settings-btn"
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
