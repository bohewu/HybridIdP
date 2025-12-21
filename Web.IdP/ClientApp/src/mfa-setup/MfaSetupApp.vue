<template>
  <div class="mfa-setup-container">
    <!-- Header -->
    <div class="setup-header">
      <div class="header-icon">
        <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
            d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
        </svg>
      </div>
      <h1 class="title">{{ t('mfa.setupPageTitle') }}</h1>
      
      <!-- Grace Period Info -->
      <div v-if="!gracePeriodExpired" class="grace-info">
        <p class="grace-text">
          {{ t('mfa.gracePeriodMessage', { days: remainingGraceDays }) }}
        </p>
      </div>
      <div v-else class="grace-expired">
        <p class="expired-text">{{ t('mfa.gracePeriodExpiredMessage') }}</p>
      </div>
    </div>

    <!-- Explanation Info Box -->
    <div class="info-box">
      <div class="info-icon">
        <svg viewBox="0 0 20 20" fill="currentColor">
          <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd" />
        </svg>
      </div>
      <div class="info-content">
        <h3>{{ t('mfa.whyRequiredTitle') }}</h3>
        <p>{{ t('mfa.whyRequiredDescription') }}</p>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="loading-state">
      <div class="spinner"></div>
    </div>

    <!-- MFA Settings Section -->
    <div v-else class="mfa-section">
      <!-- All MFA Disabled -->
      <div v-if="allMfaDisabled" class="all-disabled-message">
        <p>{{ t('mfa.allDisabledTitle') }}</p>
        <p class="secondary">{{ t('mfa.allDisabledDescription') }}</p>
      </div>

      <!-- TOTP Section -->
      <div v-if="mfaStatus.enableTotpMfa" class="mfa-option" :class="{ enabled: mfaStatus.twoFactorEnabled }">
        <div class="option-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>
          </svg>
        </div>
        <div class="option-info">
          <h3>{{ t('mfa.title') }}</h3>
          <p v-if="mfaStatus.twoFactorEnabled">{{ t('mfa.enabled') }}</p>
          <p v-else>{{ t('mfa.enableDescription') }}</p>
        </div>
        <button v-if="!mfaStatus.twoFactorEnabled" class="btn-enable" @click="startTotpSetup" :disabled="settingUp">
          {{ settingUp ? '...' : t('mfa.enable') }}
        </button>
        <span v-else class="status-badge enabled">✓</span>
      </div>

      <!-- Email MFA Section -->
      <div v-if="mfaStatus.enableEmailMfa" class="mfa-option" :class="{ enabled: mfaStatus.emailMfaEnabled }">
        <div class="option-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"></path>
            <polyline points="22,6 12,13 2,6"></polyline>
          </svg>
        </div>
        <div class="option-info">
          <h3>{{ t('mfa.emailMfa') }}</h3>
          <p v-if="mfaStatus.emailMfaEnabled">{{ t('mfa.emailMfaEnabled') }}</p>
          <p v-else>{{ t('mfa.emailMfaDescription') }}</p>
        </div>
        <button v-if="!mfaStatus.emailMfaEnabled" class="btn-enable" @click="enableEmailMfa" :disabled="emailLoading">
          {{ emailLoading ? '...' : t('mfa.enable') }}
        </button>
        <span v-else class="status-badge enabled">✓</span>
      </div>

      <!-- Passkey Section -->
      <div v-if="mfaStatus.enablePasskey" class="mfa-option">
        <div class="option-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>
          </svg>
        </div>
        <div class="option-info">
          <h3>{{ t('mfa.passkey.title') }}</h3>
          <p>{{ t('mfa.passkey.description') }}</p>
          <p v-if="passkeys.length > 0" class="passkey-count">{{ passkeys.length }} {{ t('mfa.passkey.registered') }}</p>
        </div>
        <button class="btn-enable" @click="registerPasskey" :disabled="passkeyLoading || passkeyBlocked">
          {{ passkeyLoading ? '...' : t('mfa.passkey.register') }}
        </button>
      </div>

      <!-- Success Message -->
      <div v-if="successMessage" class="success-message">{{ successMessage }}</div>
      <div v-if="errorMessage" class="error-message">{{ errorMessage }}</div>
    </div>

    <!-- TOTP Setup Modal -->
    <div v-if="showTotpModal" class="modal-overlay" @click.self="showTotpModal = false">
      <div class="modal-content">
        <h2>{{ t('mfa.setupTitle') }}</h2>
        
        <div v-if="!totpComplete">
          <p>{{ t('mfa.scanQrCode') }}</p>
          <div class="qr-container">
            <img v-if="totpSetup.qrCodeDataUri" :src="totpSetup.qrCodeDataUri" alt="QR Code" />
          </div>
          <p class="manual-key">{{ t('mfa.cantScan') }}</p>
          <code class="shared-key">{{ totpSetup.sharedKey }}</code>
          
          <div class="verify-section">
            <label>{{ t('mfa.enterCode') }}</label>
            <input v-model="totpCode" type="text" inputmode="numeric" maxlength="6" placeholder="000000" @keyup.enter="verifyTotp" />
          </div>
          
          <div class="modal-actions">
            <button class="btn-cancel" @click="showTotpModal = false">{{ t('common.cancel') }}</button>
            <button class="btn-primary" @click="verifyTotp" :disabled="totpCode.length !== 6">{{ t('mfa.verify') }}</button>
          </div>
        </div>
        
        <div v-else class="recovery-codes">
          <h3>{{ t('mfa.setupComplete') }}</h3>
          <p class="warning">{{ t('mfa.saveRecoveryCodes') }}</p>
          <div class="codes-grid">
            <code v-for="code in recoveryCodes" :key="code">{{ code }}</code>
          </div>
          <button class="btn-primary" @click="finishTotpSetup">{{ t('mfa.done') }}</button>
        </div>
      </div>
    </div>

    <!-- Footer Actions -->
    <div class="footer-actions">
      <div v-if="!gracePeriodExpired" class="skip-section">
        <form :action="skipActionUrl" method="post">
          <input type="hidden" name="ReturnUrl" :value="returnUrl" />
          <input type="hidden" name="__RequestVerificationToken" :value="csrfToken" />
          <button type="submit" class="skip-btn">
            {{ t('mfa.skipForNow') }}
          </button>
        </form>
      </div>
      <div v-else class="logout-section">
        <a :href="logoutUrl" class="logout-link">{{ t('mfa.logout') }}</a>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useWebAuthn } from '../composables/useWebAuthn'

