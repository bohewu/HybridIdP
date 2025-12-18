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
          <h3>{{ t('mfa.notEnabled') }}</h3>
          <p>{{ t('mfa.enableDescription') }}</p>
        </div>
        <button class="btn-enable" @click="startSetup">
          {{ t('mfa.enable') }}
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
          <h3>{{ t('mfa.enabled') }}</h3>
          <p>{{ t('mfa.recoveryCodesLeft', { count: mfaStatus.recoveryCodesLeft }) }}</p>
        </div>
        <div class="action-buttons">
          <button class="btn-secondary" @click="showRegenerateModal = true">
            {{ t('mfa.regenerateCodes') }}
          </button>
          <button class="btn-danger" @click="showDisableModal = true">
            {{ t('mfa.disable') }}
          </button>
        </div>
      </div>

      <!-- Passkey Section (Phase 20.4) -->
      <div v-if="mfaStatus.enablePasskey" class="passkey-section">
        <div class="section-header">
          <div class="section-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>
            </svg>
          </div>
          <div class="section-text">
            <h3>{{ t('mfa.passkey.title') }}</h3>
            <p>{{ t('mfa.passkey.description') }}</p>
          </div>
          <div class="section-action">
            <button class="btn-enable" @click="registerNewPasskey" :disabled="passkeyLoading">
              {{ passkeyLoading ? '...' : t('mfa.passkey.register') }}
            </button>
          </div>
        </div>

        <div v-if="passkeys.length > 0" class="passkey-list">
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
            </div>
            <button class="btn-pk-delete" @click="confirmDeletePasskey(pk)" :title="t('mfa.passkey.delete')">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
            </button>
          </div>
        </div>
        
        <p v-if="passkeyError" class="error-message">{{ passkeyError }}</p>
        <p v-if="passkeySuccess" class="success-message">{{ passkeySuccess }}</p>
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
          </div>
          <div class="action-buttons">
            <button 
              v-if="!mfaStatus.emailMfaEnabled" 
              class="btn-enable" 
              @click="enableEmailMfa"
              :disabled="emailMfaLoading"
            >
              {{ emailMfaLoading ? '...' : t('mfa.enable') }}
            </button>
            <button 
              v-else 
              class="btn-danger" 
              @click="disableEmailMfa"
              :disabled="emailMfaLoading"
            >
              {{ emailMfaLoading ? '...' : t('mfa.disable') }}
            </button>
          </div>
        </div>
        <p v-if="emailMfaError" class="error-message">{{ emailMfaError }}</p>
        <p v-if="emailMfaSuccess" class="success-message">{{ emailMfaSuccess }}</p>
      </div>
    </div>

    <!-- Passkey Delete Confirmation Modal -->
    <div v-if="passkeyToDelete" class="modal-overlay" @click.self="passkeyToDelete = null">
      <div class="modal-content">
        <h2>{{ t('mfa.passkey.deleteConfirmTitle') }}</h2>
        <p>{{ t('mfa.passkey.deleteConfirmMessage') }}</p>
        <p class="delete-target"><strong>{{ passkeyToDelete.deviceName }}</strong></p>
        
        <div class="modal-actions">
          <button class="btn-cancel" @click="passkeyToDelete = null">{{ t('common.cancel') }}</button>
          <button class="btn-danger" @click="deletePasskey" :disabled="passkeyLoading">
            {{ passkeyLoading ? '...' : t('mfa.passkey.delete') }}
          </button>
        </div>
      </div>
    </div>

    <!-- Setup Modal -->
    <div v-if="showSetupModal" class="modal-overlay" @click.self="cancelSetup">
      <div class="modal-content setup-modal">
        <h2>{{ t('mfa.setupTitle') }}</h2>
        
        <div v-if="!setupComplete" class="setup-steps">
          <p class="setup-instruction">{{ t('mfa.scanQrCode') }}</p>
          
          <div class="qr-container">
            <img v-if="setupInfo.qrCodeDataUri" :src="setupInfo.qrCodeDataUri" alt="QR Code" class="qr-code" />
            <div v-else class="qr-placeholder">
              <div class="spinner"></div>
            </div>
          </div>
          
          <div class="manual-key">
            <p>{{ t('mfa.cantScan') }}</p>
            <code class="shared-key">{{ setupInfo.sharedKey }}</code>
          </div>
          
          <div class="verify-section">
            <label>{{ t('mfa.enterCode') }}</label>
            <input 
              v-model="verifyCode"
              type="text"
              inputmode="numeric"
              pattern="[0-9]*"
              maxlength="6"
              placeholder="000000"
              class="code-input"
              @keyup.enter="verifySetup"
            />
            <p v-if="setupError" class="error-message">{{ setupError }}</p>
          </div>
          
          <div class="modal-actions">
            <button class="btn-cancel" @click="cancelSetup">{{ t('common.cancel') }}</button>
            <button class="btn-primary" @click="verifySetup" :disabled="verifyCode.length !== 6">
              {{ t('mfa.verify') }}
            </button>
          </div>
        </div>

        <!-- Recovery Codes Display -->
        <div v-else class="recovery-codes-display">
          <div class="success-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"></circle>
              <polyline points="9 12 11 14 15 10"></polyline>
            </svg>
          </div>
          <h3>{{ t('mfa.setupComplete') }}</h3>
          <p class="warning-text">{{ t('mfa.saveRecoveryCodes') }}</p>
          
          <div class="recovery-codes-grid">
            <code v-for="code in recoveryCodes" :key="code" class="recovery-code">{{ code }}</code>
          </div>
          
          <div class="modal-actions">
            <button class="btn-primary" @click="finishSetup">{{ t('mfa.done') }}</button>
          </div>
        </div>
      </div>
    </div>

    <!-- Disable Modal -->
    <div v-if="showDisableModal" class="modal-overlay" @click.self="cancelDisable">
      <div class="modal-content">
        <h2>{{ t('mfa.disableTitle') }}</h2>
        <p>{{ t('mfa.disableWarning') }}</p>
        
        <!-- Password verification for users with local password -->
        <div v-if="mfaStatus.hasPassword" class="password-section">
          <label>{{ t('mfa.enterPassword') }}</label>
          <input 
            v-model="disablePassword"
            type="password"
            class="password-input"
            @keyup.enter="disableMfa"
          />
        </div>
        
        <!-- TOTP verification for users without local password (legacy/SSO) -->
        <div v-else class="password-section">
          <label>{{ t('mfa.enterTotpToDisable') }}</label>
          <input 
            v-model="disableTotpCode"
            type="text"
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
          <button class="btn-cancel" @click="cancelDisable">{{ t('common.cancel') }}</button>
          <button class="btn-danger" @click="disableMfa">{{ t('mfa.disable') }}</button>
        </div>
      </div>
    </div>

    <!-- Regenerate Codes Modal -->
    <div v-if="showRegenerateModal" class="modal-overlay" @click.self="showRegenerateModal = false">
      <div class="modal-content">
        <h2>{{ t('mfa.regenerateTitle') }}</h2>
        
        <div v-if="!regeneratedCodes.length">
          <p>{{ t('mfa.regenerateWarning') }}</p>
          <div class="modal-actions">
            <button class="btn-cancel" @click="showRegenerateModal = false">{{ t('common.cancel') }}</button>
            <button class="btn-primary" @click="regenerateCodes">{{ t('mfa.regenerate') }}</button>
          </div>
        </div>
        
        <div v-else class="recovery-codes-display">
          <p class="warning-text">{{ t('mfa.saveRecoveryCodes') }}</p>
          <div class="recovery-codes-grid">
            <code v-for="code in regeneratedCodes" :key="code" class="recovery-code">{{ code }}</code>
          </div>
          <div class="modal-actions">
            <button class="btn-primary" @click="finishRegenerate">{{ t('mfa.done') }}</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useWebAuthn } from '../../composables/useWebAuthn';

