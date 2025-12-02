<template>
  <div v-if="show" class="modal-overlay" @click.self="$emit('cancel')">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header">
          <h5>{{ t('myAccount.passwordRequired') }}</h5>
          <button type="button" class="btn-close" @click="$emit('cancel')"></button>
        </div>
        <div class="modal-body">
          <p>{{ t('myAccount.passwordRequiredDesc', { role: roleName }) }}</p>
          <div class="form-group">
            <label for="password">{{ t('myAccount.password') }}</label>
            <input
              id="password"
              v-model="password"
              type="password"
              class="form-control"
              :placeholder="t('myAccount.enterPassword')"
              @keyup.enter="handleConfirm"
              ref="passwordInput"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="$emit('cancel')">
            {{ t('common.cancel') }}
          </button>
          <button 
            type="button" 
            class="btn btn-primary" 
            :disabled="!password"
            @click="handleConfirm"
          >
            {{ t('common.confirm') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick } from 'vue';
import { useI18n } from 'vue-i18n';

const props = defineProps({
  show: Boolean,
  roleName: String
});

const emit = defineEmits(['confirm', 'cancel']);

const { t } = useI18n();
const password = ref('');
const passwordInput = ref(null);

watch(() => props.show, async (newVal) => {
  if (newVal) {
    password.value = '';
    await nextTick();
    passwordInput.value?.focus();
  }
});

function handleConfirm() {
  if (password.value) {
    emit('confirm', password.value);
    password.value = '';
  }
}
</script>

<style scoped>
/* Google Material Design Style */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(0, 0, 0, 0.32);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1050;
  animation: fadeIn 0.15s cubic-bezier(0.4, 0.0, 0.2, 1);
}

.modal-dialog {
  max-width: 448px;
  width: 90%;
  animation: scaleIn 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
}

.modal-content {
  background: white;
  border-radius: 8px;
  box-shadow: 0 8px 10px 1px rgba(0,0,0,.14), 0 3px 14px 2px rgba(0,0,0,.12), 0 5px 5px -3px rgba(0,0,0,.2);
  overflow: hidden;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 24px 24px 20px;
  border-bottom: none;
}

.modal-header h5 {
  color: #202124;
  margin: 0;
  font-size: 20px;
  font-weight: 400;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.btn-close {
  background: transparent;
  border: none;
  opacity: 0.6;
  cursor: pointer;
  width: 24px;
  height: 24px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), opacity 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
}

.btn-close:hover {
  background-color: rgba(95, 99, 104, 0.08);
  opacity: 1;
}

.modal-body {
  padding: 0 24px 24px;
}

.modal-body p {
  color: #5f6368;
  line-height: 1.5;
  margin-bottom: 24px;
  font-size: 14px;
  font-family: 'Roboto', sans-serif;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 16px 24px;
  border-top: none;
  background: white;
}

.form-group {
  margin-top: 0;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  font-weight: 500;
  color: #202124;
  font-size: 14px;
  font-family: 'Roboto', sans-serif;
}

.form-control {
  width: 100%;
  padding: 12px 16px;
  border: 1px solid #dadce0;
  border-radius: 4px;
  font-size: 14px;
  transition: border-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), box-shadow 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  font-family: 'Roboto', sans-serif;
  background: white;
}

.form-control:hover {
  border-color: #202124;
}

.form-control:focus {
  outline: none;
  border-color: #1a73e8;
  box-shadow: 0 1px 1px 0 rgba(66,133,244,.45), 0 1px 3px 1px rgba(66,133,244,.3);
}

.btn-secondary {
  background: transparent;
  border: none;
  padding: 8px 24px;
  border-radius: 4px;
  font-weight: 500;
  color: #1a73e8;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  font-size: 14px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
  letter-spacing: 0.25px;
  height: 36px;
  cursor: pointer;
}

.btn-secondary:hover {
  background-color: rgba(26, 115, 232, 0.04);
}

.btn-secondary:active {
  background-color: rgba(26, 115, 232, 0.12);
}

.btn-primary {
  background-color: #1a73e8;
  border: none;
  padding: 8px 24px;
  border-radius: 4px;
  font-weight: 500;
  color: white;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), box-shadow 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  font-size: 14px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
  letter-spacing: 0.25px;
  height: 36px;
  cursor: pointer;
  box-shadow: none;
}

.btn-primary:hover:not(:disabled) {
  background-color: #1765cc;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 1px 3px 1px rgba(60,64,67,.15);
}

.btn-primary:active:not(:disabled) {
  background-color: #1557b0;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 2px 6px 2px rgba(60,64,67,.15);
}

.btn-primary:disabled {
  background-color: rgba(26, 115, 232, 0.12);
  color: rgba(255, 255, 255, 0.5);
  cursor: not-allowed;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes scaleIn {
  from {
    opacity: 0;
    transform: scale(0.8);
  }
  to {
    opacity: 1;
    transform: scale(1);
  }
}
</style>