const { t } = useI18n()
const { registerPasskey: webAuthnRegister } = useWebAuthn()

// Data from HTML
const gracePeriodExpired = ref(false)
const remainingGraceDays = ref(0)
const returnUrl = ref('/')
const csrfToken = ref('')
const skipActionUrl = ref('/Account/MfaSetup?handler=Skip')
const logoutUrl = ref('/Account/Logout')

// State
const loading = ref(true)
const mfaStatus = ref({
  twoFactorEnabled: false,
  emailMfaEnabled: false,
  enableTotpMfa: true,
  enableEmailMfa: true,
  enablePasskey: true
})
const passkeys = ref([])
const policy = ref({ requireMfaForPasskey: false })

// UI State
const settingUp = ref(false)
const emailLoading = ref(false)
const passkeyLoading = ref(false)
const successMessage = ref('')
const errorMessage = ref('')

// TOTP Modal
const showTotpModal = ref(false)
const totpSetup = ref({ sharedKey: '', qrCodeDataUri: '' })
const totpCode = ref('')
const totpComplete = ref(false)
const recoveryCodes = ref([])

const allMfaDisabled = computed(() => {
  return !mfaStatus.value.enableTotpMfa && !mfaStatus.value.enableEmailMfa && !mfaStatus.value.enablePasskey
})

const passkeyBlocked = computed(() => {
  return policy.value.requireMfaForPasskey && !mfaStatus.value.twoFactorEnabled && !mfaStatus.value.emailMfaEnabled
})

onMounted(async () => {
  // Read config from data attributes
  const mountEl = document.getElementById('mfa-setup-app')
  if (mountEl) {
    gracePeriodExpired.value = mountEl.dataset.gracePeriodExpired === 'true'
    remainingGraceDays.value = parseInt(mountEl.dataset.remainingGraceDays || '0', 10)
    returnUrl.value = mountEl.dataset.returnUrl || '/'
    csrfToken.value = mountEl.dataset.csrfToken || ''
  }
  
  await loadData()
})