const { t } = useI18n();

const emit = defineEmits<{
  (e: 'status-changed'): void
}>();

// State
const loading = ref(true);
const mfaStatus = ref({
  twoFactorEnabled: false,
  hasAuthenticator: false,
  recoveryCodesLeft: 0,
  hasPassword: true,
  emailMfaEnabled: false,
  enableTotpMfa: true,
  enableEmailMfa: true,
  enablePasskey: true
});

const allMfaDisabled = computed(() => {
  return !mfaStatus.value.enableTotpMfa && 
         !mfaStatus.value.enableEmailMfa && 
         !mfaStatus.value.enablePasskey;
});

// Email MFA (Phase 20.3)
const userEmail = ref('');
const emailMfaLoading = ref(false);
const emailMfaError = ref('');
const emailMfaSuccess = ref('');

// Passkeys (Phase 20.4)
const { registerPasskey } = useWebAuthn();
const passkeys = ref<any[]>([]);
const passkeyLoading = ref(false);
const passkeyError = ref('');
const passkeySuccess = ref('');
const passkeyToDelete = ref<any>(null);

const maskedEmail = computed(() => {
  if (!userEmail.value) return '';
  const [local, domain] = userEmail.value.split('@');
  if (!local || !domain) return userEmail.value;
  const maskedLocal = local.length > 2 
    ? local[0] + '***' + local[local.length - 1]
    : local[0] + '***';
  return `${maskedLocal}@${domain}`;
});

