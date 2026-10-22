<script setup>
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import permissionService, { Permissions } from '@/utils/permissionService'
import PageHeader from '@/components/common/PageHeader.vue'
import LoadingPlaceholder from '@/components/common/LoadingPlaceholder.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import ExternalDataSources from '../settings/components/ExternalDataSources.vue'
import NativeFieldSources from '../settings/components/NativeFieldSources.vue'

const { t } = useI18n()
const active = ref('sources'), loading = ref(true), canRead = ref(false), canUpdate = ref(false)
const areas = ['sources', 'mappings', 'manual']
onMounted(async () => {
  try {
    await permissionService.loadPermissions()
    canRead.value = permissionService.isAdmin && permissionService.hasPermission(Permissions.Settings.Read)
    canUpdate.value = permissionService.hasPermission(Permissions.Settings.Update)
  } finally { loading.value = false }
})
</script>

<template>
  <div class="min-h-screen bg-gray-50 py-6">
    <div class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
      <PageHeader :title="t('data-sources.title')" :subtitle="t('data-sources.subtitle')" />
      <LoadingPlaceholder v-if="loading" :message="t('native-fields.loading')" />
      <AccessDeniedDialog v-else-if="!canRead" :show="true" />
      <template v-else>
        <nav :aria-label="t('data-sources.title')" class="mb-6 grid grid-cols-3 gap-2 border-b border-gray-200 pb-3">
          <button v-for="area in areas" :key="area" :data-test="`area-${area}`" type="button" :aria-current="active === area ? 'page' : undefined" class="min-h-10 rounded-md px-3 py-2 text-sm font-medium focus:ring-2 focus:ring-google-500" :class="active === area ? 'bg-google-100 text-google-700' : 'text-gray-600 hover:bg-gray-100'" @click="active = area">{{ t(`data-sources.${area}`) }}</button>
        </nav>
        <div :data-test="`panel-${active}`">
          <ExternalDataSources v-if="active === 'sources'" :can-update="canUpdate" />
          <NativeFieldSources v-else :key="active" :mode="active" :can-update="canUpdate" />
        </div>
      </template>
    </div>
  </div>
</template>
