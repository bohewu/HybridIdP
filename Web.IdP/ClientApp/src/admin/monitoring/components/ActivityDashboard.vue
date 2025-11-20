<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const stats = ref({
  activeSessions: 0,
  totalLogins: 0,
  failedLogins: 0,
  riskScore: 0
})
const loading = ref(true)

onMounted(async () => {
  // TODO: Fetch real-time stats from API
  // For now, mock data
  stats.value = {
    activeSessions: 42,
    totalLogins: 1250,
    failedLogins: 15,
    riskScore: 2.3
  }
  loading.value = false
})
</script>

<template>
  <div class="activity-dashboard">
    <h3 class="text-lg font-semibold mb-4">{{ t('admin.monitoring.activityDashboard.title') }}</h3>

    <div v-if="loading" class="text-center py-4">
      <div class="spinner-border spinner-border-sm" role="status"></div>
    </div>

    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-people"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.activeSessions }}</div>
          <div class="stat-label">{{ t('admin.monitoring.activityDashboard.activeSessions') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-box-arrow-in-right"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.totalLogins }}</div>
          <div class="stat-label">{{ t('admin.monitoring.activityDashboard.totalLogins') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-x-circle"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.failedLogins }}</div>
          <div class="stat-label">{{ t('admin.monitoring.activityDashboard.failedLogins') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-shield-exclamation"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.riskScore }}</div>
          <div class="stat-label">{{ t('admin.monitoring.activityDashboard.riskScore') }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.stat-card {
  background: white;
  border-radius: 0.5rem;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
  padding: 1rem;
  display: flex;
  align-items: center;
  gap: 1rem;
}

.stat-icon {
  font-size: 1.5rem;
  color: #2563eb;
}

.stat-content {
  flex: 1;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: bold;
  color: #111827;
}

.stat-label {
  font-size: 0.875rem;
  color: #6b7280;
}
</style>