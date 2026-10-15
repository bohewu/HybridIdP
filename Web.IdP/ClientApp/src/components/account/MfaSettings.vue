<template>
  <div class="mfa-settings">
    <!-- Loading State -->
    <div v-if="loading" class="mfa-loading">
      <div class="spinner"></div>
    </div>

    <!-- MFA Status Display -->
    <div v-else-if="allMfaDisabled" class="all-mfa-disabled">
      <div class="disabled-icon">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
          <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
          <line x1="2" y1="2" x2="22" y2="22"></line>
        </svg>
      </div>
      <h3>{{ t('mfa.allDisabledTitle') }}</h3>
      <p>{{ t('mfa.allDisabledDescription') }}</p>
    </div>

    <!-- MFA Status Display -->
    <div v-else class="mfa-content">
      <p class="mfa-summary" data-testid="mfa-overall-status" role="status">
        {{ hasMfaEnabled ? t('mfa.otpSummaryEnabled') : t('mfa.otpSummaryDisabled') }}
      </p>
      <!-- MFA Disabled State -->
      <div v-if="mfaStatus.enableTotpMfa && !mfaStatus.twoFactorEnabled" class="mfa-status mfa-disabled">
        <div class="status-icon">
          <svg class="icon-shield-off" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M19.69 14a6.9 6.9 0 0 0 .31-2V5l-8-3-3.16 1.18"></path>
            <path d="M4.73 4.73L4 5v7c0 6 8 10 8 10a20.29 20.29 0 0 0 5.62-4.38"></path>
            <line x1="1" y1="1" x2="23" y2="23"></line>
          </svg>
        </div>
        <div class="status-text">
          <h3>{{ t('mfa.authenticatorTitle') }}</h3>
          <p>{{ hasMfaEnabled ? t('mfa.authenticatorOptional') : t('mfa.authenticatorDescription') }}</p>
          <p v-if="totpSetupError" class="error-message" role="alert">{{ totpSetupError }}</p>
        </div>
        <button class="btn-enable" data-testid="totp-add" @click="startSetup()" :disabled="totpSetupLoading">
          {{ totpSetupLoading ? t('mfa.redirectingToSignIn') : t('mfa.addAuthenticator') }}
        </button>
      </div>

      <!-- MFA Enabled State -->
      <div v-else-if="mfaStatus.enableTotpMfa && mfaStatus.twoFactorEnabled" class="mfa-status mfa-enabled">
        <div class="status-icon enabled">
          <svg class="icon-shield-check" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
            <polyline points="9 12 11 14 15 10"></polyline>
          </svg>
        </div>
        <div class="status-text">
          <h3>{{ t('mfa.authenticatorTitle') }}</h3>
          <p>{{ t('mfa.authenticatorEnabled') }}</p>
          <p v-if="cannotRemoveFactor('totp')" id="totp-removal-policy" role="status" data-testid="totp-removal-policy">{{ t('mfa.addAnotherMethodBeforeRemoval') }}</p>
        </div>
        <div class="action-buttons">
          <button class="btn-danger" data-testid="totp-disable" :disabled="cannotRemoveFactor('totp') || totpSetupLoading" :title="cannotRemoveFactor('totp') ? t('mfa.addAnotherMethodBeforeRemoval') : undefined" :aria-describedby="cannotRemoveFactor('totp') ? 'totp-removal-policy' : undefined" @click="startSetup(true, 'totp-disable')">
            {{ t('mfa.disable') }}
          </button>
        </div>
      </div>

      <!-- Email MFA Section (Phase 20.3) -->
      <div v-if="mfaStatus.enableEmailMfa" class="email-mfa-section">
        <div class="mfa-status" :class="mfaStatus.emailMfaEnabled ? 'mfa-enabled' : 'mfa-disabled'">
          <div class="status-icon" :class="{ enabled: mfaStatus.emailMfaEnabled }">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"></path>
              <polyline points="22,6 12,13 2,6"></polyline>
            </svg>
          </div>
          <div class="status-text">
            <h3>{{ t('mfa.emailMfa') }}</h3>
            <p v-if="mfaStatus.emailMfaEnabled">{{ t('mfa.emailMfaEnabled') }}</p>
            <p v-else>{{ t('mfa.emailMfaDescription') }}</p>
            <p v-if="userEmail" class="masked-email">{{ maskedEmail }}</p>
            <p v-if="mfaStatus.emailMfaEnabled && cannotRemoveFactor('email')" id="email-removal-policy" role="status" data-testid="email-removal-policy">{{ t('mfa.addAnotherMethodBeforeRemoval') }}</p>
          </div>
          <div class="action-buttons">
            <button 
              v-if="!mfaStatus.emailMfaEnabled" 
              class="btn-enable" 
              @click="requestEmailMfaSetup"
              data-testid="email-mfa-enable"
              :disabled="emailMfaLoading || totpSetupLoading"
            >
              {{ emailMfaLoading ? '...' : t('mfa.enable') }}
            </button>
            <button 
              v-else 
              class="btn-danger" 
              @click="startSetup(true, 'email-disable')"
              data-testid="email-mfa-disable"
              :disabled="emailMfaLoading || totpSetupLoading || cannotRemoveFactor('email')"
              :title="cannotRemoveFactor('email') ? t('mfa.addAnotherMethodBeforeRemoval') : undefined"
              :aria-describedby="cannotRemoveFactor('email') ? 'email-removal-policy' : undefined"
            >
              {{ emailMfaLoading ? '...' : t('mfa.disable') }}
            </button>
          </div>
        </div>
        <p v-if="emailMfaError" class="error-message">{{ emailMfaError }}</p>
        <p v-if="emailMfaSuccess" class="success-message">{{ emailMfaSuccess }}</p>
      </div>

      <!-- Passkey Section (Phase 20.4) - At bottom per security model -->
      <div v-if="mfaStatus.enablePasskey" class="passkey-section">
        <!-- Warning for non-compliant users (has passkeys but no MFA when policy requires) -->
        <div v-if="requireMfaForPasskey && passkeys.length > 0 && !hasMfaEnabled" class="passkey-warning">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
            <line x1="12" y1="9" x2="12" y2="13"></line>
            <line x1="12" y1="17" x2="12.01" y2="17"></line>
          </svg>
          <span>{{ t('mfa.passkey.nonCompliantWarning') }}</span>
        </div>
        
        <div class="section-header">
          <div class="section-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>
            </svg>
          </div>
          <div class="section-text">
            <h3>{{ t('mfa.passkey.signInMethodTitle') }}</h3>
            <p>{{ t('mfa.passkey.description') }}</p>
          </div>
          <div class="section-action">
            <button 
              class="btn-enable" 
              @click="registerNewPasskey" 
              :disabled="passkeyLoading || totpSetupLoading || passkeyBlockedByPolicy"
              :title="passkeyBlockedByPolicy ? t('mfa.passkey.requiresMfa') : ''"
              :class="{ 'btn-disabled': passkeyBlockedByPolicy }"
            >
              {{ passkeyLoading ? '...' : t('mfa.passkey.register') }}
            </button>
            <p v-if="passkeyBlockedByPolicy" class="blocked-hint">{{ t('mfa.passkey.requiresMfa') }}</p>
          </div>
        </div>

        <p v-if="passkeyListLoading" role="status">{{ t('mfa.passkey.loading') }}</p>
        <div v-else-if="passkeyListSessionExpired" class="passkey-list passkey-list-session-expired" role="status" data-testid="passkey-list-session-expired">
          <p>{{ t('mfa.passkey.sessionExpired') }}</p>
          <a class="btn-secondary" href="/Account/Login?returnUrl=%2FAccount%2FProfile">{{ t('mfa.passkey.signInAgain') }}</a>
        </div>
        <p v-else-if="passkeyListFailed" class="error-message" role="alert">{{ t('mfa.errors.listPasskeysFailed') }}</p>
        <div v-else-if="passkeys.length > 0" class="passkey-list">
          <h4>{{ t('mfa.passkey.registered') }}</h4>
          <div v-for="pk in passkeys" :key="pk.id" class="passkey-item">
            <div class="pk-icon">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="5" y="2" width="14" height="20" rx="2" ry="2"></rect>
                <line x1="12" y1="18" x2="12.01" y2="18"></line>
              </svg>
            </div>
            <div class="pk-info">
              <p class="pk-name">{{ pk.deviceName }}</p>
              <p class="pk-meta">
                {{ t('mfa.passkey.createdAt') }}: {{ formatDate(pk.createdAt) }} | 
                {{ t('mfa.passkey.lastUsed') }}: {{ pk.lastUsedAt ? formatDate(pk.lastUsedAt) : t('mfa.passkey.neverUsed') }}
              </p>
              <p v-if="cannotRemoveFactor('passkey', pk.id)" :id="`passkey-removal-policy-${pk.id}`" role="status">{{ t('mfa.addAnotherMethodBeforeRemoval') }}</p>
            </div>
            <button class="btn-pk-delete" :data-testid="`passkey-delete-${pk.id}`" @click="confirmDeletePasskey(pk)" :aria-label="t('mfa.passkey.delete')" :title="cannotRemoveFactor('passkey', pk.id) ? t('mfa.addAnotherMethodBeforeRemoval') : t('mfa.passkey.delete')" :disabled="passkeyLoading || totpSetupLoading || cannotRemoveFactor('passkey', pk.id)" :aria-describedby="cannotRemoveFactor('passkey', pk.id) ? `passkey-removal-policy-${pk.id}` : undefined">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
            </button>
          </div>
        </div>
        <p v-else class="pk-meta" data-testid="passkey-list-empty">{{ t('mfa.passkey.noneRegistered') }}</p>
        
        <p v-if="passkeyError" class="error-message">{{ passkeyError }}</p>
        <p v-if="passkeySuccess" class="success-message">{{ passkeySuccess }}</p>
      </div>
    </div>

    <div v-show="!loading">
      <AccountRecoveryCodes ref="accountRecoveryCodes" :csrf-token="csrfToken" :busy="totpSetupLoading || emailMfaLoading || passkeyLoading || disableLoading || !!reauthenticationIntent" @status-changed="recoveryCodeStatus = $event" @saved="finishEnrollmentDisclosure" @reauthenticate="startSetup(false, 'account-recovery-codes')" />
    </div>
    <RecoveryEmailSettings v-if="!loading" :csrf-token="csrfToken" />

    <div v-if="reauthenticationIntent" class="modal-overlay" @click.self="cancelReauthentication" @keydown.esc="cancelReauthentication">
      <div class="modal-content" role="dialog" aria-modal="true" aria-labelledby="mfa-reauthentication-title" aria-describedby="mfa-reauthentication-purpose" data-testid="mfa-reauthentication-notice" :aria-busy="totpSetupLoading">
        <h2 id="mfa-reauthentication-title">{{ t('mfa.reauthenticationTitle') }}</h2>
        <p id="mfa-reauthentication-purpose">{{ reauthenticationIntent.action === 'totp-disable' ? t('mfa.disableReauthenticationDescription') : t('mfa.reauthenticationDescription', { action: reauthenticationPurpose }) }}</p>
        <p v-if="reauthenticationIntent.action === 'account-recovery-codes'">{{ t('mfa.accountRecoveryCodes.replaceWarning') }}</p>
        <div v-if="losesLegacyRecoveryCodes(reauthenticationIntent.action)" data-testid="legacy-recovery-removal-warning">
          <p>{{ t('mfa.accountRecoveryCodes.lastOtpRemovalWarning') }}</p>
          <button type="button" class="btn-secondary" data-testid="legacy-recovery-regenerate-first" :disabled="totpSetupLoading" @click="regenerateBeforeRemoval">{{ t('mfa.accountRecoveryCodes.regenerateBeforeRemoval') }}</button>
        </div>
        <p v-if="totpSetupError" class="error-message" role="alert">{{ totpSetupError }}</p>
        <div class="modal-actions">
          <button ref="reauthenticationCancelButton" type="button" class="btn-cancel" data-testid="mfa-reauthentication-cancel" @click="cancelReauthentication" :disabled="totpSetupLoading">{{ t('common.cancel') }}</button>
          <button type="button" class="btn-primary" data-testid="mfa-reauthentication-continue" @click="continueReauthentication" :disabled="totpSetupLoading">
            {{ totpSetupLoading ? t('mfa.redirectingToSignIn') : t('mfa.continueReauthentication') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Email MFA Verification Modal -->
    <div v-if="showEmailMfaModal" class="modal-overlay" @click.self="closeEmailMfaSetup">
      <div
        class="modal-content email-mfa-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="email-mfa-setup-title"
      >
        <h2 id="email-mfa-setup-title">{{ t('mfa.emailSetupTitle') }}</h2>
        <p class="setup-instruction">
          {{ t('mfa.emailSetupDescription', { email: maskedEmail }) }}
        </p>
        <p class="field-hint">{{ t('mfa.emailReauthenticationHint') }}</p>

        <p v-if="emailCodeSent" class="success-message" role="status">
          {{ t('mfa.emailCodePending') }}
        </p>
        <p v-if="emailMfaModalError" class="error-message" role="alert">
          {{ emailMfaModalError }}
        </p>

        <form @submit.prevent="verifyEmailMfa">
          <div class="verify-section">
            <label for="email-mfa-code">{{ t('mfa.emailCodeLabel') }}</label>
            <input
              id="email-mfa-code"
              ref="emailCodeInput"
              v-model.trim="emailMfaCode"
              @input="syncEmailMfaCode"
              @change="syncEmailMfaCode"
              @focus="syncEmailMfaCode"
              type="text"
              inputmode="numeric"
              autocomplete="one-time-code"
              pattern="[0-9]*"
              maxlength="6"
              placeholder="000000"
              class="code-input"
              :disabled="emailMfaLoading"
              aria-describedby="email-mfa-code-guidance"
            />
            <p id="email-mfa-code-guidance" class="field-hint" data-testid="email-mfa-code-guidance" role="status">{{ emailCodeGuidance }}</p>
          </div>

          <div class="modal-actions">
            <button type="button" class="btn-cancel" @click="closeEmailMfaSetup" :disabled="emailMfaLoading">
              {{ t('common.cancel') }}
            </button>
            <button
              type="button"
              class="btn-secondary"
              data-testid="email-mfa-send"
              @click="sendEmailMfaCode"
              :disabled="emailMfaLoading || emailCooldownSeconds > 0"
              :aria-busy="emailMfaLoading ? 'true' : 'false'"
            >
              {{ emailSendButtonText }}
            </button>
            <button
              type="submit"
              class="btn-primary"
              data-testid="email-mfa-verify"
              :disabled="emailMfaLoading || !emailCodeSent || !/^\d{6}$/.test(emailMfaCode)"
            >
              {{ emailMfaLoading ? '...' : t('mfa.verifyAndEnable') }}
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- Passkey Delete Confirmation Modal -->
    <div v-if="passkeyToDelete" class="modal-overlay" @click.self="!passkeyLoading && (passkeyToDelete = null)">
      <div class="modal-content">
        <h2>{{ t('mfa.passkey.deleteConfirmTitle') }}</h2>
        <p>{{ t('mfa.passkey.deleteConfirmMessage') }}</p>
        <p class="delete-target"><strong>{{ passkeyToDelete.deviceName }}</strong></p>
        <p v-if="passkeyError" class="error-message" role="alert">{{ passkeyError }}</p>
        
        <div class="modal-actions">
          <button class="btn-cancel" @click="passkeyToDelete = null" :disabled="passkeyLoading">{{ t('common.cancel') }}</button>
          <button class="btn-danger" @click="deletePasskey" :disabled="passkeyLoading || cannotRemoveFactor('passkey', passkeyToDelete.id)">
            {{ passkeyLoading ? '...' : t('mfa.passkey.delete') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Disable Modal -->
    <div v-if="showDisableModal" class="modal-overlay" @click.self="cancelDisable">
      <div class="modal-content">
        <h2>{{ t('mfa.disableTitle') }}</h2>
        <p>{{ t(mfaStatus.emailMfaEnabled ? 'mfa.disableWarningWithEmail' : 'mfa.disableWarning') }}</p>
        <p class="field-hint">{{ t(mfaStatus.hasPassword ? 'mfa.disablePasswordConfirmation' : 'mfa.disableTotpConfirmation') }}</p>
        
        <!-- Password verification for users with local password -->
        <div v-if="mfaStatus.hasPassword" class="password-section">
          <label>{{ t('mfa.enterPassword') }}</label>
          <input 
            v-model="disablePassword"
            type="password"
            autocomplete="current-password"
            :placeholder="t('mfa.passwordPlaceholder')"
            class="password-input"
            :disabled="disableLoading"
            @keyup.enter="disableMfa"
          />
        </div>
        
        <!-- TOTP verification for users without local password (legacy/SSO) -->
        <div v-else class="password-section">
          <label>{{ t('mfa.enterTotpToDisable') }}</label>
          <input 
            v-model="disableTotpCode"
            type="text"
            :disabled="disableLoading"
            inputmode="numeric"
            pattern="[0-9]*"
            maxlength="6"
            placeholder="000000"
            class="code-input"
            @keyup.enter="disableMfa"
          />
        </div>
        
        <p v-if="disableError" class="error-message">{{ disableError }}</p>
        
        <div class="modal-actions">
          <button class="btn-cancel" @click="cancelDisable" :disabled="disableLoading">{{ t('common.cancel') }}</button>
          <button class="btn-danger" @click="disableMfa" :disabled="disableLoading || totpSetupLoading || cannotRemoveFactor('totp')">{{ t('mfa.disable') }}</button>
        </div>
      </div>
    </div>

  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick } from 'vue';
import { useI18n } from 'vue-i18n';
import { useWebAuthn } from '../../composables/useWebAuthn';
import { useEmailCodeCooldown } from '../../composables/useEmailCodeCooldown';
import RecoveryEmailSettings from './RecoveryEmailSettings.vue';
import AccountRecoveryCodes from './AccountRecoveryCodes.vue';
import { saveMfaContinuation, takeMfaContinuation, clearMfaContinuation } from '../../composables/mfaContinuation';

const { t } = useI18n();

const props = defineProps<{
  profile?: any
  csrfToken?: string
}>();

const emit = defineEmits<{
  (e: 'status-changed'): void
}>();

// State
const loading = ref(true);
const accountRecoveryCodes = ref<InstanceType<typeof AccountRecoveryCodes> | null>(null);
const recoveryCodeStatus = ref({ remainingCodes: 0, legacyRemainingCodes: 0 });
const pendingEnrollmentRefresh = ref(false);
const mfaStatus = ref({
  twoFactorEnabled: false,
  hasAuthenticator: false,
  recoveryCodesLeft: 0,
  hasPassword: true,
  emailMfaEnabled: false,
  enableTotpMfa: true,
  enableEmailMfa: true,
  enablePasskey: true,
  enforceMandatoryMfaEnrollment: false
});

const allMfaDisabled = computed(() => {
  return !mfaStatus.value.enableTotpMfa && 
         !mfaStatus.value.enableEmailMfa && 
         !mfaStatus.value.enablePasskey;
});

// Email MFA (Phase 20.3)
const userEmail = ref('');
const userId = ref('');
let resumingSecurityAction = false;
const userTimeZone = ref('');
const emailMfaLoading = ref(false);
const emailMfaError = ref('');
const emailMfaSuccess = ref('');
const showEmailMfaModal = ref(false);
const emailMfaCode = ref('');
const emailCodeSent = ref(false);
const emailMfaModalError = ref('');
const emailCodeInput = ref<HTMLInputElement | null>(null);
const {
  remainingSeconds: emailCooldownSeconds,
  startCooldown: startEmailCooldown,
  stopCooldown: stopEmailCooldown
} = useEmailCodeCooldown();
const emailSendButtonText = computed(() => {
  if (emailMfaLoading.value) return t('mfa.sendingCode');
  if (emailCooldownSeconds.value > 0) {
    return t(emailCodeSent.value ? 'mfa.resendCodeIn' : 'mfa.sendCodeIn', { seconds: emailCooldownSeconds.value });
  }
  return emailCodeSent.value ? t('mfa.resendCode') : t('mfa.sendCode');
});
const emailCodeGuidance = computed(() => emailCodeSent.value
  ? t('mfa.emailCodeHelp')
  : emailCooldownSeconds.value > 0 ? t('mfa.emailCodeUnavailable') : t('mfa.emailCodeNeeded'));

function syncEmailMfaCode() {
  if (emailMfaLoading.value || !emailCodeInput.value) return;
  emailMfaCode.value = emailCodeInput.value.value.replace(/\D/g, '').slice(0, 6);
  emailCodeInput.value.value = emailMfaCode.value;
}

// Passkeys (Phase 20.4)
const { registerPasskey } = useWebAuthn();
const passkeys = ref<any[]>([]);
const passkeyListSessionExpired = ref(false);
const passkeyListFailed = ref(false);
const passkeyListLoading = ref(true);
const passkeyLoading = ref(false);
const passkeyError = ref('');
const passkeySuccess = ref('');
const passkeyToDelete = ref<any>(null);
const requireMfaForPasskey = ref(false);

// Computed: Check if user has any MFA enabled
const hasMfaEnabled = computed(() => {
  return mfaStatus.value.twoFactorEnabled || mfaStatus.value.emailMfaEnabled;
});

// Computed: Check if passkey registration should be blocked
const passkeyBlockedByPolicy = computed(() => {
  return requireMfaForPasskey.value && !hasMfaEnabled.value;
});

function cannotRemoveFactor(factor: string, targetId: number | null = null) {
  if (!mfaStatus.value.enforceMandatoryMfaEnrollment) return false;
  const hasTotp = factor !== 'totp' && mfaStatus.value.enableTotpMfa && mfaStatus.value.twoFactorEnabled;
  const hasEmail = factor !== 'email' && mfaStatus.value.enableEmailMfa && mfaStatus.value.emailMfaEnabled;
  const hasPasskey = mfaStatus.value.enablePasskey && passkeys.value.some(key => factor !== 'passkey' || key.id !== targetId) &&
    (!requireMfaForPasskey.value || hasTotp || hasEmail);
  return !hasTotp && !hasEmail && !hasPasskey;
}

function losesLegacyRecoveryCodes(action: string | null) {
  return recoveryCodeStatus.value.remainingCodes === 0 && recoveryCodeStatus.value.legacyRemainingCodes > 0 &&
    ((action === 'totp-disable' && !mfaStatus.value.emailMfaEnabled) ||
     (action === 'email-disable' && !mfaStatus.value.twoFactorEnabled));
}

async function regenerateBeforeRemoval() {
  cancelReauthentication();
  await startSetup(false, 'account-recovery-codes');
}

const maskedEmail = computed(() => {
  if (!userEmail.value) return '';
  const [local, domain] = userEmail.value.split('@');
  if (!local || !domain) return userEmail.value;
  const maskedLocal = local.length > 2 
    ? local[0] + '***' + local[local.length - 1]
    : local[0] + '***';
  return `${maskedLocal}@${domain}`;
});

const totpSetupLoading = ref(false);
const totpSetupError = ref('');
const reauthenticationIntent = ref<{ forRemoval: boolean; action: string | null; targetId: number | null } | null>(null);
const reauthenticationCancelButton = ref<HTMLButtonElement | null>(null);
let reauthenticationReturnFocus: HTMLElement | null = null;
const reauthenticationPurpose = computed(() => t(`mfa.reauthenticationActions.${reauthenticationIntent.value?.action ?? 'passkey-list'}`));

// Disable Modal
const showDisableModal = ref(false);
const disablePassword = ref('');
const disableTotpCode = ref('');
const disableError = ref('');

onMounted(async () => {
  window.addEventListener('pageshow', syncEmailMfaCode);
  await loadMfaStatus();
  await loadSecurityPolicy();
  await loadPasskeys();
  const continuation = takeMfaContinuation(userId.value);
  if (!continuation) return;
  resumingSecurityAction = true;
  try {
    switch (continuation.action) {
      case 'email-enable': if (mfaStatus.value.enableEmailMfa && !mfaStatus.value.emailMfaEnabled) openEmailMfaSetup(); break;
      case 'email-disable': if (mfaStatus.value.emailMfaEnabled && !cannotRemoveFactor('email')) await disableEmailMfa(); break;
      case 'passkey-register': if (mfaStatus.value.enablePasskey && !passkeyBlockedByPolicy.value) await registerNewPasskey(); break;
      case 'totp-disable': if (mfaStatus.value.twoFactorEnabled && !cannotRemoveFactor('totp')) showDisableModal.value = true; break;
      case 'passkey-delete': if (!cannotRemoveFactor('passkey', continuation.targetId)) passkeyToDelete.value = passkeys.value.find(key => key.id === continuation.targetId) || null; break;
      case 'recovery-codes': accountRecoveryCodes.value?.resumeGeneration(); break;
      case 'account-recovery-codes': accountRecoveryCodes.value?.resumeGeneration(); break;
    }
  } finally { resumingSecurityAction = false; }
});
onUnmounted(() => window.removeEventListener('pageshow', syncEmailMfaCode));

async function loadSecurityPolicy() {
  try {
    const response = await fetch('/api/account/security-policy', { credentials: 'include' });
    if (response.ok) {
      const policy = await response.json();
      requireMfaForPasskey.value = policy.requireMfaForPasskey || false;
    }
  } catch (err) {
    console.error('Failed to load security policy:', err);
  }
}

async function loadPasskeys() {
  passkeyListLoading.value = true;
  passkeyListSessionExpired.value = false;
  passkeyListFailed.value = false;
  try {
    const response = await fetch('/api/passkey/list', { credentials: 'include' });
    if (response.ok) {
      passkeys.value = await response.json();
      passkeyError.value = '';
    } else {
      passkeys.value = [];
      passkeyListSessionExpired.value = response.status === 401;
      passkeyListFailed.value = !passkeyListSessionExpired.value;
    }
  } catch (err) {
    passkeys.value = [];
    passkeyListFailed.value = true;
    console.error('Failed to load passkeys:', err);
  } finally {
    passkeyListLoading.value = false;
  }
}

async function registerNewPasskey() {
  if (passkeyLoading.value || totpSetupLoading.value) return;
  passkeyLoading.value = true;
  passkeyError.value = '';
  passkeySuccess.value = '';
  
  try {
    const result = await registerPasskey();
    if (result?.accountRecoveryCodes) accountRecoveryCodes.value?.displayIssuedCodes(result.accountRecoveryCodes);
    passkeySuccess.value = t('mfa.passkey.registerSuccess');
    await loadPasskeys();
    await accountRecoveryCodes.value?.loadStatus();
    setTimeout(() => { passkeySuccess.value = ''; }, 3000);
  } catch (err: any) {
    passkeyError.value = t(err.message) || t('mfa.errors.registerPasskeyFailed');
    if (err.message === 'mfa.errors.freshAuthenticationRequired' && !resumingSecurityAction) await startSetup(false, 'passkey-register');
  } finally {
    passkeyLoading.value = false;
  }
}

function confirmDeletePasskey(pk: any) {
  if (cannotRemoveFactor('passkey', pk.id)) return;
  passkeyToDelete.value = pk;
}

async function deletePasskey() {
  if (!passkeyToDelete.value || passkeyLoading.value || cannotRemoveFactor('passkey', passkeyToDelete.value.id)) return;
  
  passkeyLoading.value = true;
  passkeyError.value = '';
  
  try {
    const response = await fetch(`/api/passkey/${passkeyToDelete.value.id}`, {
      method: 'DELETE',
      credentials: 'include'
    });
    
    if (response.ok) {
      passkeys.value = passkeys.value.filter(key => key.id !== passkeyToDelete.value.id);
      passkeyToDelete.value = null;
      await loadPasskeys();
      await accountRecoveryCodes.value?.loadStatus();
    } else {
      const result = await response.json();
      passkeyError.value = result.error ? t(`mfa.errors.${result.error}`) : t('mfa.errors.deletePasskeyFailed');
      if (result.error === 'freshAuthenticationRequired' && !resumingSecurityAction) await startSetup(true, 'passkey-delete', passkeyToDelete.value?.id);
    }
  } catch (err) {
    passkeyError.value = t('mfa.errors.deletePasskeyFailed');
  } finally {
    passkeyLoading.value = false;
  }
}

function formatDate(dateStr: string) {
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  };
  
  if (userTimeZone.value) {
    options.timeZone = userTimeZone.value;
  }
  
  return new Date(dateStr).toLocaleString(undefined, options);
}

async function loadMfaStatus() {
  loading.value = true;
  try {
    const response = await fetch('/api/account/mfa/status', { credentials: 'include' });
    if (response.ok) {
      mfaStatus.value = await response.json();
    }
    // Also fetch user email for Email MFA display
    if (props.profile) {
      userId.value = props.profile.userId || '';
      userEmail.value = props.profile.email || '';
      userTimeZone.value = props.profile.timeZone || '';
    } else {
      const profileResponse = await fetch('/api/profile', { credentials: 'include' });
      if (profileResponse.ok) {
        const profile = await profileResponse.json();
        userId.value = profile.userId || '';
        userEmail.value = profile.email || '';
        userTimeZone.value = profile.timeZone || '';
      }
    }
  } catch (err) {
    console.error('Failed to load MFA status:', err);
  } finally {
    loading.value = false;
  }
}

function openEmailMfaSetup() {
  showEmailMfaModal.value = true;
  emailMfaCode.value = '';
  emailMfaModalError.value = '';
}

async function requestEmailMfaSetup() {
  if (emailMfaLoading.value || totpSetupLoading.value) return;
  await startSetup(false, 'email-enable');
}

function closeEmailMfaSetup() {
  if (emailMfaLoading.value) return;
  showEmailMfaModal.value = false;
  emailMfaCode.value = '';
  emailMfaModalError.value = '';
}

async function sendEmailMfaCode() {
  if (emailMfaLoading.value || emailCooldownSeconds.value > 0) return;

  emailMfaLoading.value = true;
  emailMfaModalError.value = '';
  
  try {
    const response = await fetch('/api/account/mfa/email/send', {
      method: 'POST',
      headers: { 'X-XSRF-TOKEN': props.csrfToken || '' },
      credentials: 'include'
    });
    const result = await response.json().catch(() => ({}));
    emailCodeSent.value = result.hasPendingCode === true;

    if (response.ok) {
      startEmailCooldown(result.remainingSeconds || 60);
      await nextTick();
      emailCodeInput.value?.focus();
    } else if (result.error === 'freshAuthenticationRequired') {
      if (!resumingSecurityAction) await startSetup(false, 'email-enable');
    } else if (result.remainingSeconds) {
      startEmailCooldown(result.remainingSeconds);
    } else {
      emailMfaModalError.value = t('mfa.errors.sendCodeFailed');
    }
  } catch (err) {
    emailMfaModalError.value = t('mfa.errors.sendCodeFailed');
  } finally {
    emailMfaLoading.value = false;
  }
}

async function verifyEmailMfa() {
  syncEmailMfaCode();
  if (emailMfaLoading.value || !emailCodeSent.value || !/^\d{6}$/.test(emailMfaCode.value)) return;

  emailMfaLoading.value = true;
  emailMfaModalError.value = '';
  emailMfaSuccess.value = '';

  try {
    const response = await fetch('/api/account/mfa/email/verify', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': props.csrfToken || ''
      },
      credentials: 'include',
      body: JSON.stringify({ code: emailMfaCode.value })
    });
    const result = await response.json().catch(() => ({}));
    if (typeof result.hasPendingCode === 'boolean') emailCodeSent.value = result.hasPendingCode;

    if (response.ok && result.success) {
      showEmailMfaModal.value = false;
      emailMfaCode.value = '';
      emailCodeSent.value = false;
      stopEmailCooldown();
      await loadMfaStatus();
      const hasIssuedCodes = result.accountRecoveryCodes?.length > 0;
      if (hasIssuedCodes) {
        pendingEnrollmentRefresh.value = true;
        accountRecoveryCodes.value?.displayIssuedCodes(result.accountRecoveryCodes);
      }
      await accountRecoveryCodes.value?.loadStatus();
      emailMfaSuccess.value = t('mfa.emailMfaEnabled');
      if (!hasIssuedCodes) emit('status-changed');
      setTimeout(() => { emailMfaSuccess.value = ''; }, 3000);
    } else if (result.error === 'freshAuthenticationRequired') {
      if (!resumingSecurityAction) await startSetup(false, 'email-enable');
    } else {
      emailMfaModalError.value = result.message || t('mfa.errors.invalidOrExpiredCode');
    }
  } catch (err) {
    emailMfaModalError.value = t('mfa.errors.verifyFailed');
  } finally {
    emailMfaLoading.value = false;
  }
}

