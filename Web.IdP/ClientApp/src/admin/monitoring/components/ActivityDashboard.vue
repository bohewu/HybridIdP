<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'

const { t } = useI18n()

const stats = ref({
  activeSessions: 0,
  totalLogins: 0,
  failedLogins: 0,
  riskScore: 0
})
const loading = ref(true)
const error = ref(null)
const connection = ref(null)

const fetchStats = async () => {
  try {
    loading.value = true
    error.value = null

    const response = await fetch('/api/admin/monitoring/stats', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    const data = await response.json()
    stats.value = data
  } catch (err) {
    console.error('Failed to fetch activity stats:', err)
    error.value = err.message || 'Failed to load activity statistics'
  } finally {
    loading.value = false
  }
}

const startSignalRConnection = async () => {
  try {
    connection.value = new signalR.HubConnectionBuilder()
      .withUrl('/monitoringHub')
      .withAutomaticReconnect()
      .build()

    connection.value.on('ActivityStatsUpdated', (updatedStats) => {
      console.log('Received activity stats update:', updatedStats)
      stats.value = updatedStats
    })

    await connection.value.start()
    console.log('SignalR connection started for monitoring')
  } catch (err) {
    console.error('SignalR connection failed:', err)
  }
}

const stopSignalRConnection = async () => {
  if (connection.value) {
    await connection.value.stop()
    console.log('SignalR connection stopped')
  }
}

onMounted(async () => {
  await fetchStats()
  await startSignalRConnection()
})

onUnmounted(() => {
  stopSignalRConnection()
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