async function loadData() {
  loading.value = true
  try {
    // Use the new mfa-setup endpoints that accept TwoFactorUserIdScheme
    const [statusRes, policyRes, passkeysRes] = await Promise.all([
      fetch('/api/account/mfa-setup/status', { credentials: 'include' }),
      fetch('/api/account/mfa-setup/policy', { credentials: 'include' }),
      fetch('/api/account/mfa-setup/passkeys', { credentials: 'include' })
    ])
    
    if (statusRes.ok) mfaStatus.value = await statusRes.json()
    if (policyRes.ok) policy.value = await policyRes.json()
    if (passkeysRes.ok) passkeys.value = await passkeysRes.json()
  } catch (err) {
    console.error('Failed to load MFA data:', err)
    errorMessage.value = 'Failed to load MFA settings'
  } finally {
    loading.value = false
  }
}

async function startTotpSetup() {
  settingUp.value = true
  errorMessage.value = ''
  
  try {
    const res = await fetch('/api/account/mfa-setup/totp/setup', { credentials: 'include' })
    if (res.ok) {
      totpSetup.value = await res.json()
      showTotpModal.value = true
      totpCode.value = ''
      totpComplete.value = false
    } else {
      errorMessage.value = t('mfa.errors.setupFailed')
    }
  } catch (err) {
    errorMessage.value = t('mfa.errors.setupFailed')
  } finally {
    settingUp.value = false
  }
}

async function verifyTotp() {
  errorMessage.value = ''
  
  try {
    const res = await fetch('/api/account/mfa-setup/totp/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ code: totpCode.value })
    })
    
    const result = await res.json()
    if (result.success) {
      recoveryCodes.value = result.recoveryCodes || []
      totpComplete.value = true
      await loadData()
    } else {
      errorMessage.value = t('mfa.errors.invalidCode')
    }
  } catch (err) {
    errorMessage.value = t('mfa.errors.verifyFailed')
  }
}

function finishTotpSetup() {
  showTotpModal.value = false
  successMessage.value = t('mfa.setupComplete')
  setTimeout(() => { successMessage.value = '' }, 5000)
}

async function enableEmailMfa() {
  emailLoading.value = true
  errorMessage.value = ''
  
  try {
    const res = await fetch('/api/account/mfa-setup/email/enable', {
      method: 'POST',
      credentials: 'include'
    })
    
    if (res.ok) {
      await loadData()
      successMessage.value = t('mfa.emailMfaEnabled')
      setTimeout(() => { successMessage.value = '' }, 3000)
    } else {
      errorMessage.value = t('mfa.errors.toggleFailed')
    }
  } catch (err) {
    errorMessage.value = t('mfa.errors.toggleFailed')
  } finally {
    emailLoading.value = false
  }
}

async function registerPasskey() {
  passkeyLoading.value = true
  errorMessage.value = ''
  
  try {
    await webAuthnRegister()
    await loadData()
    successMessage.value = t('mfa.passkey.registerSuccess')
    setTimeout(() => { successMessage.value = '' }, 3000)
  } catch (err) {
    errorMessage.value = err.message || t('mfa.errors.registerPasskeyFailed')
  } finally {
    passkeyLoading.value = false
  }
}
</script>

<style scoped>
.mfa-setup-container {
  max-width: 600px;
  margin: 0 auto;
  padding: 2rem 1rem;
}

.setup-header {
  text-align: center;
  margin-bottom: 2rem;
}

.header-icon {
  display: flex;
  justify-content: center;
  margin-bottom: 1rem;
}

.header-icon .icon {
  width: 48px;
  height: 48px;
  color: #2563eb;
}

.title {
  font-size: 1.5rem;
  font-weight: 400;
  color: #1f2937;
  margin-bottom: 0.75rem;
}

.grace-info {
  background-color: #dbeafe;
  border: 1px solid #93c5fd;
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  margin-top: 1rem;
}

.grace-text {
  color: #1e40af;
  font-size: 0.875rem;
  margin: 0;
}

.grace-expired {
  background-color: #fee2e2;
  border: 1px solid #fca5a5;
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  margin-top: 1rem;
}

.expired-text {
  color: #dc2626;
  font-size: 0.875rem;
  font-weight: 500;
  margin: 0;
}

.info-box {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  background-color: #eff6ff;
  border: 1px solid #bfdbfe;
  border-radius: 0.5rem;
  padding: 1rem;
  margin-bottom: 1.5rem;
}

.info-icon svg {
  width: 1.25rem;
  height: 1.25rem;
  color: #60a5fa;
  flex-shrink: 0;
}

.info-content h3 {
  font-size: 0.875rem;
  font-weight: 500;
  color: #1e40af;
  margin: 0 0 0.25rem 0;
}

.info-content p {
  font-size: 0.75rem;
  color: #1e3a8a;
  margin: 0;
}