function finishEnrollmentDisclosure() {
  if (!pendingEnrollmentRefresh.value) return;
  pendingEnrollmentRefresh.value = false;
  emit('status-changed');
}

async function disableEmailMfa() {
  if (emailMfaLoading.value || totpSetupLoading.value || cannotRemoveFactor('email')) return;
  emailMfaLoading.value = true;
  emailMfaError.value = '';
  emailMfaSuccess.value = '';
  
  try {
    const response = await fetch('/api/account/mfa/email/disable', {
      method: 'POST',
      credentials: 'include'
    });
    
    if (response.ok) {
      await loadMfaStatus();
      await loadPasskeys();
      await accountRecoveryCodes.value?.loadStatus();
      emailMfaSuccess.value = t('mfa.emailMfaDisabled');
      emit('status-changed');
      setTimeout(() => { emailMfaSuccess.value = ''; }, 3000);
    } else {
      const result = await response.json();
      emailMfaError.value = result.error === 'mandatoryMfaEnforced' ? t('mfa.addAnotherMethodBeforeRemoval')
        : result.error === 'alternateSignInRequired' ? t('mfa.errors.alternateSignInRequired')
        : result.message || t('mfa.errors.toggleFailed');
      if (result.error === 'freshAuthenticationRequired' && !resumingSecurityAction) await startSetup(true, 'email-disable');
    }
  } catch (err) {
    emailMfaError.value = t('mfa.errors.toggleFailed');
  } finally {
    emailMfaLoading.value = false;
  }
}

