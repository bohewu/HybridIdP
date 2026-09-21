<template>
  <div class="recovery-assistance-overlay" @click.self="close" @keydown.esc="close">
    <section
      ref="dialog"
      class="recovery-assistance-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="recovery-assistance-title"
      :aria-busy="busy"
      tabindex="-1"
    >
      <header>
        <h2 id="recovery-assistance-title">{{ t('users.recoveryAssistance.title') }}</h2>
        <p>{{ t('users.recoveryAssistance.target', { name: targetName }) }}</p>
      </header>

      <p v-if="contextLoading" class="assistance-state" role="status">
        {{ t('users.recoveryAssistance.loading') }}
      </p>
      <p v-else-if="contextOutcome !== 'available'" class="assistance-message error" role="alert">
        {{ contextOutcome === 'highAssuranceRequired'
          ? t('users.recoveryAssistance.outcomes.highAssuranceRequired')
          : t('users.recoveryAssistance.contextUnavailable') }}
      </p>

      <div class="assistance-groups" role="tablist" :aria-label="t('users.recoveryAssistance.groupLabel')">
        <button
          v-for="(group, index) in groups"
          :key="group"
          :ref="element => setGroupButton(element, index)"
          type="button"
          role="tab"
          :id="`recovery-group-${group}`"
          :aria-selected="selectedGroup === group"
          :aria-controls="`recovery-panel-${group}`"
          :tabindex="selectedGroup === group ? 0 : -1"
          :class="{ selected: selectedGroup === group }"
          :disabled="busy"
          :data-testid="`recovery-group-${group}`"
          @click="selectGroup(group)"
          @keydown="handleGroupKeydown($event, index)"
        >
          {{ t(`users.recoveryAssistance.groups.${group}.title`) }}
        </button>
      </div>

      <div
        :id="`recovery-panel-${selectedGroup}`"
        class="assistance-panel"
        role="tabpanel"
        :aria-labelledby="`recovery-group-${selectedGroup}`"
      >
        <h3>{{ t(`users.recoveryAssistance.groups.${selectedGroup}.title`) }}</h3>
        <p class="assistance-description">{{ t(`users.recoveryAssistance.groups.${selectedGroup}.description`) }}</p>

        <template v-if="selectedGroup === 'ordinary'">
          <section class="assistance-family">
            <div class="assistance-family-heading">
              <h4>{{ t('users.recoveryAssistance.families.ordinary') }}</h4>
              <span :class="['assistance-status', ordinary.state]">{{ t(`users.recoveryAssistance.states.${ordinary.state}`) }}</span>
            </div>
            <div v-if="ordinaryActions.length" class="assistance-action-list">
              <button
                v-for="item in ordinaryActions"
                :key="item.id"
                type="button"
                class="assistance-action"
                :class="{ selected: selectedAction === item.id }"
                :aria-pressed="selectedAction === item.id"
                :disabled="busy"
                :data-action="item.id"
                @click="selectAction(item.id)"
              >
                {{ t(`users.recoveryAssistance.actions.${item.id}`) }}
              </button>
            </div>
            <p v-else class="assistance-empty">{{ t('users.recoveryAssistance.noAvailableActions') }}</p>
          </section>
          <section class="assistance-family">
            <div class="assistance-family-heading">
              <h4>{{ t('users.recoveryAssistance.families.temporary') }}</h4>
              <span :class="['assistance-status', temporary.state]">{{ t(`users.recoveryAssistance.states.${temporary.state}`) }}</span>
            </div>
            <div v-if="temporaryActions.length" class="assistance-action-list">
              <button
                v-for="item in temporaryActions"
                :key="item.id"
                type="button"
                class="assistance-action"
                :class="{ selected: selectedAction === item.id }"
                :aria-pressed="selectedAction === item.id"
                :disabled="busy"
                :data-action="item.id"
                @click="selectAction(item.id)"
              >
                {{ t(`users.recoveryAssistance.actions.${item.id}`) }}
              </button>
            </div>
            <p v-else class="assistance-empty">{{ t('users.recoveryAssistance.noAvailableActions') }}</p>
          </section>
        </template>

        <section v-else-if="selectedGroup === 'migration'" class="assistance-family">
          <div class="assistance-family-heading">
            <h4>{{ t('users.recoveryAssistance.families.migration') }}</h4>
            <span :class="['assistance-status', migration.state]">{{ t(`users.recoveryAssistance.states.${migration.state}`) }}</span>
          </div>
          <div v-if="migrationActions.length" class="assistance-action-list">
            <button
              v-for="item in migrationActions"
              :key="item.id"
              type="button"
              class="assistance-action"
              :class="{ selected: selectedAction === item.id }"
              :aria-pressed="selectedAction === item.id"
              :disabled="busy"
              :data-action="item.id"
              @click="selectAction(item.id)"
            >
              {{ t(`users.recoveryAssistance.actions.${item.id}`) }}
            </button>
          </div>
          <p v-else class="assistance-empty">{{ t('users.recoveryAssistance.noAvailableActions') }}</p>
        </section>

        <template v-else>
          <div class="assistance-family-heading">
            <h4>{{ t('users.recoveryAssistance.families.pending') }}</h4>
            <span :class="['assistance-status', pending.state]">
              {{ t(`users.recoveryAssistance.states.${pending.state}`) }}
            </span>
          </div>
          <PendingDirectoryOperationResolution
            v-if="pending.inspect"
            :user="user"
            :fetch-with-csrf="fetchWithCsrf"
            :can-inspect="pending.inspect"
            :can-prepare="pending.prepareSettlement"
            @busy-change="resolutionBusy = $event"
          />
          <p v-else class="assistance-empty">{{ t('users.recoveryAssistance.noAvailableActions') }}</p>
        </template>

        <p v-if="message" :class="['assistance-message', messageKind]" :role="messageKind === 'error' ? 'alert' : 'status'">
          {{ message }}
        </p>
        <div v-if="temporaryPassword" class="assistance-field temporary-credential" role="status">
          <label for="assistance-temporary-password">{{ t('users.recoveryAssistance.temporaryPasswordLabel') }}</label>
          <input id="assistance-temporary-password" :value="temporaryPassword" type="text" readonly autocomplete="off" />
          <small>{{ t('users.recoveryAssistance.temporaryPasswordHelp') }}</small>
        </div>

        <form v-if="activeAction" @submit.prevent="submit">
          <p class="assistance-description">{{ t(`users.recoveryAssistance.descriptions.${activeAction.id}`) }}</p>

          <div v-if="activeAction.requiresAddress" class="assistance-field">
            <label for="assistance-address">{{ t('users.recoveryAssistance.addressLabel') }}</label>
            <input id="assistance-address" v-model.trim="candidateAddress" type="email" autocomplete="off" required :disabled="submitting" />
            <small>{{ t('users.recoveryAssistance.addressHelp') }}</small>
          </div>

          <template v-if="activeAction.requiresProof">
            <div class="assistance-field">
              <label for="assistance-evidence">{{ t('users.recoveryAssistance.evidenceLabel') }}</label>
              <textarea id="assistance-evidence" v-model.trim="identityCheckEvidence" rows="3" maxlength="500" required :disabled="submitting"></textarea>
              <small>{{ t('users.recoveryAssistance.evidenceHelp') }}</small>
            </div>
            <div class="assistance-field">
              <label for="assistance-reason">{{ t('users.recoveryAssistance.reasonLabel') }}</label>
              <textarea id="assistance-reason" v-model.trim="reason" rows="2" maxlength="500" required :disabled="submitting"></textarea>
            </div>
          </template>

          <div class="assistance-actions">
            <button type="button" class="assistance-btn secondary" :disabled="submitting" @click="clearSelectedAction">
              {{ t('users.recoveryAssistance.backToActions') }}
            </button>
            <button type="submit" class="assistance-btn primary" :disabled="!canSubmit">
              {{ submitLabel }}
            </button>
          </div>
        </form>
      </div>

      <div class="dialog-actions">
        <button v-if="contextOutcome !== 'available' && !contextLoading" type="button" class="assistance-btn secondary" :disabled="busy" @click="loadContext">
          {{ t('users.recoveryAssistance.retryContext') }}
        </button>
        <button type="button" class="assistance-btn secondary" :disabled="busy" @click="close">
          {{ t('common.cancel') }}
        </button>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PendingDirectoryOperationResolution from './PendingDirectoryOperationResolution.vue'

