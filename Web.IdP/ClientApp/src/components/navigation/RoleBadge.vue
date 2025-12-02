<template>
  <a v-if="activeRole" :href="myAccountUrl" class="role-badge" :title="t('myAccount.title')">
    <i class="bi bi-person-badge"></i>
    <span class="role-name">{{ activeRole }}</span>
  </a>
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
/* Google Material Design Style */
.role-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  background: #e8f0fe;
  color: #1967d2;
  border-radius: 16px;
  font-size: 13px;
  font-weight: 500;
  font-family: 'Google Sans', 'Roboto', sans-serif;
  text-decoration: none;
  transition: background-color 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
  border: 1px solid #d2e3fc;
  line-height: 20px;
}

.role-badge:hover {
  background: #d2e3fc;
  color: #1558b0;
  text-decoration: none;
}

.role-badge i {
  font-size: 16px;
}

.role-name {
  letter-spacing: 0.25px;
}
</style>