async function startSetup(forRemoval = false, action: string | null = 'totp-add', targetId: number | null = null) {
  if (totpSetupLoading.value || reauthenticationIntent.value) return;
  if ((action === 'totp-disable' && cannotRemoveFactor('totp')) || (action === 'email-disable' && cannotRemoveFactor('email')) ||
      (action === 'passkey-delete' && cannotRemoveFactor('passkey', targetId))) return;
  reauthenticationReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  showEmailMfaModal.value = false;
  showDisableModal.value = false;
  passkeyToDelete.value = null;
  emailMfaCode.value = '';
  disablePassword.value = '';
  disableTotpCode.value = '';
  totpSetupError.value = '';
  reauthenticationIntent.value = { forRemoval: forRemoval === true, action, targetId };
  await nextTick();
  reauthenticationCancelButton.value?.focus();
}

function cancelReauthentication() {
  if (totpSetupLoading.value) return;
  reauthenticationIntent.value = null;
  totpSetupError.value = '';
  reauthenticationReturnFocus?.focus();
}

async function continueReauthentication() {
  await beginReauthentication(reauthenticationIntent.value);
}

async function beginReauthentication(intent: { forRemoval: boolean; action: string | null; targetId: number | null } | null) {
  if (totpSetupLoading.value || !intent) return;
  const { forRemoval, action, targetId } = intent;
  totpSetupLoading.value = true;
  totpSetupError.value = '';
  let redirecting = false;
  if (action) saveMfaContinuation(userId.value, action, targetId);
  else clearMfaContinuation();

  try {
    const parameters = new URLSearchParams();
    if (forRemoval === true) parameters.set('forRemoval', 'true');
    else if (action !== 'totp-add') parameters.set('returnToProfile', 'true');
    if (action) parameters.set('action', action);
    const url = `/api/account/mfa/reauthenticate?${parameters}`;
    const response = await fetch(url, {
      method: 'POST',
      credentials: 'include'
    });
    if (!response.ok) {
      throw new Error('Reauthentication could not be started');
    }

    const result = await response.json();
    if (!result.loginUrl) {
      throw new Error('Reauthentication URL was not returned');
    }

    redirecting = true;
    window.location.assign(result.loginUrl);
  } catch (err) {
    clearMfaContinuation();
    totpSetupError.value = t('mfa.errors.setupFailed');
  } finally {
    if (!redirecting) totpSetupLoading.value = false;
  }
}

