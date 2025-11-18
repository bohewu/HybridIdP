<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import AuditLogViewer from './components/AuditLogViewer.vue'
import AuditLogFilters from './components/AuditLogFilters.vue'
import AuditLogExport from './components/AuditLogExport.vue'
import AccessDeniedDialog from '@/components/AccessDeniedDialog.vue'
import permissionService, { Permissions } from '@/utils/permissionService'

const { t } = useI18n()

const auditEvents = ref([])
const loading = ref(true)
const error = ref(null)
const showAccessDenied = ref(false)
const deniedMessage = ref('')
const deniedPermission = ref('')

// Permissions
const canRead = ref(false)

// Filters and pagination
const filters = ref({
  startDate: '',
  endDate: '',
  userId: '',
  eventType: '',
  ipAddress: '',
  search: ''
})
const page = ref(1)
const pageSize = ref(25)
const totalCount = ref(0)
const sort = ref('timestamp:desc')

// Load permissions on mount
onMounted(async () => {
  await permissionService.loadPermissions()
  canRead.value = permissionService.hasPermission(Permissions.Audit.Read)

  if (!canRead.value) {
    showAccessDenied.value = true
    deniedMessage.value = t('admin.audit.noPermission')
    deniedPermission.value = Permissions.Audit.Read
    return
  }

  fetchAuditEvents()
})

const fetchAuditEvents = async () => {
  loading.value = true
  error.value = null

  try {
    const params = new URLSearchParams({
      page: page.value.toString(),
      pageSize: pageSize.value.toString(),
      sort: sort.value,
      ...Object.fromEntries(
        Object.entries(filters.value).filter(([_, v]) => v !== '')
      )
    })

    const response = await fetch(`/api/admin/audit/events?${params}`)
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }

    const data = await response.json()
    auditEvents.value = data.items || []
    totalCount.value = data.totalCount || 0
  } catch (err) {
    error.value = err.message
    console.error('Failed to fetch audit events:', err)
  } finally {
    loading.value = false
  }
}

const handleFiltersChange = (newFilters) => {
  filters.value = { ...newFilters }
  page.value = 1 // Reset to first page
  fetchAuditEvents()
}

const handlePageChange = (newPage) => {
  page.value = newPage
  fetchAuditEvents()
}

const handlePageSizeChange = (newPageSize) => {
  pageSize.value = newPageSize
  page.value = 1
  fetchAuditEvents()
}

const handleRefresh = () => {
  fetchAuditEvents()
}

const formatDate = (dateString) => {
  if (!dateString) return t('audit.never')
  return new Date(dateString).toLocaleString()
}
  if (auditEvents.value.length === 0) {
    alert(t('admin.audit.export.noData'))
    return
  }

  if (format === 'csv') {
    exportToCsv()
  } else if (format === 'excel') {
    exportToExcel()
  }
}

const exportToCsv = () => {
  const headers = [
    t('tableHeaders.timestamp'),
    t('tableHeaders.eventType'),
    t('tableHeaders.user'),
    t('tableHeaders.details'),
    t('tableHeaders.ipAddress')
  ]

  const rows = auditEvents.value.map(event => [
    formatDate(event.timestamp),
    event.eventType,
    event.user || t('audit.system'),
    event.details,
    event.ipAddress || t('audit.unknown')
  ])

  const csvContent = [headers, ...rows]
    .map(row => row.map(field => `"${field}"`).join(','))
    .join('\n')

  downloadFile(csvContent, 'audit-events.csv', 'text/csv')
}

const exportToExcel = () => {
  // For now, export as CSV since Excel requires additional libraries
  // In production, would use a library like xlsx or sheetjs
  exportToCsv()
}

const downloadFile = (content, filename, mimeType) => {
  const blob = new Blob([content], { type: mimeType })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}
</script>

<template>
  <div class="audit-app">
    <div class="mb-6">
      <h1 class="text-2xl font-bold text-gray-900">{{ t('admin.audit.title') }}</h1>
      <p class="mt-1 text-sm text-gray-600">{{ t('admin.audit.description') }}</p>
    </div>

    <AccessDeniedDialog
      v-if="showAccessDenied"
      :message="deniedMessage"
      :permission="deniedPermission"
      @close="showAccessDenied = false"
    />

    <div v-else>
      <AuditLogFilters
        :filters="filters"
        @filters-change="handleFiltersChange"
      />

      <AuditLogViewer
        :audit-events="auditEvents"
        :loading="loading"
        :error="error"
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :sort="sort"
        @page-change="handlePageChange"
        @page-size-change="handlePageSizeChange"
        @sort-change="handleSortChange"
        @refresh="handleRefresh"
      />

      <AuditLogExport
        @export="handleExport"
      />
    </div>
  </div>
</template>