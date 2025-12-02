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
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1050;
  animation: fadeIn 0.3s ease-out;
}

.modal-dialog {
  max-width: 500px;
  width: 90%;
  animation: slideUp 0.4s ease-out;
}

.modal-content {
  background: white;
  border-radius: 16px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.3);
  overflow: hidden;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem 2rem;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border-bottom: none;
}

.modal-header h5 {
  color: white;
  margin: 0;
  font-size: 1.3rem;
  font-weight: 600;
}

.btn-close {
  background: white;
  opacity: 0.8;
  border-radius: 50%;
  padding: 0.5rem;
  width: 32px;
  height: 32px;
}

.btn-close:hover {
  opacity: 1;
}

.modal-body {
  padding: 2rem;
}

.modal-body p {
  color: #666;
  line-height: 1.6;
  margin-bottom: 1.5rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 1.5rem 2rem;
  background: #f8f9fa;
  border-top: 1px solid #e9ecef;
}

.form-group {
  margin-top: 0;
}

.form-group label {
  display: block;
  margin-bottom: 0.75rem;
  font-weight: 600;
  color: #333;
}

.form-control {
  width: 100%;
  padding: 0.75rem 1rem;
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  font-size: 1rem;
  transition: all 0.3s;
}

.form-control:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
}

.btn-secondary {
  background: #6c757d;
  border: none;
  padding: 0.75rem 1.5rem;
  border-radius: 8px;
  font-weight: 500;
  color: white;
  transition: all 0.3s;
}

.btn-secondary:hover {
  background: #5a6268;
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(108, 117, 125, 0.3);
}

.btn-primary {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border: none;
  padding: 0.75rem 1.5rem;
  border-radius: 8px;
  font-weight: 500;
  color: white;
  transition: all 0.3s;
}

.btn-primary:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.4);
}

.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(50px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