const disableLoading = ref(false);

async function disableMfa() {
  if (disableLoading.value || totpSetupLoading.value || cannotRemoveFactor('totp')) return;
  disableLoading.value = true;
  disableError.value = '';
  
  try {
    // Build payload based on whether user has password
    const payload = mfaStatus.value.hasPassword
      ? { password: disablePassword.value }
      : { totpCode: disableTotpCode.value };
    
    const response = await fetch('/api/account/mfa/disable', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload)
    });
    
    if (response.ok) {
      showDisableModal.value = false;
      disablePassword.value = '';
      disableTotpCode.value = '';
      await loadMfaStatus();
      await loadPasskeys();
      await accountRecoveryCodes.value?.loadStatus();
      emit('status-changed');
    } else {
      const result = await response.json();
      // Translate error key from API (e.g., 'invalidPassword' -> mfa.errors.invalidPassword)
      const errorKey = result.error ? `mfa.errors.${result.error}` : 'mfa.errors.disableFailed';
      disableError.value = t(errorKey);
      if (result.error === 'freshAuthenticationRequired' && !resumingSecurityAction) await startSetup(true, 'totp-disable');
    }
  } catch (err) {
    disableError.value = t('mfa.errors.disableFailed');
  } finally { disableLoading.value = false; }
}

