<template>
  <div v-if="activeRole" class="role-badge">
    <span class="role-icon">👤</span>
    <span class="role-name">{{ activeRole }}</span>
    <a :href="myAccountUrl" class="role-link" :title="t('myAccount.title')">
      <span class="switch-icon">🔄</span>
    </a>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();
const activeRole = ref(null);
const myAccountUrl = '/Account/MyAccount';

onMounted(() => {
  // Read active role from meta tag or data attribute
  const metaTag = document.querySelector('meta[name="active-role"]');
  if (metaTag) {
    activeRole.value = metaTag.getAttribute('content');
  }
});
</script>

<style scoped>
.role-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border-radius: 20px;
  font-size: 0.9rem;
  font-weight: 500;
  box-shadow: 0 2px 8px rgba(102, 126, 234, 0.3);
}

.role-icon {
  font-size: 1.1rem;
}

.role-name {
  letter-spacing: 0.5px;
}

.role-link {
  display: flex;
  align-items: center;
  color: white;
  text-decoration: none;
  padding: 0.25rem;
  border-radius: 50%;
  transition: background 0.2s;
}

.role-link:hover {
  background: rgba(255, 255, 255, 0.2);
}

.switch-icon {
  font-size: 1rem;
  display: block;
}
</style>