const props = defineProps({
  user: { type: Object, required: true },
  fetchWithCsrf: { type: Function, required: true }
})
const emit = defineEmits(['close'])
const { t } = useI18n()

const groups = ['ordinary', 'migration', 'pending']
const dialog = ref(null)
const groupButtons = []
const selectedGroup = ref('ordinary')
const selectedAction = ref(null)
const context = ref(null)
const contextLoading = ref(true)
const contextOutcome = ref('unknown')
const candidateAddress = ref('')
const identityCheckEvidence = ref('')
const reason = ref('')
const temporaryPassword = ref('')
const submitting = ref(false)
const resolutionBusy = ref(false)
const message = ref('')
const messageKind = ref('error')
const cooldown = ref(0)
let cooldownTimer = null
let requestGeneration = 0
let disposed = false

const unknownFamily = Object.freeze({ state: 'unknown' })
const ordinary = computed(() => context.value?.ordinary || unknownFamily)
const temporary = computed(() => context.value?.temporary || unknownFamily)
const migration = computed(() => context.value?.migration || unknownFamily)
const pending = computed(() => context.value?.pending || unknownFamily)
const targetName = computed(() => props.user.userName || props.user.email || t('users.recoveryAssistance.targetUnknown'))
const busy = computed(() => contextLoading.value || submitting.value || resolutionBusy.value)

