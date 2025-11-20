<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'

const { t } = useI18n()

const alerts = ref([])
const loading = ref(true)
const error = ref(null)
const connection = ref(null)

const fetchAlerts = async () => {
  try {
    loading.value = true
    error.value = null

    const response = await fetch('/api/admin/monitoring/alerts', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    const data = await response.json()
    alerts.value = data
  } catch (err) {
    console.error('Failed to fetch real-time alerts:', err)
    error.value = err.message || 'Failed to load alerts'
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

    connection.value.on('SecurityAlertsUpdated', (updatedAlerts) => {
      console.log('Received security alerts update:', updatedAlerts)
      alerts.value = updatedAlerts
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
  await fetchAlerts()
  await startSignalRConnection()
})

onUnmounted(() => {
  stopSignalRConnection()
})

const getAlertClass = (type) => {
  const classes = {
    warning: 'border-yellow-200 bg-yellow-50',
    danger: 'border-red-200 bg-red-50',
    info: 'border-blue-200 bg-blue-50'
  }
  return classes[type] || 'border-gray-200 bg-gray-50'
}

const formatTimestamp = (timestamp) => {
  return new Date(timestamp).toLocaleString()
}
</script>

<template>
  <div class="real-time-alerts">
    <h3 class="text-lg font-semibold mb-4">{{ t('admin.monitoring.realTimeAlerts.title') }}</h3>

    <div v-if="loading" class="text-center py-4">
      <div class="spinner-border spinner-border-sm" role="status"></div>
    </div>

    <div v-else-if="alerts.length === 0" class="text-center py-8 text-gray-500">
      {{ t('admin.monitoring.realTimeAlerts.noAlerts') }}
    </div>

    <div v-else class="space-y-3">
      <div
        v-for="alert in alerts"
        :key="alert.id"
        :class="['alert-item', getAlertClass(alert.type)]"
      >
        <div class="flex items-start space-x-3">
          <div class="alert-icon">
            <i :class="alert.type === 'danger' ? 'bi bi-exclamation-triangle' : 'bi bi-info-circle'"></i>
          </div>
          <div class="flex-1">
            <p class="alert-message">{{ alert.message }}</p>
            <p class="alert-timestamp text-sm text-gray-600">{{ formatTimestamp(alert.timestamp) }}</p>
          </div>
          <div class="alert-severity">
            <span :class="alert.severity === 'high' ? 'badge bg-danger' : 'badge bg-warning'">
              {{ alert.severity }}
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.alert-item {
  border: 1px solid;
  border-radius: 0.5rem;
  padding: 1rem;
}

.alert-icon {
  font-size: 1.25rem;
  color: #6b7280;
}

.alert-message {
  margin: 0;
  font-weight: 500;
}

.alert-timestamp {
  margin-top: 0.25rem;
}

.badge {
  font-size: 0.75rem;
  padding: 0.25rem 0.5rem;
  border-radius: 0.25rem;
}
</style>