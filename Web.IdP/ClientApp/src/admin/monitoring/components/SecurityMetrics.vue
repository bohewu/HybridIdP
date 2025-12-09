<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'

const { t } = useI18n()

const metrics = ref({
  gauges: {},
  counters: {},
  histograms: {}
})
const loading = ref(true)
const error = ref(null)
const connection = ref(null)

const fetchMetrics = async () => {
  try {
    console.debug('[SecurityMetrics] fetchMetrics: start')
    loading.value = true
    error.value = null

    const response = await fetch('/api/admin/monitoring/system-metrics', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    const data = await response.json()
    console.debug('[SecurityMetrics] fetchMetrics: received data', data)
    metrics.value = data
  } catch (err) {
    console.error('Failed to fetch security metrics:', err)
    error.value = err.message || 'Failed to load security metrics'
  } finally {
    console.debug('[SecurityMetrics] fetchMetrics: finished, setting loading=false')
    loading.value = false
  }
}

const startSignalRConnection = async () => {
  try {
    connection.value = new signalR.HubConnectionBuilder()
      .withUrl('/monitoringHub')
      .withAutomaticReconnect()
      .build()

    connection.value.on('ReceiveSystemMetrics', (updatedMetrics) => {
      // console.log('Received system metrics update:', updatedMetrics)
      metrics.value = updatedMetrics
      loading.value = false
    })

    connection.value.onclose(() => {
      // console.log('SignalR connection stopped')
      loading.value = false
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
  console.debug('[SecurityMetrics] mounted (initial loading =', loading.value, ')')
  await fetchMetrics()
  await startSignalRConnection()
})

onUnmounted(() => {
  stopSignalRConnection()
})
</script>

<template>
  <div class="security-metrics">

    <!-- Use the shared LoadingIndicator component (inline, small) -->
    <LoadingIndicator v-if="loading" :loading="loading" size="sm" />

    <div v-else class="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <!-- Gauges -->
      <div class="metric-chart">
        <h4 class="text-md font-medium mb-2">{{ t('monitoring.securityMetrics.gauges') }}</h4>
        <div class="space-y-2">
          <div v-for="(value, key) in metrics.gauges" :key="key" class="flex justify-between">
            <span class="text-sm text-gray-600">{{ key }}</span>
            <span class="text-sm font-medium">{{ value.toFixed(2) }}</span>
          </div>
          <div v-if="Object.keys(metrics.gauges).length === 0" class="text-center text-gray-500 text-sm">
            {{ t('monitoring.securityMetrics.noGauges') }}
          </div>
        </div>
      </div>

      <!-- Counters -->
      <div class="metric-chart">
        <h4 class="text-md font-medium mb-2">{{ t('monitoring.securityMetrics.counters') }}</h4>
        <div class="space-y-2">
          <div v-for="(value, key) in metrics.counters" :key="key" class="flex justify-between">
            <span class="text-sm text-gray-600">{{ key }}</span>
            <span class="text-sm font-medium">{{ value }}</span>
          </div>
          <div v-if="Object.keys(metrics.counters).length === 0" class="text-center text-gray-500 text-sm">
            {{ t('monitoring.securityMetrics.noCounters') }}
          </div>
        </div>
      </div>

      <!-- Histograms -->
      <div class="metric-chart" v-if="Object.keys(metrics.histograms).length > 0">
        <h4 class="text-md font-medium mb-2">{{ t('monitoring.securityMetrics.histograms') }}</h4>
        <div class="space-y-2">
          <div v-for="(values, key) in metrics.histograms" :key="key">
            <div class="text-sm text-gray-600 mb-1">{{ key }}</div>
            <div class="text-xs text-gray-500">Values: {{ values.join(', ') }}</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.metric-chart {
  background: white;
  border-radius: 0.5rem;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
  padding: 1rem;
}

.chart-placeholder {
  height: 200px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed #d1d5db;
  border-radius: 0.25rem;
}
</style>