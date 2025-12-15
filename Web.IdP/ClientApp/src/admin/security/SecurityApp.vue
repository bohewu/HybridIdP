<template>
  <div class="px-4 sm:px-6 lg:px-8 py-8"
       v-loading="{ loading: loading, overlay: true, message: $t('security.loading') }">
    <!-- Page Header -->
    <PageHeader :title="$t('security.pageTitle')" :subtitle="$t('security.pageSubtitle')">
      <template #actions>
        <button
          type="button"
          @click="savePolicy"
          data-testid="save-policy-btn"
          :disabled="isSaving || !isDirty"
          class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
        >
          <svg v-if="isSaving" class="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
          {{ isSaving ? $t('security.saving') : $t('security.saveButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- Notification Area -->
    <div v-if="notification.message" :class="notification.type === 'success' ? 'bg-green-50 border-green-400' : 'bg-red-50 border-red-400'" class="mb-4 border-l-4 p-4" role="alert">
      <p :class="notification.type === 'success' ? 'text-green-700' : 'text-red-700'" class="text-sm">{{ notification.message }}</p>
    </div>

    <!-- Main Content -->
    <div v-if="!loading" class="space-y-6">
      <!-- Registration Settings -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <div class="px-4 py-5 sm:px-6">
          <h3 class="text-lg leading-6 font-medium text-gray-900">{{ $t('security.registrationSettings') || 'Registration Settings' }}</h3>
        </div>
        <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
          <dl class="sm:divide-y sm:divide-gray-200">
            <FormRow :label="$t('security.registrationEnabled') || 'Allow Public Registration'" :help-text="$t('security.registrationEnabledHelp') || 'When disabled, new users cannot register accounts.'">
              <ToggleSwitch v-model="registrationEnabled" @update:modelValue="onRegistrationToggle" />
            </FormRow>
          </dl>
        </div>
      </div>

      <!-- Password Requirements -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <div class="px-4 py-5 sm:px-6">
          <h3 class="text-lg leading-6 font-medium text-gray-900">{{ $t('security.passwordRequirements') }}</h3>
        </div>
        <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
          <dl class="sm:divide-y sm:divide-gray-200">
            <FormRow :label="$t('security.minLength')" for-id="minLength">
              <input type="number" id="minLength" v-model.number="policy.minPasswordLength" min="6" max="128" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.requireUppercase')">
              <ToggleSwitch v-model="policy.requireUppercase" />
            </FormRow>
            <FormRow :label="$t('security.requireLowercase')">
              <ToggleSwitch v-model="policy.requireLowercase" />
            </FormRow>
            <FormRow :label="$t('security.requireDigit')">
              <ToggleSwitch v-model="policy.requireDigit" />
            </FormRow>
            <FormRow :label="$t('security.requireNonAlphanumeric')">
              <ToggleSwitch v-model="policy.requireNonAlphanumeric" />
            </FormRow>
            <FormRow :label="$t('security.minCharacterTypes')" for-id="minCharacterTypes" :help-text="$t('security.minCharacterTypesHelp')">
              <input type="number" id="minCharacterTypes" v-model.number="policy.minCharacterTypes" min="2" max="4" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
          </dl>
        </div>
      </div>

      <!-- Password History & Expiration -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <div class="px-4 py-5 sm:px-6">
          <h3 class="text-lg leading-6 font-medium text-gray-900">{{ $t('security.passwordHistory') }}</h3>
        </div>
        <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
          <dl class="sm:divide-y sm:divide-gray-200">
            <FormRow :label="$t('security.historyCount')" for-id="historyCount" :help-text="$t('security.historyCountHelp')">
              <input type="number" id="historyCount" v-model.number="policy.passwordHistoryCount" min="0" max="24" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.expirationDays')" for-id="expirationDays" :help-text="$t('security.expirationDaysHelp')">
              <input type="number" id="expirationDays" v-model.number="policy.passwordExpirationDays" min="0" max="365" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.minAgeDays')" for-id="minAgeDays" :help-text="$t('security.minAgeDaysHelp')">
              <input type="number" id="minAgeDays" v-model.number="policy.minPasswordAgeDays" min="0" max="365" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.allowSelfPasswordChange')" :help-text="$t('security.allowSelfPasswordChangeHelp')">
              <ToggleSwitch v-model="policy.allowSelfPasswordChange" />
            </FormRow>
          </dl>
        </div>
      </div>

      <!-- Account Lockout -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <div class="px-4 py-5 sm:px-6">
          <h3 class="text-lg leading-6 font-medium text-gray-900">{{ $t('security.accountLockout') }}</h3>
        </div>
        <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
          <dl class="sm:divide-y sm:divide-gray-200">
            <FormRow :label="$t('security.maxFailedAttempts')" for-id="maxFailedAttempts" :help-text="$t('security.maxFailedAttemptsHelp')">
              <input type="number" id="maxFailedAttempts" v-model.number="policy.maxFailedAccessAttempts" min="3" max="20" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.lockoutDuration')" for-id="lockoutDuration" :help-text="$t('security.lockoutDurationHelp')">
              <input type="number" id="lockoutDuration" v-model.number="policy.lockoutDurationMinutes" min="1" max="1440" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
          </dl>
        </div>
      </div>

      <!-- Abnormal Login Detection -->
      <div class="bg-white shadow-sm rounded-lg border border-gray-200">
        <div class="px-4 py-5 sm:px-6">
          <h3 class="text-lg leading-6 font-medium text-gray-900">{{ $t('security.abnormalLoginDetection') }}</h3>
        </div>
        <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
          <dl class="sm:divide-y sm:divide-gray-200">
            <FormRow :label="$t('security.abnormalLoginHistoryCount')" for-id="abnormalLoginHistoryCount" :help-text="$t('security.abnormalLoginHistoryCountHelp')">
              <input type="number" id="abnormalLoginHistoryCount" v-model.number="policy.abnormalLoginHistoryCount" min="1" max="100" class="block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm h-10 px-3">
            </FormRow>
            <FormRow :label="$t('security.blockAbnormalLogin')" :help-text="$t('security.blockAbnormalLoginHelp')">
              <ToggleSwitch v-model="policy.blockAbnormalLogin" />
            </FormRow>
          </dl>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, watch, computed } from 'vue';
import { useI18n } from 'vue-i18n';
import PageHeader from '@/components/common/PageHeader.vue';
import FormRow from '@/components/common/FormRow.vue';
import ToggleSwitch from '@/components/common/ToggleSwitch.vue';
import { SettingKeys } from '@/utils/settingKeys';

const { t } = useI18n();

const policy = ref(null);
const originalPolicy = ref(null);
const loading = ref(true);
const isSaving = ref(false);
const notification = ref({ message: '', type: '' });
const registrationEnabled = ref(true);
const originalRegistrationEnabled = ref(true);

const isDirty = computed(() => {
  if (!policy.value || !originalPolicy.value) {
    return false;
  }
  const policyChanged = JSON.stringify(policy.value) !== JSON.stringify(originalPolicy.value);
  const registrationChanged = registrationEnabled.value !== originalRegistrationEnabled.value;
  return policyChanged || registrationChanged;
});

const fetchPolicy = async () => {
  loading.value = true;
  notification.value = { message: '', type: '' };
  try {
    // Fetch both policy and registration setting in parallel
    const [policyResponse, regResponse] = await Promise.all([
      fetch('/api/admin/security/policies'),
      fetch(`/api/admin/settings/${SettingKeys.Security.RegistrationEnabled}`, { credentials: 'include' })
    ]);
    
    if (!policyResponse.ok) {
      throw new Error(t('security.loadingError', { message: `${policyResponse.status} ${policyResponse.statusText}` }));
    }
    const data = await policyResponse.json();
    policy.value = data;
    originalPolicy.value = JSON.parse(JSON.stringify(data));
    
    // Load registration setting (default true if not found)
    if (regResponse.ok) {
      const regData = await regResponse.json();
      registrationEnabled.value = regData?.value?.toLowerCase() === 'true';
    } else {
      registrationEnabled.value = true;
    }
    originalRegistrationEnabled.value = registrationEnabled.value;
  } catch (e) {
    notification.value = { message: e.message, type: 'error' };
  } finally {
    loading.value = false;
  }
};

const savePolicy = async () => {
  // Client-side validation
  if (policy.value.minCharacterTypes < 2 || policy.value.minCharacterTypes > 4) {
    notification.value = { message: t('security.validation.minCharacterTypesRange') || 'Minimum character types must be between 2 and 4', type: 'error' };
    return;
  }
  if (policy.value.minPasswordLength < 6 || policy.value.minPasswordLength > 128) {
    notification.value = { message: t('security.validation.passwordLengthRange') || 'Password length must be between 6 and 128 characters', type: 'error' };
    return;
  }
  
  isSaving.value = true;
  notification.value = { message: '', type: '' };
  try {
    // Save policy and registration setting
    const [policyResponse, regResponse] = await Promise.all([
      fetch('/api/admin/security/policies', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(policy.value),
      }),
      fetch(`/api/admin/settings/${SettingKeys.Security.RegistrationEnabled}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ value: registrationEnabled.value.toString() })
      })
    ]);
    
    if (!policyResponse.ok) {
      const errorData = await policyResponse.json();
      const errorMessages = Object.values(errorData.errors || {}).flat().join(', ');
      throw new Error(errorMessages || t('security.saveError', { message: policyResponse.statusText }));
    }
    if (!regResponse.ok) {
      throw new Error(t('security.saveError', { message: 'Failed to save registration setting' }));
    }
    
    originalPolicy.value = JSON.parse(JSON.stringify(policy.value));
    originalRegistrationEnabled.value = registrationEnabled.value;
    notification.value = { message: t('security.saveSuccess'), type: 'success' };
  } catch (e) {
    notification.value = { message: e.message, type: 'error' };
  } finally {
    isSaving.value = false;
    setTimeout(() => {
      if (notification.value.type === 'success') {
        notification.value = { message: '', type: '' };
      }
    }, 5000);
  }
};

const onRegistrationToggle = (value) => {
  registrationEnabled.value = value;
};

onMounted(fetchPolicy);
</script>