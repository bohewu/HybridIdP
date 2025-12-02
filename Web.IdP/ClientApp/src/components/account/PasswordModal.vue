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
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1050;
}

.modal-dialog {
  max-width: 500px;
  width: 90%;
}

.modal-content {
  background: white;
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid #dee2e6;
}

.modal-body {
  padding: 1.5rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem 1.5rem;
  border-top: 1px solid #dee2e6;
}

.form-group {
  margin-top: 1rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
}
</style>
