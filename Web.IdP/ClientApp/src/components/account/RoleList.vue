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
  border: 2px solid #e8e8e8;
  border-radius: 12px;
  transition: all 0.3s;
  background: linear-gradient(to right, #ffffff, #f8f9fa);
  cursor: pointer;
}

.role-card.active {
  border-color: #28a745;
  background: linear-gradient(135deg, #d4edda 0%, #c3e6cb 100%);
  box-shadow: 0 4px 12px rgba(40, 167, 69, 0.2);
}

.role-card:hover:not(.active) {
  border-color: #667eea;
  background: linear-gradient(to right, #f8f9ff, #ffffff);
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.15);
  transform: translateX(4px);
}

.role-info {
  flex: 1;
}

.role-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 1.2rem;
  color: #333;
  font-weight: 600;
}

.role-info p {
  margin: 0;
  font-size: 0.9rem;
  color: #666;
  line-height: 1.5;
}

.role-card.active .role-info h4 {
  color: #155724;
}

.badge {
  font-size: 0.75rem;
  padding: 0.35rem 0.75rem;
  border-radius: 20px;
  font-weight: 600;
}

.btn-primary {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border: none;
  padding: 0.6rem 1.5rem;
  border-radius: 8px;
  font-weight: 500;
  transition: all 0.3s;
  box-shadow: 0 2px 8px rgba(102, 126, 234, 0.3);
}

.btn-primary:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.4);
}

@media (max-width: 576px) {
  .role-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
  }
  
  .role-actions {
    width: 100%;
  }
  
  .btn-primary {
    width: 100%;
  }
}
</style>
