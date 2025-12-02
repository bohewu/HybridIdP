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
/* Google Material Design Style */
.account-list {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.account-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-bottom: 1px solid #e8eaed;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  cursor: pointer;
  min-height: 72px;
}

.account-card:last-child {
  border-bottom: none;
}

.account-card:hover:not(.current) {
  background-color: #f8f9fa;
}

.account-card.current {
  background-color: #e8f0fe;
}

.account-info {
  flex: 1;
  min-width: 0;
}

.account-info h4 {
  margin: 0 0 4px 0;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  color: #202124;
  font-weight: 500;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.account-info p {
  margin: 0;
  font-size: 13px;
  color: #5f6368;
  font-family: 'Roboto', sans-serif;
}

.account-card.current .account-info h4 {
  color: #1967d2;
}

.badge {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 12px;
  font-weight: 500;
  background-color: #1a73e8;
  color: white;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.btn-secondary {
  background-color: #fff;
  border: 1px solid #dadce0;
  padding: 8px 24px;
  border-radius: 4px;
  font-weight: 500;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), box-shadow 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), border-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  box-shadow: none;
  color: #1a73e8;
  font-size: 14px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
  letter-spacing: 0.25px;
  text-transform: none;
  height: 36px;
}

.btn-secondary:hover {
  background-color: #f8f9fa;
  border-color: #dadce0;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 1px 3px 1px rgba(60,64,67,.15);
}

.btn-secondary:active {
  background-color: #f1f3f4;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 2px 6px 2px rgba(60,64,67,.15);
}

@media (max-width: 576px) {
  .account-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
    padding: 16px;
  }
  
  .account-actions {
    width: 100%;
  }
  
  .btn-secondary {
    width: 100%;
  }
}
</style>
