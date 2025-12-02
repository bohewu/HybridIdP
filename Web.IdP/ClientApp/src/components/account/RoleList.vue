<template>
  <div class="role-list">
    <div 
      v-for="role in roles" 
      :key="role.roleId"
      class="role-card"
      :class="{ 'active': role.isActive }"
    >
      <div class="role-info">
        <h4>
          {{ role.roleName }}
          <span v-if="role.isActive" class="badge bg-success">{{ t('myAccount.active') }}</span>
        </h4>
        <p v-if="role.description" class="text-muted">{{ role.description }}</p>
      </div>
      <div class="role-actions">
        <button
          v-if="!role.isActive"
          class="btn btn-primary"
          @click="$emit('switchRole', role)"
        >
          {{ t('myAccount.switchToRole') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';

defineProps({
  roles: Array,
  activeRoleId: String
});

defineEmits(['switchRole']);

const { t } = useI18n();
</script>

<style scoped>
/* Google Material Design Style */
.role-list {
  display: flex;
  flex-direction: column;
  gap: 0;
}

.role-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-bottom: 1px solid #e8eaed;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  cursor: pointer;
  min-height: 72px;
}

.role-card:last-child {
  border-bottom: none;
}

.role-card:hover:not(.active) {
  background-color: #f8f9fa;
}

.role-card.active {
  background-color: #e8f0fe;
}

.role-info {
  flex: 1;
  min-width: 0;
}

.role-info h4 {
  margin: 0 0 4px 0;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  color: #202124;
  font-weight: 500;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.role-info p {
  margin: 0;
  font-size: 13px;
  color: #5f6368;
  line-height: 1.4;
  font-family: 'Roboto', sans-serif;
}

.role-card.active .role-info h4 {
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

.btn-primary {
  background-color: #1a73e8;
  border: none;
  padding: 8px 24px;
  border-radius: 4px;
  font-weight: 500;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1), box-shadow 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  box-shadow: none;
  color: white;
  font-size: 14px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
  letter-spacing: 0.25px;
  text-transform: none;
  height: 36px;
}

.btn-primary:hover {
  background-color: #1765cc;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 1px 3px 1px rgba(60,64,67,.15);
}

.btn-primary:active {
  background-color: #1557b0;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 2px 6px 2px rgba(60,64,67,.15);
}

@media (max-width: 576px) {
  .role-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
    padding: 16px;
  }
  
  .role-actions {
    width: 100%;
  }
  
  .btn-primary {
    width: 100%;
  }
}
</style>
