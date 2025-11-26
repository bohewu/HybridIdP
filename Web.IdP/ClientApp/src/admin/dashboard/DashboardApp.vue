<template>
  <div class="max-w-7xl mx-auto dashboard-root" v-loading="loading">
    <!-- Header -->
    <div class="mb-8">
      <h1 class="text-3xl font-bold text-gray-900">{{ $t('admin.dashboard.title') }}</h1>
      <p class="mt-2 text-gray-600">{{ $t('admin.dashboard.subtitle') }}</p>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="flex items-center justify-center py-12">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg p-4 mb-6">
      <div class="flex items-center">
        <svg class="w-5 h-5 text-red-500 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
        </svg>
        <span class="text-red-800 font-medium">{{ error }}</span>
      </div>
    </div>

    <!-- Dashboard Content -->
    <div v-else>
      <!-- Quick Stats -->
      <div class="mb-8 bg-white rounded-lg shadow-sm p-6">
        <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('admin.dashboard.quickStats') }}</h3>
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div class="text-center p-4 bg-gradient-to-br from-indigo-50 to-indigo-100 rounded-lg">
            <div class="text-3xl font-bold text-indigo-600">{{ stats.totalClients }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('admin.dashboard.stats.totalClients') }}</div>
          </div>
          <div class="text-center p-4 bg-gradient-to-br from-green-50 to-green-100 rounded-lg">
            <div class="text-3xl font-bold text-green-600">{{ stats.totalScopes }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('admin.dashboard.stats.totalScopes') }}</div>
          </div>
          <div class="text-center p-4 bg-gradient-to-br from-blue-50 to-blue-100 rounded-lg">
            <div class="text-3xl font-bold text-blue-600">{{ stats.totalUsers }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('admin.dashboard.stats.totalUsers') }}</div>
          </div>
        </div>
      </div>

      <!-- Monitoring Cards -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Activity Dashboard Card -->
        <div class="bg-white rounded-lg shadow-sm p-6">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('admin.monitoring.activityDashboard.title') }}</h3>
          <ActivityDashboard />
        </div>

        <!-- Security Metrics Card -->
        <div class="bg-white rounded-lg shadow-sm p-6">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('admin.monitoring.securityMetrics.title') }}</h3>
          <SecurityMetrics />
        </div>
      </div>

      <!-- Real-time Alerts Card -->
      <div class="mt-6 bg-white rounded-lg shadow-sm p-6">
        <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('admin.monitoring.realTimeAlerts.title') }}</h3>
        <RealTimeAlerts />
      </div>

      <!-- Navigation Cards -->
      <div class="mt-6 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 auto-rows-fr">
        <!-- OIDC Clients Card -->
        <div class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow p-6 h-full flex flex-col">
          <div class="flex items-center justify-center w-12 h-12 bg-indigo-100 rounded-lg mb-4">
            <svg class="w-6 h-6 text-indigo-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"></path>
            </svg>
          </div>
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('admin.dashboard.cards.clients.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('admin.dashboard.cards.clients.description') }}</p>
          <a href="/Admin/Clients" class="inline-flex items-center text-indigo-600 hover:text-indigo-700 font-medium text-sm mt-auto">
            {{ $t('admin.dashboard.cards.clients.action') }}
            <svg class="ml-2 w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"></path>
            </svg>
          </a>
        </div>

        <!-- OIDC Scopes Card -->
        <div class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow p-6 h-full flex flex-col">
          <div class="flex items-center justify-center w-12 h-12 bg-green-100 rounded-lg mb-4">
            <svg class="w-6 h-6 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"></path>
            </svg>
          </div>
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('admin.dashboard.cards.scopes.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('admin.dashboard.cards.scopes.description') }}</p>
          <a href="/Admin/Scopes" class="inline-flex items-center text-green-600 hover:text-green-700 font-medium text-sm mt-auto">
            {{ $t('admin.dashboard.cards.scopes.action') }}
            <svg class="ml-2 w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"></path>
            </svg>
          </a>
        </div>

        <!-- Users Card -->
        <div class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow p-6 h-full flex flex-col">
          <div class="flex items-center justify-center w-12 h-12 bg-blue-100 rounded-lg mb-4">
            <!-- User Group Icon -->
            <svg class="w-7 h-7 text-blue-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-1a6 6 0 00-9-5.197M17 20H7m10 0v-1a6 6 0 00-9-5.197M7 20H2v-1a6 6 0 019-5.197M12 11a4 4 0 100-8 4 4 0 000 8zm6 0a4 4 0 100-8 4 4 0 000 8zm-12 0a4 4 0 100-8 4 4 0 000 8z" />
            </svg>
          </div>
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('admin.dashboard.cards.users.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('admin.dashboard.cards.users.description') }}</p>
          <a href="/Admin/Users" class="inline-flex items-center text-green-600 hover:text-green-700 font-medium text-sm mt-auto">
            {{ $t('admin.dashboard.cards.users.action') }}
            <svg class="ml-2 w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"></path>
            </svg>
          </a>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { HubConnectionBuilder } from '@microsoft/signalr'
import ActivityDashboard from '../monitoring/components/ActivityDashboard.vue'
import SecurityMetrics from '../monitoring/components/SecurityMetrics.vue'
import RealTimeAlerts from '../monitoring/components/RealTimeAlerts.vue'

const { t } = useI18n()

const loading = ref(true)
const error = ref(null)
const stats = ref({
  totalClients: 0,
  totalScopes: 0,
  totalUsers: 0
})

const hubConnection = ref(null)

const fetchStats = async () => {
  try {
    loading.value = true
    error.value = null
    
    const response = await fetch('/api/admin/dashboard/stats')
    
    if (!response.ok) {
      if (response.status === 401) {
        throw new Error(t('admin.dashboard.errors.unauthorized'))
      } else if (response.status === 403) {
        throw new Error(t('admin.dashboard.errors.accessDenied'))
      } else {
        throw new Error(`${t('admin.dashboard.errors.loadFailed')} (${response.status})`)
      }
    }
    
    const data = await response.json()
    stats.value = data
  } catch (err) {
    console.error('Dashboard stats error:', err)
    error.value = err.message || t('admin.dashboard.errors.loadFailed')
  } finally {
    loading.value = false
  }
}

const setupSignalR = async () => {
  try {
    hubConnection.value = new HubConnectionBuilder()
      .withUrl('/monitoringHub')
      .build()

    await hubConnection.value.start()
    console.log('SignalR connected')

    // Listen for updates
    hubConnection.value.on('ActivityStatsUpdated', () => {
      console.log('Activity stats updated')
      // Could refresh data here
    })

    hubConnection.value.on('SecurityAlertsUpdated', () => {
      console.log('Security alerts updated')
    })

    hubConnection.value.on('SystemMetricsUpdated', () => {
      console.log('System metrics updated')
    })
  } catch (err) {
    console.error('SignalR connection failed:', err)
  }
}

const cleanupSignalR = () => {
  if (hubConnection.value) {
    hubConnection.value.stop()
  }
}

onMounted(async () => {
  await fetchStats()
  await setupSignalR()
})

onUnmounted(() => {
  cleanupSignalR()
})
</script>
