<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import permissionService from '@/services/permissionService'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import BrandingSettings from './components/BrandingSettings.vue'

const { t } = useI18n()

const hasReadPermission = ref(false)
const hasUpdatePermission = ref(false)
const loading = ref(true)
const error = ref(null)

onMounted(async () => {
  try {
    const permissions = await permissionService.loadPermissions()
    hasReadPermission.value = permissions.includes('Permissions.Settings.Read')
    hasUpdatePermission.value = permissions.includes('Permissions.Settings.Update')
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
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-3xl font-bold text-gray-900">
          {{ t('admin.settings.pageTitle') }}
        </h1>
        <p class="mt-2 text-sm text-gray-600">
          {{ t('admin.settings.pageSubtitle') }}
        </p>
      </div>

      <!-- Loading State -->
      <div v-if="loading" class="flex justify-center items-center py-12">
        <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
        <span class="ml-3 text-gray-600">{{ t('admin.settings.loading') }}</span>
      </div>

      <!-- Error State -->
      <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg p-4">
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
      <AccessDeniedDialog v-else-if="!hasReadPermission" />

      <!-- Settings Sections -->
      <div v-else class="space-y-6">
        <!-- Branding Settings -->
        <BrandingSettings :can-update="hasUpdatePermission" />

        <!-- Future sections placeholder -->
        <div class="bg-white shadow-sm rounded-lg border border-gray-200 p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-2">
            Security Settings
          </h2>
          <p class="text-sm text-gray-500">
            Coming soon: Password policy, MFA settings, session configuration
          </p>
        </div>

        <div class="bg-white shadow-sm rounded-lg border border-gray-200 p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-2">
            Email Settings
          </h2>
          <p class="text-sm text-gray-500">
            Coming soon: SMTP configuration, email templates
          </p>
        </div>
      </div>
    </div>
  </div>
</template>