// Setup Modal
const showSetupModal = ref(false);
const setupInfo = ref({ sharedKey: '', authenticatorUri: '', qrCodeDataUri: '' });
const verifyCode = ref('');
const setupError = ref('');
const setupComplete = ref(false);
const recoveryCodes = ref<string[]>([]);

// Disable Modal
const showDisableModal = ref(false);
const disablePassword = ref('');
const disableTotpCode = ref('');
const disableError = ref('');

// Regenerate Modal
const showRegenerateModal = ref(false);
const regeneratedCodes = ref<string[]>([]);

onMounted(async () => {
  await loadMfaStatus();
  await loadPasskeys();
});

async function loadPasskeys() {
  try {
    const response = await fetch('/api/passkey/list', { credentials: 'include' });
    if (response.ok) {
      passkeys.value = await response.json();
    }
  } catch (err) {
    console.error('Failed to load passkeys:', err);
  }
}

async function registerNewPasskey() {
  passkeyLoading.value = true;
  passkeyError.value = '';
  passkeySuccess.value = '';
  
  try {
    await registerPasskey();
    passkeySuccess.value = t('mfa.passkey.registerSuccess');
    await loadPasskeys();
    setTimeout(() => { passkeySuccess.value = ''; }, 3000);
  } catch (err: any) {
    passkeyError.value = t(err.message) || t('mfa.errors.registerPasskeyFailed');
  } finally {
    passkeyLoading.value = false;
  }
}

function confirmDeletePasskey(pk: any) {
  passkeyToDelete.value = pk;
}

async function deletePasskey() {
  if (!passkeyToDelete.value) return;
  
  passkeyLoading.value = true;
  passkeyError.value = '';
  
  try {
    const response = await fetch(`/api/passkey/${passkeyToDelete.value.id}`, {
      method: 'DELETE',
      credentials: 'include'
    });
    
    if (response.ok) {
      passkeyToDelete.value = null;
      await loadPasskeys();
    } else {
      const result = await response.json();
      passkeyError.value = result.error || t('mfa.errors.deletePasskeyFailed');
    }
  } catch (err) {
    passkeyError.value = t('mfa.errors.deletePasskeyFailed');
  } finally {
    passkeyLoading.value = false;
  }
}

function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleString(undefined, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  });
}

async function loadMfaStatus() {
  loading.value = true;
  try {
    const response = await fetch('/api/account/mfa/status', { credentials: 'include' });
    if (response.ok) {
      mfaStatus.value = await response.json();
    }
    // Also fetch user email for Email MFA display
    const profileResponse = await fetch('/api/profile', { credentials: 'include' });
    if (profileResponse.ok) {
      const profile = await profileResponse.json();
      userEmail.value = profile.email || '';
    }
  } catch (err) {
    console.error('Failed to load MFA status:', err);
  } finally {
    loading.value = false;
  }
}

