<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import ActivityDashboard from './components/ActivityDashboard.vue'
import SecurityMetrics from './components/SecurityMetrics.vue'
import RealTimeAlerts from './components/RealTimeAlerts.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import PageHeader from '@/components/common/PageHeader.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()

const loading = ref(true)
const error = ref(null)
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')

// Permissions
const canRead = ref(false)

// Load permissions on mount
onMounted(async () => {
  await permissionService.loadPermissions()
  canRead.value = permissionService.hasPermission(Permissions.Monitoring.Read)

  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.monitoring.noPermission')
    deniedPermission.value = Permissions.Monitoring.Read
    return
  }

  loading.value = false
})
</script>

<template>
  <div class="monitoring-app">
    <PageHeader
      :title="t('admin.monitoring.title')"
      :subtitle="t('admin.monitoring.subtitle')"
    />

    <div v-if="loading" class="text-center py-8">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">{{ t('admin.monitoring.loading') }}</span>
      </div>
      <p class="mt-2">{{ t('admin.monitoring.loading') }}</p>
    </div>

    <div v-else-if="error" class="alert alert-danger">
      {{ error }}
    </div>

    <div v-else class="monitoring-content">
      <ActivityDashboard />
      <SecurityMetrics />
      <RealTimeAlerts />
    </div>

    <AccessDeniedDialog
      v-model="showAccessDenied"
      :message="deniedMessage"
      :permission="deniedPermission"
    />
  </div>
</template>

<style scoped>
.monitoring-app {
  @apply space-y-6;
}

.monitoring-content {
  @apply space-y-6;
}
</style>