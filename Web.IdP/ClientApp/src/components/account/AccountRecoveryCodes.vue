<template>
  <section v-if="!disclosureOnly" class="account-recovery-codes" aria-labelledby="account-recovery-codes-title" :aria-busy="loading">
    <h3 id="account-recovery-codes-title">{{ t('mfa.accountRecoveryCodes.title') }}</h3>
    <p>{{ t('mfa.accountRecoveryCodes.description') }}</p>
    <p>{{ t(status.legacyRemainingCodes > 0 ? 'mfa.accountRecoveryCodes.legacyCompatibility' : 'mfa.accountRecoveryCodes.independent') }}</p>
    <p v-if="loading" role="status">{{ t('mfa.accountRecoveryCodes.loading') }}</p>
    <p v-else-if="statusError" class="error-message" role="alert">{{ statusError }}</p>
    <p v-else role="status" data-testid="account-recovery-status">
      {{ !status.hasBatch ? t('mfa.accountRecoveryCodes.neverGenerated') : status.totalRemainingCodes === 0 ? t('mfa.accountRecoveryCodes.exhausted') : t('mfa.accountRecoveryCodes.remaining', { count: status.totalRemainingCodes }) }}
    </p>
    <p class="help-text">{{ t('mfa.accountRecoveryCodes.adminHelp') }}</p>
    <button v-if="statusError" type="button" class="btn-secondary" :disabled="loading" @click="loadStatus">{{ t('mfa.accountRecoveryCodes.retry') }}</button>
    <button v-else type="button" class="btn-secondary" data-testid="account-recovery-generate" :disabled="loading || submitting || showDialog || busy" @click="requestReauthentication">
      {{ t(status.hasBatch ? 'mfa.accountRecoveryCodes.replace' : 'mfa.accountRecoveryCodes.generate') }}
    </button>
  </section>
    <div v-if="showDialog" class="modal-overlay" @click.self="closeDialog">
      <div ref="dialog" class="modal-content" role="dialog" aria-modal="true" aria-labelledby="account-recovery-dialog-title" aria-describedby="account-recovery-dialog-description" :aria-busy="submitting" @keydown="dialogKeydown">
        <h2 id="account-recovery-dialog-title">{{ t(codes.length ? 'mfa.accountRecoveryCodes.saveTitle' : 'mfa.accountRecoveryCodes.title') }}</h2>
        <p id="account-recovery-dialog-description">{{ t(codes.length ? 'mfa.accountRecoveryCodes.saveWarning' : status.hasBatch ? 'mfa.accountRecoveryCodes.replaceWarning' : 'mfa.accountRecoveryCodes.generateWarning') }}</p>
        <template v-if="codes.length">
          <div class="recovery-codes-grid" data-testid="account-recovery-disclosure">
            <code v-for="code in codes" :key="code" class="recovery-code">{{ code }}</code>
          </div>
          <div class="copy-actions">
            <button type="button" class="btn-secondary" data-testid="account-recovery-copy" :disabled="copying" @click="copyCodes">{{ t(copying ? 'mfa.accountRecoveryCodes.copying' : 'mfa.accountRecoveryCodes.copyAll') }}</button>
            <p v-if="copyFeedback" role="status" aria-live="polite" data-testid="account-recovery-copy-status">{{ copyFeedback }}</p>
          </div>
          <label class="saved-acknowledgement">
            <input v-model="saved" type="checkbox" data-testid="account-recovery-saved" />
            {{ t('mfa.accountRecoveryCodes.savedAcknowledgement') }}
          </label>
          <div class="modal-actions">
            <button type="button" class="btn-primary" data-testid="account-recovery-done" :disabled="!saved" @click="closeDialog">{{ t('mfa.done') }}</button>
          </div>
        </template>
        <template v-else>
          <p v-if="generationError" class="error-message" role="alert">{{ generationError }}</p>
          <div class="modal-actions">
            <button type="button" class="btn-secondary" :disabled="submitting" data-testid="account-recovery-cancel" @click="closeDialog">{{ t('common.cancel') }}</button>
            <button type="button" class="btn-primary" :disabled="submitting" data-testid="account-recovery-confirm" @click="generate">
              {{ t(submitting ? 'mfa.accountRecoveryCodes.generating' : status.hasBatch ? 'mfa.accountRecoveryCodes.replace' : 'mfa.accountRecoveryCodes.generate') }}
            </button>
          </div>
        </template>
      </div>
    </div>
