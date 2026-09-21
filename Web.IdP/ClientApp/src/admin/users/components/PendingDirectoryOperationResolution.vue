<template>
  <div class="pending-resolution">
    <button v-if="state === 'idle'" type="button" class="pending-btn secondary" :disabled="busy || !canInspect" @click="inspectPending">
      {{ t('users.recoveryAssistance.pending.inspect') }}
    </button>

    <p v-else-if="state === 'loading'" class="pending-state" role="status">
      {{ t('users.recoveryAssistance.pending.loading') }}
    </p>

    <div v-else-if="state === 'available' && pendingAttempt">
      <dl class="pending-summary" data-testid="pending-directory-operation">
        <div>
          <dt>{{ t('users.recoveryAssistance.pending.operationKind') }}</dt>
          <dd :data-operation-kind="pendingAttempt.operationKind">{{ operationKindLabel }}</dd>
        </div>
        <div>
          <dt>{{ t('users.recoveryAssistance.pending.status') }}</dt>
          <dd :data-operation-status="pendingAttempt.status">{{ statusLabel }}</dd>
        </div>
      </dl>

      <form v-if="canPrepare" @submit.prevent="prepareSettlement">
        <fieldset class="pending-choice">
          <legend>{{ t('users.recoveryAssistance.pending.dispositionLegend') }}</legend>
          <label>
            <input v-model="disposition" type="radio" value="OriginalOperationSettled" :disabled="busy" />
            <span>{{ t('users.recoveryAssistance.pending.dispositions.OriginalOperationSettled') }}</span>
          </label>
          <label>
            <input v-model="disposition" type="radio" value="ApprovedOutOfBandRecoveryCompleted" :disabled="busy" />
            <span>{{ t('users.recoveryAssistance.pending.dispositions.ApprovedOutOfBandRecoveryCompleted') }}</span>
          </label>
        </fieldset>

        <label class="pending-confirmation">
          <input v-model="originalWritersDrained" type="checkbox" :disabled="busy" />
          <span>{{ t('users.recoveryAssistance.pending.originalWritersDrained') }}</span>
        </label>

        <div class="pending-field">
          <label for="pending-evidence-reference">{{ t('users.recoveryAssistance.pending.evidenceReferenceLabel') }}</label>
          <input
            id="pending-evidence-reference"
            v-model.trim="evidenceReference"
            type="text"
            autocomplete="off"
            maxlength="200"
            pattern="[A-Za-z0-9_:.\/-]+"
            required
            :disabled="busy"
            :placeholder="t('users.recoveryAssistance.pending.evidenceReferencePlaceholder')"
          />
          <small>{{ t('users.recoveryAssistance.pending.evidenceReferenceHelp') }}</small>
        </div>

        <div class="pending-actions">
          <button type="button" class="pending-btn secondary" :disabled="busy" @click="inspectPending">
            {{ t('users.recoveryAssistance.pending.reload') }}
          </button>
          <button type="submit" class="pending-btn primary" :disabled="!canSubmit">
            {{ busy ? t('users.recoveryAssistance.submitting') : t('users.recoveryAssistance.pending.prepare') }}
          </button>
        </div>
      </form>
    </div>

    <div v-else-if="state === 'prepared'" ref="resultState" class="pending-state success" role="status" tabindex="-1">
      <p>{{ t('users.recoveryAssistance.pending.prepared') }}</p>
      <button type="button" class="pending-btn secondary" :disabled="busy" @click="cancelPreparation">
        {{ t('users.recoveryAssistance.pending.cancelPreparation') }}
      </button>
    </div>

    <div v-else class="pending-state" :class="state === 'cancelled' ? 'success' : 'error'" :role="state === 'cancelled' ? 'status' : 'alert'">
      <p>{{ stateMessage }}</p>
      <button v-if="state !== 'cancelled'" type="button" class="pending-btn secondary" :disabled="busy || !canInspect" @click="inspectPending">
        {{ t('users.recoveryAssistance.pending.retry') }}
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  user: { type: Object, required: true },
  fetchWithCsrf: { type: Function, required: true },
  canInspect: { type: Boolean, required: true },
  canPrepare: { type: Boolean, required: true }
})
const emit = defineEmits(['busy-change'])
const { t } = useI18n()

