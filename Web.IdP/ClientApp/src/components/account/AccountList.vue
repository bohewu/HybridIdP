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
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  transition: all 0.3s;
}

.account-card.current {
  border-color: #17a2b8;
  background-color: #f0f8ff;
}

.account-card:hover:not(.current) {
  border-color: #6c757d;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

.account-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.account-info p {
  margin: 0;
  font-size: 0.9rem;
}
</style>
