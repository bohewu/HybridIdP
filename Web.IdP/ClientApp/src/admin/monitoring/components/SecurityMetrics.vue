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

onMounted(async () => {
  // TODO: Fetch metrics data from API, possibly /metrics
  // For now, mock data
  metrics.value = {
    loginAttempts: [120, 135, 142, 158, 145, 162, 178],
    activeSessions: [25, 32, 28, 45, 38, 42, 50],
    failedLogins: [2, 3, 1, 5, 2, 4, 3]
  }
  loading.value = false
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