const actionDefinitions = {
  'ordinary-resend': { id: 'ordinary-resend', family: 'ordinary', routeFamily: 'password-recovery', route: 'resend-otp', requiresAddress: false, requiresProof: false },
  'ordinary-replace': { id: 'ordinary-replace', family: 'ordinary', routeFamily: 'password-recovery', route: 'replace-email', requiresAddress: true, requiresProof: true },
  'ordinary-approve': { id: 'ordinary-approve', family: 'ordinary', routeFamily: 'password-recovery', route: 'approve-reset', requiresAddress: false, requiresProof: true },
  temporary: { id: 'temporary', family: 'ordinary', routeFamily: 'credential-recovery', route: 'issue-temporary-password', requiresAddress: false, requiresProof: true },
  'migration-resend': { id: 'migration-resend', family: 'migration', routeFamily: 'credential-recovery', route: 'resend-otp', requiresAddress: false, requiresProof: false },
  'migration-replace': { id: 'migration-replace', family: 'migration', routeFamily: 'credential-recovery', route: 'replace-email', requiresAddress: true, requiresProof: true },
  'migration-approve': { id: 'migration-approve', family: 'migration', routeFamily: 'credential-recovery', route: 'approve-reset', requiresAddress: false, requiresProof: true }
}

const ordinaryActions = computed(() => [
  ordinary.value.resendOtp && actionDefinitions['ordinary-resend'],
  ordinary.value.replaceRecoveryEmail && actionDefinitions['ordinary-replace'],
  ordinary.value.approveReset && actionDefinitions['ordinary-approve']
].filter(Boolean))
const temporaryActions = computed(() => temporary.value.issueTemporaryCredential ? [actionDefinitions.temporary] : [])
const migrationActions = computed(() => [
  migration.value.resendOtp && actionDefinitions['migration-resend'],
  migration.value.replaceRecoveryEmail && actionDefinitions['migration-replace'],
  migration.value.approveReset && actionDefinitions['migration-approve']
].filter(Boolean))
const activeAction = computed(() => actionDefinitions[selectedAction.value] || null)