function cancelDisable() {
  if (disableLoading.value) return;
  showDisableModal.value = false;
  disablePassword.value = '';
  disableTotpCode.value = '';
  disableError.value = '';
}

</script>

<style scoped>
.mfa-summary {
  margin: 0 0 1rem;
  font-weight: 600;
}

.mfa-settings {
  padding: 20px 24px;
}

.mfa-loading {
  display: flex;
  justify-content: center;
  padding: 40px;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid #e8eaed;
  border-top-color: #1a73e8;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.mfa-status {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.status-icon {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fce8e6;
  color: #c5221f;
}

.status-icon.enabled {
  background: #e6f4ea;
  color: #137333;
}

.status-icon svg {
  width: 24px;
  height: 24px;
}

.status-text {
  flex: 1;
  min-width: 200px;
}

.status-text h3 {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 500;
  color: #202124;
}

.status-text p {
  margin: 0;
  font-size: 13px;
  color: #5f6368;
}

.status-text .reauthentication-note {
  margin-top: 6px;
  color: #3c4043;
}

.btn-enable {
  background: #1a73e8;
  color: white;
  border: none;
  padding: 10px 24px;
  border-radius: 4px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.2s;
}

.btn-enable:hover {
  background: #1557b0;
}

.btn-enable:disabled,
.btn-enable.btn-disabled {
  background: #dadce0;
  color: #80868b;
  cursor: not-allowed;
}