const pendingAttempt = ref(null)
const preparationId = ref(null)
const originalWritersDrained = ref(false)
const disposition = ref('OriginalOperationSettled')
const evidenceReference = ref('')
const busy = ref(false)
const state = ref('idle')
const resultState = ref(null)
let requestGeneration = 0
let disposed = false

const evidenceCategory = computed(() => disposition.value === 'OriginalOperationSettled'
  ? 'ApprovedDirectoryOperation'
  : 'ApprovedRecoveryOperation')
const canSubmit = computed(() => Boolean(
  props.canPrepare &&
  pendingAttempt.value &&
  !busy.value &&
  originalWritersDrained.value &&
  evidenceReference.value &&
  evidenceReference.value.length <= 200 &&
  /^[A-Za-z0-9_:./-]+$/.test(evidenceReference.value)
))
const stateMessage = computed(() => {
  if (state.value === 'cancelled') return t('users.recoveryAssistance.pending.cancelled')
  if (state.value === 'highAssuranceRequired') return t('users.recoveryAssistance.outcomes.highAssuranceRequired')
  return t('users.recoveryAssistance.pending.unavailable')
})
const operationKindLabel = computed(() => t(`users.recoveryAssistance.pending.operationKinds.${pendingAttempt.value?.operationKind}`))
const statusLabel = computed(() => t(`users.recoveryAssistance.pending.statuses.${pendingAttempt.value?.status}`))

function setBusy(value) {
  busy.value = value
  emit('busy-change', value)
}

function clearAttemptState() {
  pendingAttempt.value = null
  preparationId.value = null
  originalWritersDrained.value = false
  disposition.value = 'OriginalOperationSettled'
  evidenceReference.value = ''
}

function isValidAttempt(value) {
  return value &&
    typeof value.attemptId === 'string' && value.attemptId &&
    Number(value.version) > 0 &&
    ['NativeReset', 'AdminTemporaryIssue', 'RequiredChange'].includes(value.operationKind) &&
    ['Reserved', 'ReconciliationRequired'].includes(value.status) &&
    typeof value.directoryObjectId === 'string' && value.directoryObjectId
}

async function inspectPending() {
  const generation = ++requestGeneration
  clearAttemptState()
  state.value = 'loading'
  setBusy(true)
  try {
    const response = await props.fetchWithCsrf(
      `/api/admin/users/${props.user.id}/credential-recovery/pending-directory-operation`,
      { method: 'GET', headers: {} }
    )
    const result = await response.json().catch(() => ({}))
    if (disposed || generation !== requestGeneration) return
    if (props.canInspect && response.ok && result.outcome === 'available' && isValidAttempt(result)) {
      pendingAttempt.value = {
        attemptId: result.attemptId,
        version: result.version,
        operationKind: result.operationKind,
        status: result.status,
        directoryObjectId: result.directoryObjectId
      }
      state.value = 'available'
    } else {
      state.value = result.outcome === 'highAssuranceRequired' ? 'highAssuranceRequired' : 'unavailable'
    }
  } catch {
    if (!disposed && generation === requestGeneration) state.value = 'unavailable'
  } finally {
    if (!disposed && generation === requestGeneration) setBusy(false)
  }
}

async function prepareSettlement() {
  if (!canSubmit.value || !window.confirm(t('users.recoveryAssistance.pending.confirmPrepare'))) return
  const generation = ++requestGeneration
  const attempt = pendingAttempt.value
  const requestBody = {
    attemptId: attempt.attemptId,
    expectedVersion: attempt.version,
    operationKind: attempt.operationKind,
    expectedStatus: attempt.status,
    directoryObjectId: attempt.directoryObjectId,
    originalWritersDrained: originalWritersDrained.value,
    disposition: disposition.value,
    evidenceCategory: evidenceCategory.value,
    evidenceReference: evidenceReference.value
  }
  originalWritersDrained.value = false
  evidenceReference.value = ''
  setBusy(true)
  try {
    const response = await props.fetchWithCsrf(
      `/api/admin/users/${props.user.id}/credential-recovery/prepare-directory-settlement`,
      { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(requestBody) }
    )
    const result = await response.json().catch(() => ({}))
    if (disposed || generation !== requestGeneration) return
    pendingAttempt.value = null
    if (response.ok && result.outcome === 'prepared' && typeof result.preparationId === 'string' && result.preparationId) {
      preparationId.value = result.preparationId
      state.value = 'prepared'
      await nextTick()
      resultState.value?.focus()
    } else {
      preparationId.value = null
      state.value = result.outcome === 'highAssuranceRequired' ? 'highAssuranceRequired' : 'unavailable'
    }
  } catch {
    if (!disposed && generation === requestGeneration) {
      clearAttemptState()
      state.value = 'unavailable'
    }
  } finally {
    if (!disposed && generation === requestGeneration) setBusy(false)
  }
}