const canSubmit = computed(() => {
  if (!activeAction.value || busy.value || (activeAction.value.route === 'resend-otp' && cooldown.value > 0)) return false
  if (activeAction.value.requiresAddress && !candidateAddress.value) return false
  if (activeAction.value.requiresProof && (!identityCheckEvidence.value || !reason.value)) return false
  return true
})
const submitLabel = computed(() => {
  if (submitting.value) return t('users.recoveryAssistance.submitting')
  if (activeAction.value?.route === 'resend-otp' && cooldown.value > 0) {
    return t('users.recoveryAssistance.resendIn', { seconds: cooldown.value })
  }
  return activeAction.value ? t(`users.recoveryAssistance.submit.${activeAction.value.id}`) : ''
})

function normalizeState(value) {
  return ['available', 'disabled', 'unavailable', 'confirmedFailure', 'unknown'].includes(value) ? value : 'unknown'
}

function normalizeContext(value) {
  if (!value || typeof value !== 'object') return null
  return {
    ordinary: {
      state: normalizeState(value.ordinary?.state),
      resendOtp: value.ordinary?.resendOtp === true,
      replaceRecoveryEmail: value.ordinary?.replaceRecoveryEmail === true,
      approveReset: value.ordinary?.approveReset === true
    },
    temporary: {
      state: normalizeState(value.temporary?.state),
      issueTemporaryCredential: value.temporary?.issueTemporaryCredential === true
    },
    migration: {
      state: normalizeState(value.migration?.state),
      resendOtp: value.migration?.resendOtp === true,
      replaceRecoveryEmail: value.migration?.replaceRecoveryEmail === true,
      approveReset: value.migration?.approveReset === true
    },
    pending: {
      state: normalizeState(value.pending?.state),
      inspect: value.pending?.inspect === true,
      prepareSettlement: value.pending?.prepareSettlement === true
    }
  }
}

function stopCooldown() {
  cooldown.value = 0
  if (cooldownTimer) window.clearInterval(cooldownTimer)
  cooldownTimer = null
}

function clearActionState() {
  candidateAddress.value = ''
  identityCheckEvidence.value = ''
  reason.value = ''
  temporaryPassword.value = ''
  message.value = ''
  messageKind.value = 'error'
  stopCooldown()
}

function setGroupButton(element, index) {
  groupButtons[index] = element
}

function selectGroup(group, focus = false) {
  if (!groups.includes(group) || group === selectedGroup.value) return
  requestGeneration += 1
  clearActionState()
  selectedAction.value = null
  resolutionBusy.value = false
  selectedGroup.value = group
  if (focus) nextTick(() => groupButtons[groups.indexOf(group)]?.focus())
}