.btn-enable:disabled:hover,
.btn-enable.btn-disabled:hover {
  background: #dadce0;
}

.blocked-hint {
  margin: 4px 0 0;
  font-size: 12px;
  color: #ea8600;
}

.passkey-warning {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 16px;
  background: #fef7e0;
  border: 1px solid #fdd663;
  border-radius: 8px;
  margin-bottom: 16px;
}

.passkey-warning svg {
  width: 20px;
  height: 20px;
  color: #ea8600;
  flex-shrink: 0;
  margin-top: 2px;
}

.passkey-warning span {
  font-size: 13px;
  color: #5f6368;
  line-height: 1.4;
}

.action-buttons {
  display: flex;
  gap: 8px;
}

.btn-secondary {
  background: white;
  color: #1a73e8;
  border: 1px solid #dadce0;
  padding: 8px 16px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}

.btn-secondary:hover {
  background: #f1f3f4;
}

.btn-secondary:disabled,
.btn-secondary:disabled:hover,
.btn-danger:disabled,
.btn-danger:disabled:hover {
  background: #f8f9fa;
  color: #9aa0a6;
  border-color: #e4e7eb;
  cursor: not-allowed;
  opacity: 0.75;
}

.btn-danger {
  background: white;
  color: #c5221f;
  border: 1px solid #dadce0;
  padding: 8px 16px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}

