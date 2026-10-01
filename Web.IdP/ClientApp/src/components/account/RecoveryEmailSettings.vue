<template>
  <section class="recovery-email-settings" aria-labelledby="recovery-email-title" :aria-busy="loading">
    <div class="recovery-email-header">
      <div class="recovery-email-icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 4h16v16H4z"/><path d="m4 7 8 6 8-6"/></svg>
      </div>
      <div class="recovery-email-copy">
        <h3 id="recovery-email-title">{{ t('mfa.recoveryEmail.title') }}</h3>
        <p>{{ t('mfa.recoveryEmail.description') }}</p>
        <p class="recovery-warning">{{ t('mfa.recoveryEmail.independentWarning') }}</p>
        <p v-if="loading" role="status">{{ t('mfa.recoveryEmail.loading') }}</p>
        <template v-else-if="!statusError">
          <template v-if="selectionStatus">
            <p data-testid="recovery-email-mode"><strong>{{ t('mfa.recoveryEmail.currentMode') }}</strong> {{ t(`mfa.recoveryEmail.modes.${status.mode}`) }}</p>
            <p class="recovery-email-address" data-testid="recovery-email-masked">{{ t('mfa.recoveryEmail.effectiveAddress') }} {{ status.maskedAddress || t('mfa.recoveryEmail.noEffectiveAddress') }}</p>
            <p class="recovery-email-address">{{ t('mfa.recoveryEmail.defaultAddress') }} {{ status.maskedDefaultAddress || t('mfa.recoveryEmail.noDefault') }}</p>
            <p v-if="!status.enabled" data-testid="recovery-email-readonly">{{ t('mfa.recoveryEmail.readOnly') }}</p>
            <p v-else>{{ t('mfa.recoveryEmail.freshnessHelp') }}</p>
          </template>
          <p v-else-if="status.isConfigured" class="recovery-email-address" data-testid="recovery-email-masked">
            {{ status.maskedAddress || t('mfa.recoveryEmail.maskedUnavailable') }}
            <span :class="['recovery-status', status.isVerified ? 'verified' : 'pending']">{{ t(status.isVerified ? 'mfa.recoveryEmail.verified' : 'mfa.recoveryEmail.pending') }}</span>
          </p>
          <p v-else>{{ t('mfa.recoveryEmail.notConfigured') }}</p>
        </template>
        <p v-if="statusError" class="recovery-message error" role="alert">{{ statusError }}</p>
        <p v-if="actionError" class="recovery-message error" role="alert">{{ actionError }}</p>
        <p v-if="successMessage" class="recovery-message success" role="status">{{ successMessage }}</p>
        <p v-if="needsHelp"><a href="/Account/MfaSetup">{{ t('mfa.recoveryEmail.enrollmentHelp') }}</a> {{ t('mfa.recoveryEmail.adminHelp') }}</p>
      </div>
    </div>
    <div v-if="!loading && !statusError" class="recovery-email-actions">
      <template v-if="selectionStatus && status.enabled">
        <button class="recovery-btn secondary" type="button" :disabled="submitting" data-testid="recovery-reauthenticate" @click="openDialog('reauthenticate', $event)">{{ t('mfa.recoveryEmail.reauthenticate') }}</button>
        <button ref="changeButton" class="recovery-btn primary" type="button" :disabled="submitting" @click="openDialog('change', $event)">{{ t('mfa.recoveryEmail.changeTitle') }}</button>
        <button ref="defaultButton" class="recovery-btn secondary" type="button" :disabled="submitting || !status.maskedDefaultAddress" data-testid="recovery-use-default" @click="prepareDefault($event)">{{ t('mfa.recoveryEmail.useDefault') }}</button>
      </template>
      <template v-else-if="!selectionStatus">
        <button ref="changeButton" class="recovery-btn primary" type="button" :disabled="submitting" @click="openDialog('change', $event)">{{ t(status.isConfigured ? 'mfa.recoveryEmail.change' : 'mfa.recoveryEmail.add') }}</button>
        <button v-if="status.isConfigured" class="recovery-btn danger" type="button" :disabled="submitting" @click="revoke">{{ t('mfa.recoveryEmail.remove') }}</button>
        <button v-if="status.isConfigured && !status.isVerified" class="recovery-btn secondary" type="button" @click="openDialog('verify', $event)">{{ t('mfa.recoveryEmail.verify') }}</button>
      </template>
    </div>
    <button v-if="statusError" class="recovery-btn secondary" type="button" :disabled="loading" @click="loadStatus">{{ t('mfa.recoveryEmail.retry') }}</button>
    <button v-if="statusError && selectionStatus && status.enabled" class="recovery-btn secondary" type="button" :disabled="submitting || loading" @click="openDialog('reauthenticate', $event)">{{ t('mfa.recoveryEmail.reauthenticate') }}</button>
    <div v-if="selectionStatus && status.maskedPendingAddress && !loading && !statusError" class="recovery-pending" data-testid="recovery-pending">
      <p><strong>{{ t('mfa.recoveryEmail.pending') }}</strong> <span class="recovery-email-address">{{ status.maskedPendingAddress }}</span></p>
      <p>{{ t('mfa.recoveryEmail.pendingHelp') }}</p>
      <p>{{ t('mfa.recoveryEmail.expiresAt') }} <time :datetime="status.pendingExpiresAtUtc">{{ formatDate(status.pendingExpiresAtUtc) }}</time> <span v-if="pendingExpired">{{ t('mfa.recoveryEmail.pendingExpired') }}</span></p>
      <p v-if="cooldownSeconds">{{ t('mfa.recoveryEmail.resendIn', { seconds: cooldownSeconds }) }}</p>
      <div v-if="status.enabled" class="recovery-email-actions">
        <button class="recovery-btn primary" type="button" :disabled="submitting || pendingExpired" @click="openDialog('verify', $event)">{{ t('mfa.recoveryEmail.verify') }}</button>
        <button class="recovery-btn secondary" type="button" :disabled="submitting || pendingExpired || cooldownSeconds > 0" data-testid="recovery-resend" @click="resend">{{ t('mfa.recoveryEmail.resend') }}</button>
        <button class="recovery-btn secondary" type="button" :disabled="submitting" data-testid="recovery-cancel" @click="cancelPending">{{ t('mfa.recoveryEmail.cancelPending') }}</button>
      </div>
    </div>

    <div v-if="showDialog" class="recovery-modal-overlay" @click.self="closeDialog">
      <div ref="dialog" class="recovery-modal" role="dialog" aria-modal="true" aria-labelledby="recovery-dialog-title" aria-describedby="recovery-dialog-description" :aria-busy="submitting" @keydown="dialogKeydown">
        <h2 id="recovery-dialog-title">{{ t(`mfa.recoveryEmail.${mode}Title`) }}</h2>
        <p id="recovery-dialog-description" class="recovery-modal-description">{{ t(`mfa.recoveryEmail.${mode}Help`) }}</p>
        <p v-if="dialogError" class="recovery-message error" role="alert">{{ dialogError }}</p>
        <form v-if="mode === 'change' || mode === 'verify'" @submit.prevent="mode === 'change' ? beginChange() : verify()">
          <template v-if="mode === 'change'">
            <label for="recovery-email-candidate">{{ t('mfa.recoveryEmail.addressLabel') }}</label>
            <input id="recovery-email-candidate" v-model.trim="candidateAddress" type="email" autocomplete="email" required :disabled="submitting" />
          </template>
          <template v-else>
            <p v-if="status.maskedPendingAddress" class="recovery-email-address">{{ status.maskedPendingAddress }}</p>
            <label for="recovery-email-code">{{ t('mfa.recoveryEmail.codeLabel') }}</label>
            <input id="recovery-email-code" v-model.trim="verificationCode" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="8" required :disabled="submitting" />
          </template>
          <div class="recovery-modal-actions">
            <button class="recovery-btn secondary" type="button" :disabled="submitting" @click="closeDialog">{{ t('common.cancel') }}</button>
            <button class="recovery-btn primary" type="submit" :disabled="submitting || !(mode === 'change' ? candidateAddress : verificationCode)">{{ t(mode === 'change' ? (submitting ? 'mfa.recoveryEmail.sending' : 'mfa.recoveryEmail.sendVerification') : (submitting ? 'mfa.recoveryEmail.verifying' : 'mfa.recoveryEmail.verify')) }}</button>
          </div>
        </form>
        <template v-else>
          <p v-if="mode === 'default'" class="recovery-email-address" data-testid="recovery-default-confirm-mask">{{ defaultConfirmation.maskedAddress }}</p>
          <p v-if="mode === 'hardware'">{{ t('mfa.recoveryEmail.hardwareOnly') }}</p>
          <div class="recovery-modal-actions">
            <button class="recovery-btn secondary" type="button" :disabled="submitting" @click="closeDialog">{{ t('common.cancel') }}</button>
            <button class="recovery-btn primary" type="button" :disabled="submitting" data-testid="recovery-dialog-confirm" @click="mode === 'default' ? useDefault() : mode === 'hardware' ? continueLogin() : reauthenticate()">{{ t(mode === 'default' ? 'mfa.recoveryEmail.confirmDefault' : 'mfa.recoveryEmail.continueSignIn') }}</button>
          </div>
        </template>
      </div>
    </div>
  </section>
