<template>
  <div class="account-list">
    <div 
      v-for="account in accounts" 
      :key="account.userId"
      class="account-card"
      :class="{ 'current': account.isCurrent }"
    >
      <div class="account-info">
        <h4>
          {{ account.email }}
          <span v-if="account.isCurrent" class="badge bg-info">{{ t('myAccount.currentAccount') }}</span>
        </h4>
        <p class="text-muted">{{ t('myAccount.username') }}: {{ account.username }}</p>
      </div>
      <div class="account-actions">
        <button
          v-if="!account.isCurrent"
          class="btn btn-secondary"
          @click="$emit('switchAccount', account)"
        >
          {{ t('myAccount.switchToAccount') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';

defineProps({
  accounts: Array
});

defineEmits(['switchAccount']);

const { t } = useI18n();
</script>

<style scoped>
.account-list {
  display: grid;
  gap: 1rem;
}

.account-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  border: 2px solid #e8e8e8;
  border-radius: 12px;
  transition: all 0.3s;
  background: linear-gradient(to right, #ffffff, #f8f9fa);
  cursor: pointer;
}

.account-card.current {
  border-color: #17a2b8;
  background: linear-gradient(135deg, #d1ecf1 0%, #bee5eb 100%);
  box-shadow: 0 4px 12px rgba(23, 162, 184, 0.2);
}

.account-card:hover:not(.current) {
  border-color: #667eea;
  background: linear-gradient(to right, #f8f9ff, #ffffff);
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.15);
  transform: translateX(4px);
}

.account-info {
  flex: 1;
}

.account-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 1.2rem;
  color: #333;
  font-weight: 600;
}

.account-info p {
  margin: 0;
  font-size: 0.9rem;
  color: #666;
}

.account-card.current .account-info h4 {
  color: #0c5460;
}

.badge {
  font-size: 0.75rem;
  padding: 0.35rem 0.75rem;
  border-radius: 20px;
  font-weight: 600;
}

.btn-secondary {
  background: linear-gradient(135deg, #6c757d 0%, #5a6268 100%);
  border: none;
  padding: 0.6rem 1.5rem;
  border-radius: 8px;
  font-weight: 500;
  transition: all 0.3s;
  box-shadow: 0 2px 8px rgba(108, 117, 125, 0.3);
  color: white;
}

.btn-secondary:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(108, 117, 125, 0.4);
  background: linear-gradient(135deg, #5a6268 0%, #545b62 100%);
}

@media (max-width: 576px) {
  .account-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
  }
  
  .account-actions {
    width: 100%;
  }
  
  .btn-secondary {
    width: 100%;
  }
}
</style>