/* Loading State */
.loading-state {
  display: flex;
  justify-content: center;
  padding: 3rem;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid #e5e7eb;
  border-top-color: #2563eb;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

/* MFA Section */
.mfa-section {
  background-color: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  overflow: hidden;
  margin-bottom: 1.5rem;
}

.all-disabled-message {
  text-align: center;
  padding: 2rem;
  color: #6b7280;
}

.all-disabled-message .secondary {
  font-size: 0.875rem;
  margin-top: 0.5rem;
}

/* MFA Options */
.mfa-option {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
  border-bottom: 1px solid #e5e7eb;
}

.mfa-option:last-child {
  border-bottom: none;
}

.mfa-option.enabled {
  background-color: #f0fdf4;
}

.option-icon {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f3f4f6;
  flex-shrink: 0;
}

.option-icon svg {
  width: 20px;
  height: 20px;
  color: #6b7280;
}

.mfa-option.enabled .option-icon {
  background: #dcfce7;
}

.mfa-option.enabled .option-icon svg {
  color: #16a34a;
}

.option-info {
  flex: 1;
  min-width: 0;
}

.option-info h3 {
  font-size: 0.9375rem;
  font-weight: 500;
  color: #1f2937;
  margin: 0 0 0.25rem 0;
}

.option-info p {
  font-size: 0.8125rem;
  color: #6b7280;
  margin: 0;
}

.btn-enable {
  background: #2563eb;
  color: white;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 0.375rem;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
}

.btn-enable:hover {
  background: #1d4ed8;
}

.btn-enable:disabled {
  background: #9ca3af;
  cursor: not-allowed;
}

.status-badge.enabled {
  color: #16a34a;
  font-size: 1.25rem;
}

/* Messages */
.success-message {
  padding: 0.75rem 1rem;
  background: #dcfce7;
  color: #166534;
  font-size: 0.875rem;
}

.error-message {
  padding: 0.75rem 1rem;
  background: #fee2e2;
  color: #dc2626;
  font-size: 0.875rem;
}

/* Modal */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
}

.modal-content {
  background: white;
  border-radius: 0.5rem;
  padding: 1.5rem;
  max-width: 400px;
  width: 90%;
  max-height: 90vh;
  overflow-y: auto;
}

.modal-content h2 {
  font-size: 1.25rem;
  margin: 0 0 1rem 0;
}

.qr-container {
  text-align: center;
  padding: 1rem 0;
}

.qr-container img {
  max-width: 200px;
}

.manual-key {
  font-size: 0.875rem;
  color: #6b7280;
  margin-bottom: 0.5rem;
}

.shared-key {
  display: block;
  font-size: 0.75rem;
  background: #f3f4f6;
  padding: 0.5rem;
  border-radius: 0.25rem;
  word-break: break-all;
  margin-bottom: 1rem;
}

.verify-section label {
  display: block;
  font-size: 0.875rem;
  margin-bottom: 0.5rem;
}

.verify-section input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font-size: 1.5rem;
  letter-spacing: 0.5em;
  text-align: center;
}

.modal-actions {
  display: flex;
  gap: 0.75rem;
  justify-content: flex-end;
  margin-top: 1rem;
}

.btn-cancel {
  background: none;
  border: 1px solid #d1d5db;
  padding: 0.5rem 1rem;
  border-radius: 0.375rem;
  cursor: pointer;
}

.btn-primary {
  background: #2563eb;
  color: white;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 0.375rem;
  cursor: pointer;
}

.btn-primary:disabled {
  background: #9ca3af;
}

/* Recovery Codes */
.recovery-codes {
  text-align: center;
}

.recovery-codes .warning {
  background: #fef3c7;
  padding: 0.75rem;
  border-radius: 0.375rem;
  font-size: 0.875rem;
  color: #92400e;
  margin-bottom: 1rem;
}

.codes-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.codes-grid code {
  background: #f3f4f6;
  padding: 0.5rem;
  border-radius: 0.25rem;
  font-size: 0.75rem;
}

/* Footer */
.footer-actions {
  display: flex;
  justify-content: center;
  padding-top: 1rem;
}

.skip-btn {
  background: none;
  border: none;
  color: #6b7280;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  padding: 0.5rem 1rem;
}

.skip-btn:hover {
  color: #1f2937;
}

.logout-link {
  color: #6b7280;
  font-size: 0.875rem;
  font-weight: 500;
  text-decoration: none;
  padding: 0.5rem 1rem;
}

.logout-link:hover {
  color: #1f2937;
}
</style>
