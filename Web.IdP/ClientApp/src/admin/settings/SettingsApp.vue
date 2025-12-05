<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import permissionService, { Permissions } from '@/utils/permissionService'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import BrandingSettings from './components/BrandingSettings.vue'
import EmailSettings from './components/EmailSettings.vue'
import PageHeader from '@/components/common/PageHeader.vue'
const { t } = useI18n()

const hasReadPermission = ref(false)
const hasUpdatePermission = ref(false)
const loading = ref(true)
const error = ref(null)

onMounted(async () => {
  try {
    await permissionService.loadPermissions()
    hasReadPermission.value = permissionService.hasPermission(Permissions.Settings.Read)
    hasUpdatePermission.value = permissionService.hasPermission(Permissions.Settings.Update)
  } catch (err) {
    console.error('Failed to load permissions:', err)
    error.value = t('admin.settings.loadingError', { message: err.message })
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="min-h-screen bg-gray-50 py-6">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8"
         v-loading="{ loading: loading, overlay: true, message: t('admin.settings.loading') }">
      <!-- Header -->
    <PageHeader 
      :title="$t('admin.settings.pageTitle')" 
      :subtitle="$t('admin.settings.pageSubtitle')"
    >
    </PageHeader>
      

      <!-- Error State -->
      <div v-if="error" class="bg-red-50 border border-red-200 rounded-lg p-4">
        <div class="flex">
          <div class="flex-shrink-0">
            <svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
            </svg>
          </div>
          <div class="ml-3">
            <p class="text-sm text-red-700">{{ error }}</p>
          </div>
        </div>
      </div>

      <!-- Access Denied -->
      <AccessDeniedDialog v-if="!loading && !hasReadPermission" />

      <!-- Settings Sections -->
      <div v-if="!loading && !error && hasReadPermission" class="space-y-6">
        <!-- Branding Settings -->
        <BrandingSettings :can-update="hasUpdatePermission" />

        <!-- Email Settings -->
        <EmailSettings :can-update="hasUpdatePermission" />
      </div>
    </div>
  </div>
</template>