</template>

<script setup>
import { computed, nextTick, onMounted, onUnmounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { accountApi } from '../../services/accountApi'

const props = defineProps({ csrfToken: { type: String, default: '' } })
const { t, locale } = useI18n()
const status = reactive({})
const selectionStatus = computed(() => ['Legacy', 'UseDefault', 'UseCustom', 'Disabled'].includes(status.mode))
const loading = ref(true)
const submitting = ref(false)
const statusError = ref('')
const dialogError = ref('')
const actionError = ref('')
const successMessage = ref('')
const needsHelp = ref(false)
const showDialog = ref(false)
const mode = ref('change')
const candidateAddress = ref('')
const verificationCode = ref('')
const dialog = ref(null)
const changeButton = ref(null)
const defaultButton = ref(null)
const defaultConfirmation = ref({})
const loginUrl = ref('')
const now = ref(Date.now())
const pendingExpired = computed(() => Boolean(status.pendingExpiresAtUtc && Date.parse(status.pendingExpiresAtUtc) <= now.value))
const cooldownSeconds = computed(() => Math.max(0, Math.ceil(((Date.parse(status.nextSendAllowedAtUtc) || 0) - now.value) / 1000)))
let clockTimer
let returnFocus

function formatDate(value) {
  return value ? new Date(value).toLocaleString(locale?.value || undefined) : t('mfa.recoveryEmail.maskedUnavailable')
}
function outcomeMessage(outcome) {
  const known = ['invalid', 'missing', 'expired', 'exhausted', 'replayed', 'cooldown', 'highAssuranceRequired', 'hardwareReauthenticationUnavailable', 'unauthorized', 'unavailable']
  if (['highAssuranceRequired', 'hardwareReauthenticationUnavailable', 'unavailable'].includes(outcome)) needsHelp.value = true
  return t(`mfa.recoveryEmail.outcomes.${known.includes(outcome) ? outcome : 'unavailable'}`)
}
async function loadStatus() {
  loading.value = true
  statusError.value = ''
  try {
    const result = await accountApi.getRecoveryEmailStatus(props.csrfToken)
    if (!result.httpOk) { statusError.value = outcomeMessage(result.outcome); return false }
    for (const key of Object.keys(status)) delete status[key]
    Object.assign(status, result)
    now.value = Date.now()
    return true
  } catch { statusError.value = outcomeMessage('unavailable'); return false }
  finally { loading.value = false }
}
async function focusDialog() {
  await nextTick()
  dialog.value?.querySelector('input:not(:disabled), button:not(:disabled)')?.focus()
}
function openDialog(nextMode, event) {
  returnFocus = event?.currentTarget || document.activeElement
  mode.value = nextMode
  candidateAddress.value = ''
  verificationCode.value = ''
  dialogError.value = ''
  successMessage.value = ''
  actionError.value = ''
  showDialog.value = true
  focusDialog()
}
function closeDialog() {
  if (submitting.value) return
  showDialog.value = false
  dialogError.value = ''
  candidateAddress.value = ''
  verificationCode.value = ''
  defaultConfirmation.value = {}
  loginUrl.value = ''
  nextTick(() => {
    const target = returnFocus?.isConnected ? returnFocus : mode.value === 'default' ? defaultButton.value : changeButton.value
    target?.focus()
  })
}
function dialogKeydown(event) {
  if (event.key === 'Escape') { event.preventDefault(); closeDialog(); return }
  if (event.key !== 'Tab') return
  const controls = [...dialog.value.querySelectorAll('input:not(:disabled), button:not(:disabled), a[href]')]
  const first = controls[0], last = controls[controls.length - 1]
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
}
async function mutate(request, successKey, afterSuccess) {
  if (submitting.value || (selectionStatus.value && !status.enabled)) return false
  submitting.value = true
  dialogError.value = ''
  actionError.value = ''
  successMessage.value = ''
  try {
    const result = await request()
    if (!result.httpOk || result.outcome !== 'success') {
      const message = outcomeMessage(result.outcome)
      await loadStatus()
      if (showDialog.value) dialogError.value = message
      else actionError.value = message
      return false
    }
    if (!await loadStatus()) return false
    if (successKey) successMessage.value = t(`mfa.recoveryEmail.${successKey}`)
    afterSuccess?.()
    return true
  } catch {
    if (showDialog.value) dialogError.value = outcomeMessage('unavailable')
    else actionError.value = outcomeMessage('unavailable')
    return false
  } finally { submitting.value = false }
}
async function beginChange() {
  if (!candidateAddress.value) return
  await mutate(() => accountApi.changeRecoveryEmail(candidateAddress.value, props.csrfToken), 'pendingSuccess', () => {
    candidateAddress.value = ''
    mode.value = 'verify'
    focusDialog()
  })
}
async function verify() {
  if (!verificationCode.value) return
  const succeeded = await mutate(() => accountApi.verifyRecoveryEmail(verificationCode.value, props.csrfToken), 'verifiedSuccess')
  if (succeeded) closeDialog()
}
async function resend() { await mutate(() => accountApi.resendRecoveryEmail(props.csrfToken), 'resentSuccess') }
async function cancelPending() { await mutate(() => accountApi.cancelRecoveryEmailPending(props.csrfToken), 'cancelledSuccess') }
async function revoke() {
  if (selectionStatus.value || !window.confirm(t('mfa.recoveryEmail.removeConfirm'))) return
  await mutate(() => accountApi.revokeRecoveryEmail(props.csrfToken), 'removedSuccess')
}
async function prepareDefault(event) {
  if (submitting.value || !status.enabled || !status.maskedDefaultAddress) return
  const opener = event?.currentTarget || document.activeElement
  submitting.value = true
  actionError.value = ''
  successMessage.value = ''
  try {
    const result = await accountApi.prepareRecoveryEmailDefault(props.csrfToken)
    if (!result.httpOk || !result.maskedAddress || !result.confirmation) { actionError.value = outcomeMessage(result.outcome); return }
    defaultConfirmation.value = { maskedAddress: result.maskedAddress, confirmation: result.confirmation }
    openDialog('default', { currentTarget: opener })
  } catch { actionError.value = outcomeMessage('unavailable') }
  finally { submitting.value = false; if (showDialog.value) focusDialog() }
}
async function useDefault() {
  if (!defaultConfirmation.value.confirmation) return
  const succeeded = await mutate(() => accountApi.useRecoveryEmailDefault(defaultConfirmation.value.confirmation, props.csrfToken), 'defaultSuccess')
  defaultConfirmation.value = {}
  if (succeeded) closeDialog()
  else { actionError.value ||= dialogError.value; closeDialog() }
}
function continueLogin() {
  if (loginUrl.value.startsWith('/Account/Login?')) window.location.assign(loginUrl.value)
}
async function reauthenticate() {
  if (submitting.value || !status.enabled) return
  submitting.value = true
  dialogError.value = ''
  try {
    const result = await accountApi.reauthenticateRecoveryEmail(props.csrfToken)
    if (!result.httpOk || !result.loginUrl?.startsWith('/Account/Login?')) { dialogError.value = outcomeMessage(result.outcome); return }
    loginUrl.value = result.loginUrl
    if (result.hardwareOnly) { mode.value = 'hardware'; focusDialog() }
    else continueLogin()
  } catch { dialogError.value = outcomeMessage('unavailable') }
  finally { submitting.value = false; if (showDialog.value) focusDialog() }
}
onMounted(() => { loadStatus(); clockTimer = window.setInterval(() => { now.value = Date.now() }, 1000) })
onUnmounted(() => window.clearInterval(clockTimer))
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
.recovery-email-actions { margin-top: 12px; display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; }
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
.recovery-pending { margin-top: 16px; padding: 16px; border-left: 3px solid #b06000; background: #fef7e0; font-size: 13px; color: #5f6368; line-height: 1.5; }
.recovery-pending p { margin: 0 0 8px; }
.recovery-email-copy .recovery-warning { margin: 12px 0; color: #8a4b08; }
.recovery-btn:focus-visible, a:focus-visible { outline: 2px solid #174ea6; outline-offset: 3px; }
.recovery-modal .recovery-email-address { overflow-wrap: anywhere; }
</style>
