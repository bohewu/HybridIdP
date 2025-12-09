<template>
  <div
    class="max-w-7xl mx-auto dashboard-root"
    v-loading="{ loading: loading, overlay: true, message: t('dashboard.loading') }">
    <!-- Header -->
    <div class="mb-8">
      <h1 class="text-3xl font-bold text-gray-900">{{ $t('dashboard.title') }}</h1>
      <p class="mt-2 text-gray-600">{{ $t('dashboard.subtitle') }}</p>
    </div>

    <!-- Page-level loading is now handled by the v-loading overlay so the
         page content remains visible while data loads. Keep the
         LoadingIndicator component available for smaller/local usages. -->

    <!-- Error State -->
    <div v-if="error" class="bg-red-50 border border-red-200 rounded-lg p-4 mb-6">
      <div class="flex items-center">
        <svg class="w-5 h-5 text-red-500 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
        </svg>
        <span class="text-red-800 font-medium">{{ error }}</span>
      </div>
    </div>

    <!-- Dashboard Content -->
    <div v-if="!error">
      <!-- Quick Stats -->
      <div class="mb-8 bg-white rounded-lg shadow-sm p-6">
        <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('dashboard.quickStats') }}</h3>
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div class="text-center p-4 bg-gradient-to-br from-indigo-50 to-indigo-100 rounded-lg">
            <div class="text-3xl font-bold text-indigo-600">{{ stats.totalClients }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('dashboard.stats.totalClients') }}</div>
          </div>
          <div class="text-center p-4 bg-gradient-to-br from-green-50 to-green-100 rounded-lg">
            <div class="text-3xl font-bold text-green-600">{{ stats.totalScopes }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('dashboard.stats.totalScopes') }}</div>
          </div>
          <div class="text-center p-4 bg-gradient-to-br from-blue-50 to-blue-100 rounded-lg">
            <div class="text-3xl font-bold text-blue-600">{{ stats.totalUsers }}</div>
            <div class="text-sm text-gray-600 mt-1">{{ $t('dashboard.stats.totalUsers') }}</div>
          </div>
        </div>
      </div>

      <!-- System Health Widget -->
      <div class="mb-8 bg-white rounded-lg shadow-sm p-6">
        <div class="flex items-center justify-between mb-4">
            <h3 class="text-lg font-semibold text-gray-900">{{ $t('dashboard.health.title') }}</h3>
            <button 
                @click="fetchHealth" 
                :disabled="healthLoading"
                class="text-sm text-blue-600 hover:text-blue-800 flex items-center">
                <svg v-if="healthLoading" class="animate-spin -ml-1 mr-2 h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                <svg v-else class="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                </svg>
                {{ $t('dashboard.health.refresh') }}
            </button>
        </div>
        
        <div v-if="healthError" class="text-sm text-red-600 mb-4">
            {{ healthError }}
        </div>

        <div v-if="healthData" class="space-y-4">
            <!-- Overall Status -->
            <div class="flex items-center p-4 rounded-lg" :class="healthData.status === 'Healthy' ? 'bg-green-50' : 'bg-red-50'">
                <div class="flex-shrink-0">
                    <svg v-if="healthData.status === 'Healthy'" class="h-8 w-8 text-green-400" fill="currentColor" viewBox="0 0 20 20">
                        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
                    </svg>
                    <svg v-else class="h-8 w-8 text-red-400" fill="currentColor" viewBox="0 0 20 20">
                         <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
                    </svg>
                </div>
                <div class="ml-3">
                    <h3 class="text-lg font-medium" :class="healthData.status === 'Healthy' ? 'text-green-800' : 'text-red-800'">
                        {{ healthData.status }}
                    </h3>
                    <div class="text-sm" :class="healthData.status === 'Healthy' ? 'text-green-700' : 'text-red-700'">
                        {{ t('dashboard.health.lastUpdated', { time: new Date().toLocaleTimeString() }) }}
                    </div>
                </div>
            </div>

            <!-- Individual Checks -->
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                <div v-for="(entry, name) in healthData.entries" :key="name" class="border rounded-md p-3 flex items-center justify-between">
                    <span class="font-medium text-gray-700">{{ name }}</span>
                    <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
                        :class="entry.status === 'Healthy' ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'">
                        {{ entry.status }}
                    </span>
                </div>
            </div>
        </div>
        <div v-else-if="!healthLoading" class="text-gray-500 text-sm italic">
            {{ t('dashboard.health.noData') }}
        </div>
      </div>

      <!-- Monitoring Cards -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Activity Dashboard Card -->
        <div class="bg-white rounded-lg shadow-sm p-6">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('monitoring.activityDashboard.title') }}</h3>
          <ActivityDashboard />
        </div>

        <!-- Security Metrics Card -->
        <div class="bg-white rounded-lg shadow-sm p-6">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('monitoring.securityMetrics.title') }}</h3>
          <SecurityMetrics />
        </div>
      </div>

      <!-- Real-time Alerts Card -->
      <div class="mt-6 bg-white rounded-lg shadow-sm p-6">
        <h3 class="text-lg font-semibold text-gray-900 mb-4">{{ $t('monitoring.realTimeAlerts.title') }}</h3>
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
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('dashboard.cards.clients.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('dashboard.cards.clients.description') }}</p>
          <a href="/Admin/Clients" class="inline-flex items-center text-indigo-600 hover:text-indigo-700 font-medium text-sm mt-auto">
            {{ $t('dashboard.cards.clients.action') }}
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
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('dashboard.cards.scopes.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('dashboard.cards.scopes.description') }}</p>
          <a href="/Admin/Scopes" class="inline-flex items-center text-green-600 hover:text-green-700 font-medium text-sm mt-auto">
            {{ $t('dashboard.cards.scopes.action') }}
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
          <h2 class="text-xl font-semibold text-gray-900 mb-2">{{ $t('dashboard.cards.users.title') }}</h2>
          <p class="text-sm text-gray-600 mb-4">{{ $t('dashboard.cards.users.description') }}</p>
          <a href="/Admin/Users" class="inline-flex items-center text-green-600 hover:text-green-700 font-medium text-sm mt-auto">
            {{ $t('dashboard.cards.users.action') }}
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
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const { t } = useI18n()

const loading = ref(true)
const error = ref(null)
const stats = ref({
  totalClients: 0,
  totalScopes: 0,
  totalUsers: 0
})

const healthData = ref(null)
const healthLoading = ref(false)
const healthError = ref(null)

const hubConnection = ref(null)

const fetchStats = async () => {
  try {
    loading.value = true
    error.value = null
    
    const response = await fetch('/api/admin/dashboard/stats')
    
    if (!response.ok) {
      if (response.status === 401) {
        throw new Error(t('dashboard.errors.unauthorized'))
      } else if (response.status === 403) {
        throw new Error(t('dashboard.errors.accessDenied'))
      } else {
        throw new Error(`${t('dashboard.errors.loadFailed')} (${response.status})`)
      }
    }
    
    const data = await response.json()
    stats.value = data
  } catch (err) {
    console.error('Dashboard stats error:', err)
    error.value = err.message || t('dashboard.errors.loadFailed')
  } finally {
    loading.value = false
  }
}

const fetchHealth = async () => {
    healthLoading.value = true
    healthError.value = null
    try {
        const response = await fetch('/health')
        if (!response.ok) throw new Error('Failed to fetch health status')
        healthData.value = await response.json()
    } catch (err) {
        console.error('Health check failed:', err)
        healthError.value = t('dashboard.errors.healthFailed')
    } finally {
        healthLoading.value = false
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
  fetchHealth() // Don't await, let it load in background
  await setupSignalR()
})

onUnmounted(() => {
  cleanupSignalR()
})
</script>