.btn-danger:hover {
  background: #fce8e6;
}

/* Modal Styles */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-content {
  background: white;
  border-radius: 8px;
  padding: 24px;
  max-width: 480px;
  width: 90%;
  max-height: 90vh;
  overflow-y: auto;
}

.modal-content h2 {
  margin: 0 0 16px;
  font-size: 18px;
  font-weight: 500;
  color: #202124;
}

.setup-modal {
  max-width: 400px;
}

.email-mfa-modal {
  max-width: 440px;
}

.setup-instruction {
  text-align: center;
  color: #5f6368;
  margin-bottom: 16px;
}

.qr-container {
  display: flex;
  justify-content: center;
  margin: 24px 0;
}

.qr-code {
  width: 200px;
  height: 200px;
  border-radius: 8px;
}

.qr-placeholder {
  width: 200px;
  height: 200px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f1f3f4;
  border-radius: 8px;
}

.manual-key {
  text-align: center;
  margin: 16px 0;
  padding: 16px;
  background: #f1f3f4;
  border-radius: 8px;
}

.manual-key p {
  margin: 0 0 8px;
  font-size: 12px;
  color: #5f6368;
}

.shared-key {
  font-family: 'Roboto Mono', monospace;
  font-size: 14px;
  letter-spacing: 2px;
  word-break: break-all;
}

.verify-section {
  margin: 24px 0;
}

.verify-section label {
  display: block;
  margin-bottom: 8px;
  font-size: 14px;
  color: #202124;
}

.code-input {
  width: 100%;
  padding: 12px;
  font-size: 24px;
  text-align: center;
  letter-spacing: 8px;
  border: 1px solid #dadce0;
  border-radius: 4px;
  font-family: 'Roboto Mono', monospace;
}

.code-input:focus {
  outline: none;
  border-color: #1a73e8;
  box-shadow: 0 0 0 2px rgba(26, 115, 232, 0.2);
}

.code-input:disabled {
  background: #f3f4f6;
  cursor: not-allowed;
}

