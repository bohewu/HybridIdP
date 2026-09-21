<template>
  <section class="recovery-email-settings" aria-labelledby="recovery-email-title">
    <div class="recovery-email-header">
      <div class="recovery-email-icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M4 4h16v16H4z"></path>
          <path d="m4 7 8 6 8-6"></path>
        </svg>
      </div>
      <div class="recovery-email-copy">
        <h3 id="recovery-email-title">{{ t('mfa.recoveryEmail.title') }}</h3>
        <p>{{ t('mfa.recoveryEmail.description') }}</p>
        <p v-if="status.isConfigured" class="recovery-email-address" data-testid="recovery-email-masked">
          {{ status.maskedAddress || t('mfa.recoveryEmail.maskedUnavailable') }}
          <span :class="['recovery-status', status.isVerified ? 'verified' : 'pending']">
            {{ status.isVerified ? t('mfa.recoveryEmail.verified') : t('mfa.recoveryEmail.pending') }}
          </span>
        </p>
        <p v-else-if="!statusError" class="recovery-email-empty">{{ t('mfa.recoveryEmail.notConfigured') }}</p>
        <p v-if="statusError" class="recovery-message error" role="alert">{{ statusError }}</p>
        <p v-if="successMessage" class="recovery-message success" role="status">{{ successMessage }}</p>
      </div>
      <div class="recovery-email-actions">
        <button v-if="statusError" class="recovery-btn secondary" type="button" @click="loadStatus">
          {{ t('mfa.recoveryEmail.retry') }}
        </button>
        <button v-else class="recovery-btn primary" type="button" :disabled="loading" @click="openChange">
          {{ status.isConfigured ? t('mfa.recoveryEmail.change') : t('mfa.recoveryEmail.add') }}
        </button>
        <button
          v-if="status.isConfigured && !statusError"
          class="recovery-btn danger"
          type="button"
          :disabled="loading"
          @click="revoke"
        >
          {{ t('mfa.recoveryEmail.remove') }}
        </button>
      </div>
    </div>

    <div v-if="showDialog" class="recovery-modal-overlay" @click.self="closeDialog">
      <div class="recovery-modal" role="dialog" aria-modal="true" aria-labelledby="recovery-dialog-title" :aria-busy="submitting">
        <h2 id="recovery-dialog-title">
          {{ mode === 'change' ? t('mfa.recoveryEmail.changeTitle') : t('mfa.recoveryEmail.verifyTitle') }}
        </h2>
        <p class="recovery-modal-description">
          {{ mode === 'change' ? t('mfa.recoveryEmail.changeHelp') : t('mfa.recoveryEmail.verifyHelp') }}
        </p>
        <p v-if="dialogError" class="recovery-message error" role="alert">{{ dialogError }}</p>

        <form v-if="mode === 'change'" @submit.prevent="beginChange">
          <label for="recovery-email-candidate">{{ t('mfa.recoveryEmail.addressLabel') }}</label>
          <input
            id="recovery-email-candidate"
            v-model.trim="candidateAddress"
            type="email"
            autocomplete="email"
            required
            :disabled="submitting"
          />
          <div class="recovery-modal-actions">
            <button class="recovery-btn secondary" type="button" :disabled="submitting" @click="closeDialog">{{ t('common.cancel') }}</button>
            <button class="recovery-btn primary" type="submit" :disabled="submitting || !candidateAddress">
              {{ submitting ? t('mfa.recoveryEmail.sending') : t('mfa.recoveryEmail.sendVerification') }}
            </button>
          </div>
        </form>

        <form v-else @submit.prevent="verify">
          <label for="recovery-email-code">{{ t('mfa.recoveryEmail.codeLabel') }}</label>
          <input
            id="recovery-email-code"
            v-model.trim="verificationCode"
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="8"
            required
            :disabled="submitting"
          />
          <div class="recovery-modal-actions">
            <button class="recovery-btn secondary" type="button" :disabled="submitting" @click="closeDialog">{{ t('common.cancel') }}</button>
            <button class="recovery-btn primary" type="submit" :disabled="submitting || !verificationCode">
              {{ submitting ? t('mfa.recoveryEmail.verifying') : t('mfa.recoveryEmail.verify') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </section>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { accountApi } from '../../services/accountApi'

const props = defineProps({ csrfToken: { type: String, default: '' } })
const { t } = useI18n()
const status = reactive({ isConfigured: false, isVerified: false, maskedAddress: '' })
const loading = ref(true)
const submitting = ref(false)
const statusError = ref('')
const dialogError = ref('')
const successMessage = ref('')
const showDialog = ref(false)
const mode = ref('change')
const candidateAddress = ref('')
const verificationCode = ref('')

function outcomeMessage(outcome) {
  const known = ['invalid', 'missing', 'expired', 'exhausted', 'replayed', 'cooldown', 'highAssuranceRequired', 'unauthorized', 'unavailable']
  return t(`mfa.recoveryEmail.outcomes.${known.includes(outcome) ? outcome : 'unavailable'}`)
}

async function loadStatus() {
  loading.value = true
  statusError.value = ''
  try {
    const result = await accountApi.getRecoveryEmailStatus(props.csrfToken)
    if (!result.httpOk) {
      statusError.value = outcomeMessage(result.outcome)
      return
    }
    status.isConfigured = Boolean(result.isConfigured)
    status.isVerified = Boolean(result.isVerified)
    status.maskedAddress = result.maskedAddress || ''
  } catch {
    statusError.value = outcomeMessage('unavailable')
  } finally {
    loading.value = false
  }
}

function openChange() {
  mode.value = 'change'
  candidateAddress.value = ''
  verificationCode.value = ''
  dialogError.value = ''
  showDialog.value = true
}

function closeDialog() {
  if (submitting.value) return
  showDialog.value = false
  dialogError.value = ''
}

async function beginChange() {
  if (!candidateAddress.value || submitting.value) return
  submitting.value = true
  dialogError.value = ''
  try {
    const result = await accountApi.changeRecoveryEmail(candidateAddress.value, props.csrfToken)
    if (!result.httpOk || result.outcome !== 'success') {
      dialogError.value = outcomeMessage(result.outcome)
      return
    }
    mode.value = 'verify'
    candidateAddress.value = ''
    await loadStatus()
  } catch {
    dialogError.value = outcomeMessage('unavailable')
  } finally {
    submitting.value = false
  }
}

async function verify() {
  if (!verificationCode.value || submitting.value) return
  submitting.value = true
  dialogError.value = ''
  try {
    const result = await accountApi.verifyRecoveryEmail(verificationCode.value, props.csrfToken)
    if (!result.httpOk || result.outcome !== 'success') {
      dialogError.value = outcomeMessage(result.outcome)
      return
    }
    verificationCode.value = ''
    showDialog.value = false
    successMessage.value = t('mfa.recoveryEmail.verifiedSuccess')
    await loadStatus()
  } catch {
    dialogError.value = outcomeMessage('unavailable')
  } finally {
    submitting.value = false
  }
}

async function revoke() {
  if (!window.confirm(t('mfa.recoveryEmail.removeConfirm'))) return
  loading.value = true
  statusError.value = ''
  try {
    const result = await accountApi.revokeRecoveryEmail(props.csrfToken)
    if (!result.httpOk || result.outcome !== 'success') {
      statusError.value = outcomeMessage(result.outcome)
      return
    }
    successMessage.value = t('mfa.recoveryEmail.removedSuccess')
    await loadStatus()
  } catch {
    statusError.value = outcomeMessage('unavailable')
  } finally {
    loading.value = false
  }
}

onMounted(loadStatus)
</script>

<style scoped>
.recovery-email-settings { margin-top: 24px; padding-top: 24px; border-top: 1px solid #e8eaed; }
.recovery-email-header { display: flex; align-items: flex-start; gap: 16px; }
.recovery-email-icon { width: 40px; height: 40px; border-radius: 50%; display: flex; align-items: center; justify-content: center; background: #fef7e0; color: #b06000; flex: 0 0 auto; }
.recovery-email-icon svg { width: 20px; height: 20px; }
.recovery-email-copy { flex: 1; min-width: 0; }
.recovery-email-copy h3 { margin: 0 0 4px; font-size: 14px; font-weight: 500; color: #202124; }
.recovery-email-copy p { margin: 0 0 4px; font-size: 13px; color: #5f6368; line-height: 1.45; }
.recovery-email-address { font-family: 'Roboto Mono', monospace; overflow-wrap: anywhere; }
.recovery-status { display: inline-flex; margin-left: 8px; padding: 2px 7px; border-radius: 999px; font-family: inherit; font-size: 11px; font-weight: 500; }
.recovery-status.verified { background: #e6f4ea; color: #137333; }
.recovery-status.pending { background: #fef7e0; color: #8a4b08; }
.recovery-email-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; }
.recovery-btn { min-height: 38px; padding: 8px 14px; border-radius: 4px; border: 1px solid #dadce0; font-size: 13px; font-weight: 500; cursor: pointer; }
.recovery-btn:disabled { opacity: .6; cursor: not-allowed; }
.recovery-btn.primary { border-color: #1a73e8; background: #1a73e8; color: white; }
.recovery-btn.secondary { background: white; color: #1a73e8; }
.recovery-btn.danger { background: white; color: #c5221f; }
.recovery-message.error { color: #c5221f; }
.recovery-message.success { color: #137333; }
.recovery-modal-overlay { position: fixed; inset: 0; z-index: 1000; display: flex; align-items: center; justify-content: center; padding: 16px; background: rgba(0,0,0,.5); }
.recovery-modal { width: min(440px, 100%); max-height: 90vh; overflow-y: auto; padding: 24px; border-radius: 8px; background: white; }
.recovery-modal h2 { margin: 0 0 8px; font-size: 18px; font-weight: 500; color: #202124; }
.recovery-modal-description { margin: 0 0 20px; color: #5f6368; font-size: 13px; line-height: 1.5; }
.recovery-modal label { display: block; margin-bottom: 8px; color: #202124; font-size: 14px; }
.recovery-modal input { box-sizing: border-box; width: 100%; min-height: 42px; padding: 10px 12px; border: 1px solid #dadce0; border-radius: 4px; font-size: 14px; }
.recovery-modal input:focus { outline: none; border-color: #1a73e8; box-shadow: 0 0 0 2px rgba(26,115,232,.2); }
.recovery-modal-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 24px; }
@media (max-width: 600px) { .recovery-email-header { flex-wrap: wrap; } .recovery-email-copy { flex-basis: calc(100% - 56px); } .recovery-email-actions { width: 100%; } .recovery-email-actions .recovery-btn { flex: 1; } }
</style>
