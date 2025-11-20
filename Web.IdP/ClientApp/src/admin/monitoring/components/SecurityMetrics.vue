<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const metrics = ref({
  loginAttempts: [],
  activeSessions: [],
  failedLogins: []
})
const loading = ref(true)
const error = ref(null)

const fetchMetrics = async () => {
  try {
    loading.value = true
    error.value = null

    const response = await fetch('/api/admin/monitoring/metrics', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    const data = await response.json()
    metrics.value = data
  } catch (err) {
    console.error('Failed to fetch security metrics:', err)
    error.value = err.message || 'Failed to load security metrics'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchMetrics()
})
</script>

<template>
  <div class="security-metrics">
    <h3 class="text-lg font-semibold mb-4">{{ t('admin.monitoring.securityMetrics.title') }}</h3>

    <div v-if="loading" class="text-center py-4">
      <div class="spinner-border spinner-border-sm" role="status"></div>
    </div>

    <div v-else class="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <div class="metric-chart">
        <h4 class="text-md font-medium mb-2">{{ t('admin.monitoring.securityMetrics.loginAttempts') }}</h4>
        <div class="chart-placeholder">
          <!-- TODO: Implement chart with Chart.js or similar -->
          <div class="text-center text-gray-500">
            Chart: {{ metrics.loginAttempts.join(', ') }}
          </div>
        </div>
      </div>

      <div class="metric-chart">
        <h4 class="text-md font-medium mb-2">{{ t('admin.monitoring.securityMetrics.activeSessions') }}</h4>
        <div class="chart-placeholder">
          <div class="text-center text-gray-500">
            Chart: {{ metrics.activeSessions.join(', ') }}
          </div>
        </div>
      </div>

      <div class="metric-chart">
        <h4 class="text-md font-medium mb-2">{{ t('admin.monitoring.securityMetrics.failedLogins') }}</h4>
        <div class="chart-placeholder">
          <div class="text-center text-gray-500">
            Chart: {{ metrics.failedLogins.join(', ') }}
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