async function cancelPreparation() {
  if (!preparationId.value || !window.confirm(t('users.recoveryAssistance.pending.confirmCancel'))) return
  const generation = ++requestGeneration
  const currentPreparationId = preparationId.value
  preparationId.value = null
  setBusy(true)
  try {
    const response = await props.fetchWithCsrf(
      `/api/admin/users/credential-recovery/directory-settlement/${currentPreparationId}/cancel`,
      { method: 'POST', headers: {} }
    )
    const result = await response.json().catch(() => ({}))
    if (disposed || generation !== requestGeneration) return
    state.value = response.ok && result.outcome === 'cancelled'
      ? 'cancelled'
      : result.outcome === 'highAssuranceRequired' ? 'highAssuranceRequired' : 'unavailable'
  } catch {
    if (!disposed && generation === requestGeneration) state.value = 'unavailable'
  } finally {
    if (!disposed && generation === requestGeneration) setBusy(false)
  }
}

function reset() {
  requestGeneration += 1
  clearAttemptState()
  state.value = 'idle'
  setBusy(false)
}

watch(() => [props.user.id, props.canInspect, props.canPrepare], reset)
onBeforeUnmount(() => {
  disposed = true
  requestGeneration += 1
  clearAttemptState()
  emit('busy-change', false)
})
</script>

<style scoped>
.pending-resolution { margin-top: 12px; padding: 14px; border: 1px solid #dadce0; border-radius: 6px; }
.pending-summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1px; overflow: hidden; margin: 0; border: 1px solid #dadce0; border-radius: 4px; background: #dadce0; }
.pending-summary > div { min-width: 0; padding: 10px 12px; background: #f8f9fa; }
.pending-summary dt { color: #5f6368; font-size: 12px; }
.pending-summary dd { margin: 3px 0 0; color: #202124; font-size: 13px; overflow-wrap: anywhere; }
.pending-field { margin-top: 16px; }
.pending-field label, .pending-choice legend { display: block; margin-bottom: 6px; font-size: 14px; font-weight: 500; }
.pending-field input { box-sizing: border-box; width: 100%; padding: 10px 12px; border: 1px solid #dadce0; border-radius: 4px; font: inherit; }
.pending-field input:focus { outline: none; border-color: #1a73e8; box-shadow: 0 0 0 2px rgba(26,115,232,.2); }
.pending-field small { display: block; margin-top: 5px; color: #5f6368; font-size: 12px; line-height: 1.4; }
.pending-choice { display: grid; gap: 10px; margin: 16px 0 0; padding: 0; border: 0; }
.pending-choice label, .pending-confirmation { display: flex; align-items: flex-start; gap: 8px; color: #3c4043; font-size: 13px; line-height: 1.45; }
.pending-confirmation { margin-top: 16px; }
.pending-choice input, .pending-confirmation input { flex: 0 0 auto; width: 18px; height: 18px; margin: 1px 0 0; accent-color: #1a73e8; }
.pending-state { margin: 0; padding: 12px; border-radius: 4px; background: #f8f9fa; color: #5f6368; font-size: 13px; line-height: 1.5; }
.pending-state p { margin: 0 0 10px; }
.pending-state.error { background: #fce8e6; color: #b3261e; }
.pending-state.success { background: #e6f4ea; color: #137333; }
.pending-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 20px; }
.pending-btn { min-height: 40px; padding: 8px 16px; border: 1px solid #dadce0; border-radius: 4px; font-size: 14px; font-weight: 500; cursor: pointer; }
.pending-btn.primary { border-color: #1a73e8; background: #1a73e8; color: white; }
.pending-btn.secondary { background: white; color: #5f6368; }
.pending-btn:focus-visible { outline: 2px solid #1a73e8; outline-offset: 2px; }
.pending-btn:disabled { opacity: .6; cursor: not-allowed; }
@media (max-width: 560px) {
  .pending-summary { grid-template-columns: 1fr; }
  .pending-actions .pending-btn { flex: 1; }
}
</style>