async function enableEmailMfa() {
  emailMfaLoading.value = true;
  emailMfaError.value = '';
  emailMfaSuccess.value = '';
  
  try {
    const response = await fetch('/api/account/mfa/email/enable', {
      method: 'POST',
      credentials: 'include'
    });
    
    if (response.ok) {
      await loadMfaStatus();
      emailMfaSuccess.value = t('mfa.emailMfaEnabled');
      emit('status-changed');
      setTimeout(() => { emailMfaSuccess.value = ''; }, 3000);
    } else {
      const result = await response.json();
      emailMfaError.value = result.message || t('mfa.errors.toggleFailed');
    }
  } catch (err) {
    emailMfaError.value = t('mfa.errors.toggleFailed');
  } finally {
    emailMfaLoading.value = false;
  }
}

async function disableEmailMfa() {
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
      emailMfaSuccess.value = t('mfa.emailMfaDisabled');
      emit('status-changed');
      setTimeout(() => { emailMfaSuccess.value = ''; }, 3000);
    } else {
      const result = await response.json();
      emailMfaError.value = result.message || t('mfa.errors.toggleFailed');
    }
  } catch (err) {
    emailMfaError.value = t('mfa.errors.toggleFailed');
  } finally {
    emailMfaLoading.value = false;
  }
}

async function startSetup() {
  showSetupModal.value = true;
  setupComplete.value = false;
  verifyCode.value = '';
  setupError.value = '';
  
  try {
    const response = await fetch('/api/account/mfa/setup', { credentials: 'include' });
    if (response.ok) {
      setupInfo.value = await response.json();
    }
  } catch (err) {
    setupError.value = t('mfa.errors.setupFailed');
  }
}

async function verifySetup() {
  setupError.value = '';
  
  try {
    const response = await fetch('/api/account/mfa/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ code: verifyCode.value })
    });
    
    const result = await response.json();
    if (result.success) {
      recoveryCodes.value = result.recoveryCodes || [];
      setupComplete.value = true;
    } else {
      // Translate error key from API (e.g., 'invalidCode' -> mfa.errors.invalidCode)
      const errorKey = result.error ? `mfa.errors.${result.error}` : 'mfa.errors.invalidCode';
      setupError.value = t(errorKey);
    }
  } catch (err) {
    setupError.value = t('mfa.errors.verifyFailed');
  }
}

function cancelSetup() {
  showSetupModal.value = false;
  setupInfo.value = { sharedKey: '', authenticatorUri: '', qrCodeDataUri: '' };
  verifyCode.value = '';
  setupError.value = '';
}

function finishSetup() {
  showSetupModal.value = false;
  loadMfaStatus();
  emit('status-changed');
}

async function disableMfa() {
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
      emit('status-changed');
    } else {
      const result = await response.json();
      // Translate error key from API (e.g., 'invalidPassword' -> mfa.errors.invalidPassword)
      const errorKey = result.error ? `mfa.errors.${result.error}` : 'mfa.errors.disableFailed';
      disableError.value = t(errorKey);
    }
  } catch (err) {
    disableError.value = t('mfa.errors.disableFailed');
  }
}

function cancelDisable() {
  showDisableModal.value = false;
  disablePassword.value = '';
  disableTotpCode.value = '';
  disableError.value = '';
}

async function regenerateCodes() {
  try {
    const response = await fetch('/api/account/mfa/recovery-codes', {
      method: 'POST',
      credentials: 'include'
    });
    
    if (response.ok) {
      const result = await response.json();
      regeneratedCodes.value = result.recoveryCodes || [];
    }
  } catch (err) {
    console.error('Failed to regenerate codes:', err);
  }
}

function finishRegenerate() {
  showRegenerateModal.value = false;
  regeneratedCodes.value = [];
  loadMfaStatus();
}
</script>

<style scoped>
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