function handleGroupKeydown(event, index) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  let nextIndex = index
  if (event.key === 'Home') nextIndex = 0
  else if (event.key === 'End') nextIndex = groups.length - 1
  else nextIndex = (index + (['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1) + groups.length) % groups.length
  selectGroup(groups[nextIndex], true)
}

function selectAction(actionId) {
  if (!actionDefinitions[actionId] || actionDefinitions[actionId].family !== selectedGroup.value) return
  requestGeneration += 1
  clearActionState()
  selectedAction.value = actionId
}

function clearSelectedAction() {
  requestGeneration += 1
  clearActionState()
  selectedAction.value = null
}

function close() {
  if (busy.value) return
  requestGeneration += 1
  clearActionState()
  selectedAction.value = null
  context.value = null
  emit('close')
}

function startCooldown(seconds) {
  stopCooldown()
  cooldown.value = Math.max(0, Number(seconds) || 0)
  if (!cooldown.value) return
  cooldownTimer = window.setInterval(() => {
    cooldown.value -= 1
    if (cooldown.value <= 0) stopCooldown()
  }, 1000)
}

function outcomeMessage(outcome) {
  return outcome === 'highAssuranceRequired'
    ? t('users.recoveryAssistance.outcomes.highAssuranceRequired')
    : t('users.recoveryAssistance.outcomes.unavailable')
}

async function loadContext() {
  const generation = ++requestGeneration
  clearActionState()
  selectedAction.value = null
  submitting.value = false
  resolutionBusy.value = false
  context.value = null
  contextOutcome.value = 'unknown'
  contextLoading.value = true
  try {
    const response = await props.fetchWithCsrf(
      `/api/admin/users/${props.user.id}/credential-recovery/context`,
      { method: 'GET', headers: {} }
    )
    const result = await response.json().catch(() => ({}))
    if (disposed || generation !== requestGeneration) return
    const normalized = response.ok ? normalizeContext(result) : null
    if (normalized) {
      context.value = normalized
      contextOutcome.value = 'available'
    } else {
      contextOutcome.value = result.outcome === 'highAssuranceRequired' ? 'highAssuranceRequired' : 'unknown'
    }
  } catch {
    if (!disposed && generation === requestGeneration) contextOutcome.value = 'unknown'
  } finally {
    if (!disposed && generation === requestGeneration) contextLoading.value = false
  }
}

async function submit() {
  if (!canSubmit.value || !activeAction.value) return
  const action = activeAction.value
  if (!window.confirm(t(`users.recoveryAssistance.confirmations.${action.id}`))) return

  const generation = ++requestGeneration
  submitting.value = true
  message.value = ''
  temporaryPassword.value = ''
  const body = action.requiresProof
    ? {
        identityCheckEvidence: identityCheckEvidence.value,
        reason: reason.value,
        ...(action.requiresAddress ? { candidateAddress: candidateAddress.value } : {})
      }
    : null
  try {
    const response = await props.fetchWithCsrf(
      `/api/admin/users/${props.user.id}/${action.routeFamily}/${action.route}`,
      {
        method: 'POST',
        headers: body ? { 'Content-Type': 'application/json' } : {},
        ...(body ? { body: JSON.stringify(body) } : {})
      }
    )
    const result = await response.json().catch(() => ({}))
    if (disposed || generation !== requestGeneration) return

    if (!response.ok || result.outcome !== 'success') {
      if (result.outcome === 'cooldown') {
        startCooldown(result.retryAfterSeconds)
        message.value = t('users.recoveryAssistance.outcomes.cooldown', { seconds: cooldown.value })
      } else {
        message.value = outcomeMessage(result.outcome)
      }
      messageKind.value = 'error'
      return
    }

    message.value = t(`users.recoveryAssistance.outcomes.${action.id}Success`)
    messageKind.value = 'success'
    temporaryPassword.value = action.id === 'temporary' ? result.temporaryPassword || '' : ''
    candidateAddress.value = ''
    identityCheckEvidence.value = ''
    reason.value = ''
    if (action.route === 'resend-otp') startCooldown(result.retryAfterSeconds)
  } catch {
    if (!disposed && generation === requestGeneration) {
      message.value = t('users.recoveryAssistance.outcomes.unavailable')
      messageKind.value = 'error'
    }
  } finally {
    if (!disposed && generation === requestGeneration) submitting.value = false
  }
}

onMounted(() => {
  dialog.value?.focus()
  loadContext()
})
watch(() => props.user.id, () => {
  selectedGroup.value = 'ordinary'
  loadContext()
})
onBeforeUnmount(() => {
  disposed = true
  requestGeneration += 1
  clearActionState()
  context.value = null
  resolutionBusy.value = false
})
</script>

<style scoped>
.recovery-assistance-overlay { position: fixed; inset: 0; z-index: 1000; display: flex; align-items: center; justify-content: center; padding: 16px; background: rgba(0,0,0,.5); }
.recovery-assistance-dialog { width: min(680px, 100%); max-height: 92vh; overflow-y: auto; padding: 24px; border-radius: 8px; background: white; color: #202124; }
.recovery-assistance-dialog:focus { outline: none; }
header h2 { margin: 0 0 4px; font-size: 18px; font-weight: 500; }
header p { margin: 0; color: #5f6368; font-size: 13px; overflow-wrap: anywhere; }
.assistance-state { margin: 20px 0 0; padding: 12px; border-radius: 4px; background: #f8f9fa; color: #5f6368; font-size: 13px; }
.assistance-groups { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0; margin-top: 20px; border: 1px solid #dadce0; border-radius: 4px; overflow: hidden; }
.assistance-groups button { min-height: 48px; padding: 8px 10px; border: 0; border-right: 1px solid #dadce0; background: white; color: #3c4043; font-size: 13px; line-height: 1.35; cursor: pointer; }
.assistance-groups button:last-child { border-right: 0; }
.assistance-groups button.selected { background: #e8f0fe; color: #174ea6; font-weight: 500; }
.assistance-groups button:focus-visible, .assistance-action:focus-visible, .assistance-btn:focus-visible { position: relative; z-index: 1; outline: 2px solid #1a73e8; outline-offset: -2px; }
.assistance-groups button:disabled { cursor: not-allowed; opacity: .6; }
.assistance-panel { margin-top: 18px; }
.assistance-panel h3 { margin: 0; font-size: 16px; font-weight: 600; }
.assistance-description { margin: 6px 0 16px; color: #5f6368; font-size: 13px; line-height: 1.5; }
.assistance-family { margin-top: 12px; padding: 14px; border: 1px solid #dadce0; border-radius: 6px; }
.assistance-family-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.assistance-family-heading h4 { margin: 0; font-size: 14px; font-weight: 500; }
.assistance-status { flex: 0 0 auto; padding: 3px 8px; border-radius: 999px; background: #f1f3f4; color: #5f6368; font-size: 12px; }
.assistance-status.available { background: #e6f4ea; color: #137333; }
.assistance-status.disabled { background: #fef7e0; color: #8a4b00; }
.assistance-status.confirmedFailure { background: #fce8e6; color: #b3261e; }
.assistance-action-list { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.assistance-action { min-height: 40px; padding: 8px 12px; border: 1px solid #dadce0; border-radius: 4px; background: white; color: #174ea6; font-size: 13px; font-weight: 500; cursor: pointer; }
.assistance-action.selected { border-color: #1a73e8; background: #e8f0fe; }
.assistance-action:disabled { cursor: not-allowed; opacity: .6; }
.assistance-empty { margin: 10px 0 0; color: #5f6368; font-size: 13px; }
.assistance-field { margin-top: 16px; }
.assistance-field label { display: block; margin-bottom: 6px; font-size: 14px; font-weight: 500; }
.assistance-field input, .assistance-field textarea { box-sizing: border-box; width: 100%; padding: 10px 12px; border: 1px solid #dadce0; border-radius: 4px; font: inherit; resize: vertical; }
.assistance-field input:focus, .assistance-field textarea:focus { outline: none; border-color: #1a73e8; box-shadow: 0 0 0 2px rgba(26,115,232,.2); }
.assistance-field small { display: block; margin-top: 5px; color: #5f6368; font-size: 12px; line-height: 1.4; }
.temporary-credential { padding: 12px; border: 1px solid #137333; border-radius: 4px; background: #e6f4ea; }
.assistance-message { margin-top: 16px; padding: 10px 12px; border-radius: 4px; font-size: 13px; line-height: 1.45; }
.assistance-message.error { background: #fce8e6; color: #b3261e; }
.assistance-message.success { background: #e6f4ea; color: #137333; }
.assistance-actions, .dialog-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; margin-top: 20px; }
.dialog-actions { padding-top: 16px; border-top: 1px solid #e8eaed; }
.assistance-btn { min-height: 40px; padding: 8px 16px; border-radius: 4px; border: 1px solid #dadce0; font-size: 14px; font-weight: 500; cursor: pointer; }
.assistance-btn.primary { border-color: #1a73e8; background: #1a73e8; color: white; }
.assistance-btn.secondary { background: white; color: #5f6368; }
.assistance-btn:disabled { opacity: .6; cursor: not-allowed; }
@media (max-width: 560px) {
  .recovery-assistance-dialog { padding: 20px 16px; }
  .assistance-groups { grid-template-columns: 1fr; }
  .assistance-groups button { border-right: 0; border-bottom: 1px solid #dadce0; }
  .assistance-groups button:last-child { border-bottom: 0; }
  .assistance-action-list { display: grid; }
  .assistance-actions .assistance-btn, .dialog-actions .assistance-btn { flex: 1; }
}
</style>
