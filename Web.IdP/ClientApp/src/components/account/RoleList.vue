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
.role-list {
  display: grid;
  gap: 1rem;
}

.role-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  transition: all 0.3s;
}

.role-card.active {
  border-color: #28a745;
  background-color: #f0f9f4;
}

.role-card:hover:not(.active) {
  border-color: #3498db;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

.role-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.role-info p {
  margin: 0;
  font-size: 0.9rem;
}
</style>
