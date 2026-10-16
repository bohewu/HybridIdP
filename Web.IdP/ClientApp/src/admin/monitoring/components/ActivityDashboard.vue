<script setup>
import {onMounted, onUnmounted, ref} from 'vue'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import {useI18n} from 'vue-i18n'
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
const hasData = ref(false)
const connection = ref(null)

const fetchStats = async () => {
  if (loading.value && hasData.value) return
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

    stats.value = await response.json()
    hasData.value = true
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
      // console.log('Received activity stats update:', updatedStats)
      stats.value = updatedStats
      hasData.value = true
      error.value = null
    })

    await connection.value.start()
    // console.log('SignalR connection started for monitoring')
  } catch (err) {
    console.error('SignalR connection failed:', err)
  }
}

const stopSignalRConnection = async () => {
  if (connection.value) {
    await connection.value.stop()
    // console.log('SignalR connection stopped')
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

    <div class="mb-3 flex items-center justify-end gap-3">
      <p v-if="loading && hasData" role="status" class="text-sm text-gray-600">{{ t('monitoring.refreshing') }}</p>
      <button type="button" :disabled="loading" @click="fetchStats" class="text-sm text-blue-600 hover:text-blue-800 disabled:opacity-50">{{ t('monitoring.refresh') }}</button>
    </div>
    <LoadingPlaceholder v-if="loading && !hasData" :message="t('monitoring.loading')" />
    <div v-if="error" role="alert" class="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
      {{ t('monitoring.loadFailed') }}
      <span v-if="hasData">{{ t('monitoring.stale') }}</span>
    </div>

    <p v-if="hasData && Object.values(stats).every(value => value === 0)" class="mb-3 text-sm text-gray-500">{{ t('monitoring.activityDashboard.noActivity') }}</p>
    <div v-if="hasData" class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-people"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.activeSessions }}</div>
          <div class="stat-label">{{ t('monitoring.activityDashboard.activeSessions') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-box-arrow-in-right"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.totalLogins }}</div>
          <div class="stat-label">{{ t('monitoring.activityDashboard.totalLogins') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-x-circle"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.failedLogins }}</div>
          <div class="stat-label">{{ t('monitoring.activityDashboard.failedLogins') }}</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">
          <i class="bi bi-shield-exclamation"></i>
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats.riskScore }}</div>
          <div class="stat-label">{{ t('monitoring.activityDashboard.riskScore') }}</div>
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