</template>

<script setup>
import { nextTick, onMounted, onUnmounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { accountRecoveryCodesApi } from '../../services/accountRecoveryCodes';

const props = defineProps({ csrfToken: { type: String, default: '' }, busy: { type: Boolean, default: false }, disclosureOnly: { type: Boolean, default: false } });
const emit = defineEmits(['reauthenticate', 'saved', 'status-changed']);
const { t } = useI18n();
const status = ref({ hasBatch: false, remainingCodes: 0, legacyRemainingCodes: 0, totalRemainingCodes: 0 });
const loading = ref(true);
const statusError = ref('');
const generationError = ref('');
const submitting = ref(false);
const showDialog = ref(false);
const codes = ref([]);
const saved = ref(false);
const copying = ref(false);
const copyFeedback = ref('');
const dialog = ref(null);
let returnFocus;
let unmounted = false;

async function loadStatus() {
  loading.value = true;
  statusError.value = '';
  try {
    const result = await accountRecoveryCodesApi.getStatus();
    if (unmounted) return;
    const legacyRemainingCodes = result.legacyRemainingCodes ?? 0;
    const totalRemainingCodes = result.totalRemainingCodes ?? result.remainingCodes + legacyRemainingCodes;
    if (!result.httpOk || typeof result.hasBatch !== 'boolean' ||
        [result.remainingCodes, legacyRemainingCodes, totalRemainingCodes].some(count => !Number.isInteger(count) || count < 0)) {
      statusError.value = t('mfa.accountRecoveryCodes.statusFailed');
      return;
    }
    status.value = { hasBatch: result.hasBatch, remainingCodes: result.remainingCodes, legacyRemainingCodes, totalRemainingCodes };
    emit('status-changed', status.value);
  } catch { if (!unmounted) statusError.value = t('mfa.accountRecoveryCodes.statusFailed'); }
  finally { loading.value = false; }
}

function requestReauthentication() {
  if (loading.value || submitting.value || showDialog.value || props.busy) return;
  emit('reauthenticate');
}

async function focusDialog() {
  await nextTick();
  dialog.value?.querySelector('input, button:not(:disabled)')?.focus();
}

function resumeGeneration() {
  if (unmounted || submitting.value || codes.value.length) return;
  returnFocus = document.activeElement;
  generationError.value = '';
  showDialog.value = true;
  focusDialog();
}

function displayIssuedCodes(issuedCodes) {
  if (unmounted || !Array.isArray(issuedCodes) || !issuedCodes.length || !issuedCodes.every(code => typeof code === 'string' && code.length > 0)) return;
  returnFocus = document.activeElement;
  codes.value = [...issuedCodes];
  saved.value = false;
  copying.value = false;
  copyFeedback.value = '';
  generationError.value = '';
  showDialog.value = true;
  status.value = { hasBatch: true, remainingCodes: codes.value.length, legacyRemainingCodes: 0, totalRemainingCodes: codes.value.length };
  emit('status-changed', status.value);
  focusDialog();
}

function closeDialog() {
  if (submitting.value || (codes.value.length && !saved.value)) return;
  const disclosedCodes = codes.value.length > 0;
  codes.value = [];
  saved.value = false;
  copying.value = false;
  copyFeedback.value = '';
  showDialog.value = false;
  generationError.value = '';
  if (disclosedCodes) emit('saved');
  nextTick(() => returnFocus?.isConnected && returnFocus.focus());
}

async function copyCodes() {
  if (unmounted || !showDialog.value || !codes.value.length || copying.value) return;
  const disclosedCodes = codes.value;
  copying.value = true;
  copyFeedback.value = '';
  try {
    await navigator.clipboard.writeText(disclosedCodes.join('\n'));
    if (!unmounted && showDialog.value && codes.value === disclosedCodes) copyFeedback.value = t('mfa.accountRecoveryCodes.copySuccess');
  } catch {
    if (!unmounted && showDialog.value && codes.value === disclosedCodes) copyFeedback.value = t('mfa.accountRecoveryCodes.copyFailed');
  } finally {
    if (!unmounted && showDialog.value && codes.value === disclosedCodes) copying.value = false;
  }
}

async function generate() {
  if (submitting.value || !showDialog.value || codes.value.length) return;
  submitting.value = true;
  generationError.value = '';
  try {
    const result = await accountRecoveryCodesApi.generate(props.csrfToken);
    if (unmounted) return;
    if (!result.httpOk) {
      if (result.httpStatus === 403 && result.error === 'freshAuthenticationRequired') {
        showDialog.value = false;
        emit('reauthenticate');
      } else generationError.value = t('mfa.accountRecoveryCodes.generateFailed');
      return;
    }
    if (!Array.isArray(result.recoveryCodes) || !result.recoveryCodes.length || !result.recoveryCodes.every(code => typeof code === 'string' && code.length > 0)) {
      generationError.value = t('mfa.accountRecoveryCodes.generateFailed');
      return;
    }
    displayIssuedCodes(result.recoveryCodes);
    await loadStatus();
  } catch { if (!unmounted) generationError.value = t('mfa.accountRecoveryCodes.generateFailed'); }
  finally { submitting.value = false; }
}

function dialogKeydown(event) {
  if (event.key === 'Escape') { event.preventDefault(); closeDialog(); return; }
  if (event.key !== 'Tab') return;
  const controls = [...dialog.value.querySelectorAll('input:not(:disabled), button:not(:disabled)')];
  const first = controls[0], last = controls[controls.length - 1];
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
}

onMounted(() => { if (!props.disclosureOnly) loadStatus(); });
onUnmounted(() => { unmounted = true; codes.value = []; saved.value = false; copying.value = false; copyFeedback.value = ''; });
defineExpose({ loadStatus, resumeGeneration, displayIssuedCodes });
</script>

<style scoped>
.account-recovery-codes { margin-top: 24px; padding: 20px; border: 1px solid #dadce0; border-radius: 8px; }
h3 { margin: 0 0 8px; font-size: 16px; font-weight: 500; color: #202124; }
p { margin: 0 0 12px; font-size: 14px; line-height: 1.6; color: #5f6368; }
.help-text { font-size: 13px; }
.error-message { color: #b91c1c; }
button { min-height: 40px; padding: 8px 16px; border-radius: 4px; font-size: 14px; cursor: pointer; }
.btn-secondary { background: white; color: #1a73e8; border: 1px solid #dadce0; }
.btn-primary { background: #1a73e8; color: white; border: 1px solid #1a73e8; }
button:disabled { opacity: .6; cursor: not-allowed; }
button:focus-visible, input:focus-visible { outline: 2px solid #1a73e8; outline-offset: 3px; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0, 0, 0, .5); display: flex; align-items: center; justify-content: center; padding: 16px; z-index: 1000; }
.modal-content { width: 100%; max-width: 500px; max-height: 90vh; overflow-y: auto; background: white; padding: 24px; border-radius: 8px; }
h2 { margin: 0 0 16px; font-size: 18px; font-weight: 500; color: #202124; }
.modal-actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 12px; margin-top: 24px; }
.recovery-codes-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; margin: 20px 0; }
.recovery-code { padding: 8px; background: #f1f3f4; border-radius: 4px; font-size: 14px; text-align: center; overflow-wrap: anywhere; }
.copy-actions { margin-bottom: 16px; }
.copy-actions p { margin: 8px 0 0; }
.saved-acknowledgement { display: flex; align-items: flex-start; gap: 8px; font-size: 14px; line-height: 1.6; color: #202124; }
.saved-acknowledgement input { margin-top: 4px; }
@media (max-width: 480px) { .account-recovery-codes { padding: 16px; } .modal-content { padding: 20px; } .modal-actions button { flex: 1; } }
</style>