.field-hint {
  margin: 8px 0 0;
  color: #5f6368;
  font-size: 13px;
}

.error-message {
  color: #c5221f;
  font-size: 13px;
  margin-top: 8px;
}

.password-section {
  margin: 16px 0;
}

.password-section label {
  display: block;
  margin-bottom: 8px;
  font-size: 14px;
}

.password-input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #dadce0;
  border-radius: 4px;
  font-size: 14px;
}

.modal-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 24px;
}

.btn-cancel {
  background: white;
  color: #5f6368;
  border: 1px solid #dadce0;
  padding: 10px 24px;
  border-radius: 4px;
  font-size: 14px;
  cursor: pointer;
}

.btn-primary {
  background: #1a73e8;
  color: white;
  border: none;
  padding: 10px 24px;
  border-radius: 4px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
}

.btn-primary:disabled {
  background: #dadce0;
  cursor: not-allowed;
}

/* Recovery Codes */
.recovery-codes-display {
  text-align: center;
}

.success-icon {
  width: 64px;
  height: 64px;
  margin: 0 auto 16px;
  background: #e6f4ea;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #137333;
}

.success-icon svg {
  width: 32px;
  height: 32px;
}

.warning-text {
  color: #c5221f;
  font-weight: 500;
  margin: 16px 0;
}

.recovery-codes-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
  margin: 16px 0;
}

.recovery-code {
  background: #f1f3f4;
  padding: 8px 12px;
  border-radius: 4px;
  font-family: 'Roboto Mono', monospace;
  font-size: 13px;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

@media (max-width: 480px) {
  .mfa-status {
    flex-direction: column;
    align-items: flex-start;
  }
  
  .action-buttons {
    width: 100%;
    flex-direction: column;
  }
  
  .btn-secondary,
  .btn-danger {
    width: 100%;
    text-align: center;
  }
}

/* Passkey Section (Phase 20.4) */
.passkey-section {
  margin-top: 24px;
  padding: 24px 0;
  border-top: 1px solid #e8eaed;
}

.passkey-list {
  margin-top: 20px;
}

.passkey-list-session-expired {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem;
  background: #f9fafb;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
}

.passkey-list-session-expired p {
  margin: 0;
  color: #6b7280;
  font-size: 0.875rem;
}

.passkey-list-session-expired .btn-secondary {
  flex-shrink: 0;
}

.passkey-list h4 {
  font-size: 13px;
  font-weight: 500;
  color: #5f6368;
  margin-bottom: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.passkey-item {
  display: flex;
  align-items: center;
  padding: 12px;
  background: #f8f9fa;
  border: 1px solid #e8eaed;
  border-radius: 8px;
  margin-bottom: 8px;
  gap: 12px;
}

.pk-icon {
  width: 36px;
  height: 36px;
  border-radius: 4px;
  background: #e6f4ea;
  color: #137333;
  display: flex;
  align-items: center;
  justify-content: center;
}

.pk-icon svg {
  width: 20px;
  height: 20px;
}

.pk-info {
  flex: 1;
}

.pk-name {
  font-size: 14px;
  font-weight: 500;
  color: #202124;
  margin: 0;
}

.pk-meta {
  font-size: 12px;
  color: #5f6368;
  margin: 4px 0 0;
}

.btn-pk-delete {
  background: none;
  border: none;
  color: #5f6368;
  cursor: pointer;
  padding: 8px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-pk-delete:hover {
  background: #fce8e6;
  color: #c5221f;
}

.btn-pk-delete:disabled,
.btn-pk-delete:disabled:hover {
  background: #f8f9fa;
  color: #9aa0a6;
  cursor: not-allowed;
  opacity: 0.75;
}

.btn-pk-delete svg {
  width: 18px;
  height: 18px;
}

.delete-target {
  margin: 16px 0;
  padding: 12px;
  background: #f1f3f4;
  border-radius: 4px;
  text-align: center;
}

.section-action {
  flex-shrink: 0;
}

/* All MFA Disabled (Phase 20.4) */
.all-mfa-disabled {
  text-align: center;
  padding: 48px 24px;
  background: #f8f9fa;
  border: 1px dashed #dadce0;
  border-radius: 12px;
  margin-top: 24px;
}

.disabled-icon {
  color: #bdc1c6;
  margin-bottom: 16px;
}

.disabled-icon svg {
  width: 48px;
  height: 48px;
}

.all-mfa-disabled h3 {
  font-size: 18px;
  color: #202124;
  margin: 0 0 8px;
}

.all-mfa-disabled p {
  font-size: 14px;
  color: #5f6368;
  margin: 0;
}

/* Email MFA Section (Phase 20.3) */
.email-mfa-section {
  margin-top: 24px;
  padding-top: 24px;
  border-top: 1px solid #e8eaed;
}

.section-header {
  display: flex;
  align-items: flex-start;
  gap: 16px;
}

.section-icon {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #e8f0fe;
  color: #1a73e8;
  flex-shrink: 0;
}

.section-icon svg {
  width: 20px;
  height: 20px;
}

.section-text {
  flex: 1;
}

.section-text h4 {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 500;
  color: #202124;
}

.section-text p {
  margin: 0 0 4px;
  font-size: 13px;
  color: #5f6368;
}

.masked-email {
  font-family: 'Roboto Mono', monospace;
  font-size: 12px;
  color: #1a73e8;
}

.section-toggle {
  flex-shrink: 0;
}

.toggle-switch {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 24px;
}

.toggle-switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.toggle-slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: #dadce0;
  transition: 0.3s;
  border-radius: 24px;
}

.toggle-slider:before {
  position: absolute;
  content: "";
  height: 18px;
  width: 18px;
  left: 3px;
  bottom: 3px;
  background-color: white;
  transition: 0.3s;
  border-radius: 50%;
  box-shadow: 0 1px 3px rgba(0,0,0,0.2);
}

input:checked + .toggle-slider {
  background-color: #1a73e8;
}

input:checked + .toggle-slider:before {
  transform: translateX(24px);
}

input:disabled + .toggle-slider {
  opacity: 0.5;
  cursor: not-allowed;
}

.success-message {
  color: #137333;
  font-size: 13px;
  margin-top: 8px;
}
</style>
