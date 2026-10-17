<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import ActivityDashboard from './components/ActivityDashboard.vue'
import SecurityMetrics from './components/SecurityMetrics.vue'
import RealTimeAlerts from './components/RealTimeAlerts.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import PageHeader from '@/components/common/PageHeader.vue'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
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
  try {
    await permissionService.loadPermissions()
    canRead.value = permissionService.hasPermission(Permissions.Monitoring.Read)
    if (!canRead.value) {
      showAccessDenied.value = true
      deniedMessage.value = t('monitoring.noPermission')
      deniedPermission.value = Permissions.Monitoring.Read
    }
  } catch (err) {
    error.value = t('monitoring.loadFailed')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      :title="t('monitoring.title')"
      :subtitle="t('monitoring.subtitle')"
    />

    <LoadingPlaceholder v-if="loading" :message="t('monitoring.loading')" />
    <div v-if="error" role="alert" class="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">
      {{ error }}
    </div>

    <div v-if="!loading && !error && canRead" class="space-y-6">
      <ActivityDashboard />
      <SecurityMetrics />
      <RealTimeAlerts />
    </div>

    <AccessDeniedDialog
      v-model="showAccessDenied"
      :message="deniedMessage"
      :permission="deniedPermission"
      :show="showAccessDenied"
    />
  </div>
</template>

<style scoped>
/* Scoped styles removed as they only contained @apply rules now moved to template */
</style>