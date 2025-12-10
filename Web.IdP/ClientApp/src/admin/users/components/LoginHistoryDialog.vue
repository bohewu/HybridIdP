<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const { t } = useI18n()

const props = defineProps({
  user: { type: Object, required: true },
  canUpdate: { type: Boolean, default: false }
})

const emit = defineEmits(['close'])

const loginHistory = ref([])
const page = ref(1)
const pageSize = ref(10)
const pageSizeOptions = [5, 10, 20, 50]
const total = ref(0)
const pages = ref(1)
const loading = ref(true)
const error = ref('')
const approving = ref(false)
const showAbnormalOnly = ref(false)

const filteredHistory = computed(() => {
  if (!showAbnormalOnly.value) {
    return loginHistory.value
  }
  return loginHistory.value.filter(h => h.isFlaggedAbnormal && !h.isApprovedByAdmin)
})

const fetchLoginHistory = async () => {
  loading.value = true
  error.value = ''
  try {
    const count = pageSize.value * page.value // Fetch more records for client-side filtering
    const response = await fetch(`/api/admin/users/${props.user.id}/login-history?count=${count}`)
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }
    const data = await response.json()
    loginHistory.value = Array.isArray(data) ? data : []
    total.value = loginHistory.value.length
    pages.value = Math.ceil(total.value / pageSize.value) || 1
  } catch (e) {
    error.value = t('users.loginHistory.errors.loadFailed')
    console.error('Error loading login history:', e)
  } finally {
    loading.value = false
  }
}

const approveLogin = async (loginHistoryId, ipAddress) => {
  if (!props.canUpdate) return
  if (!confirm(t('users.loginHistory.confirm.approve', { ip: ipAddress }))) return
  
  approving.value = true
  try {
    const response = await fetch(
      `/api/admin/users/${props.user.id}/login-history/${loginHistoryId}/approve`,
      { method: 'POST' }
    )
    if (!response.ok && response.status !== 204) {
      throw new Error(`HTTP ${response.status}`)
    }
    alert(t('users.loginHistory.alerts.approveSuccess'))
    await fetchLoginHistory()
  } catch (e) {
    alert(t('users.loginHistory.alerts.approveFailed', { message: e.message }))
    console.error('Error approving login:', e)
  } finally {
    approving.value = false
  }
}

const nextPage = () => {
  if (page.value < pages.value) {
    page.value++
  }
}

const prevPage = () => {
  if (page.value > 1) {
    page.value--
  }
}

const handleClose = () => {
  if (approving.value) return
  emit('close')
}

const formatDate = (dt) => {
  if (!dt) return '-'
  // Append 'Z' if no timezone info to ensure UTC interpretation
  const isoDate = dt.endsWith('Z') || dt.includes('+') ? dt : dt + 'Z'
  return new Date(isoDate).toLocaleString()
}

const getStatusBadge = (login) => {
  if (!login.isSuccessful) {
    return { class: 'bg-red-100 text-red-800', label: t('users.loginHistory.status.failed') }
  }
  if (login.isFlaggedAbnormal && !login.isApprovedByAdmin) {
    return { class: 'bg-orange-100 text-orange-800', label: t('users.loginHistory.status.abnormal') }
  }
  if (login.isApprovedByAdmin) {
    return { class: 'bg-green-100 text-green-800', label: t('users.loginHistory.status.approved') }
  }
  return { class: 'bg-blue-100 text-blue-800', label: t('users.loginHistory.status.success') }
}

const paginatedHistory = computed(() => {
  const start = (page.value - 1) * pageSize.value
  const end = start + pageSize.value
  return filteredHistory.value.slice(start, end)
})

onMounted(() => fetchLoginHistory())
</script>

<template>
  <BaseModal
    :show="true"
    :title="t('users.loginHistory.title')"
    size="5xl"
    :loading="approving"
    :close-on-backdrop="false"
    @close="handleClose"
  >
    <template #body>
      <div class="text-sm text-gray-500 mb-4">
        <span class="font-medium">{{ t('users.loginHistory.userLabel') }}:</span> {{ user.email }}
      </div>

                <!-- Filter: Show Abnormal Only -->
                <div class="mt-4 flex items-center">
                  <label class="flex items-center cursor-pointer">
                    <input 
                      type="checkbox" 
                      v-model="showAbnormalOnly"
                      class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-600"
                    >
                    <span class="ml-2 text-sm text-gray-700">
                      {{ t('users.loginHistory.showAbnormalOnly') }}
                    </span>
                  </label>
                </div>

                <!-- Error State -->
                <div v-if="error" class="mt-4 bg-red-50 border-l-4 border-red-400 p-4" role="alert">
                  <p class="text-sm text-red-700">{{ error }}</p>
                </div>

                <!-- Loading State -->
                <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('users.loginHistory.loading')" />

                <!-- Empty State -->
                <div v-else-if="!loading && filteredHistory.length === 0" class="mt-4 text-center py-8">
                  <p class="text-sm text-gray-500">{{ t('users.loginHistory.empty') }}</p>
                </div>

                <!-- Table -->
                <div v-else class="mt-4 overflow-x-auto">
                  <table class="min-w-full divide-y divide-gray-200">
                    <thead class="bg-gray-50">
                      <tr>
                        <th scope="col" class="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.loginTime') }}
                        </th>
                        <th scope="col" class="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.ipAddress') }}
                        </th>
                        <th scope="col" class="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.userAgent') }}
                        </th>
                        <th scope="col" class="px-3 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.riskScore') }}
                        </th>
                        <th scope="col" class="px-3 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.status') }}
                        </th>
                        <th scope="col" class="px-3 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                          {{ t('users.loginHistory.tableHeaders.actions') }}
                        </th>
                      </tr>
                    </thead>
                    <tbody class="bg-white divide-y divide-gray-200">
                      <tr v-for="login in paginatedHistory" :key="login.id" class="hover:bg-gray-50">
                        <td class="px-3 py-4 whitespace-nowrap text-sm text-gray-900">
                          {{ formatDate(login.loginTime) }}
                        </td>
                        <td class="px-3 py-4 whitespace-nowrap text-sm text-gray-700">
                          <div class="flex items-center">
                            <!-- Warning icon for abnormal logins -->
                            <svg 
                              v-if="login.isFlaggedAbnormal && !login.isApprovedByAdmin"
                              class="h-5 w-5 text-orange-500 mr-2" 
                              fill="currentColor" 
                              viewBox="0 0 20 20"
                            >
                              <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd" />
                            </svg>
                            <span>{{ login.ipAddress || 'N/A' }}</span>
                          </div>
                        </td>
                        <td class="px-3 py-4 text-sm text-gray-700 max-w-xs truncate" :title="login.userAgent">
                          {{ login.userAgent || 'N/A' }}
                        </td>
                        <td class="px-3 py-4 whitespace-nowrap text-center text-sm">
                          <span 
                            class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
                            :class="{
                              'bg-red-100 text-red-800': login.riskScore >= 70,
                              'bg-yellow-100 text-yellow-800': login.riskScore >= 40 && login.riskScore < 70,
                              'bg-green-100 text-green-800': login.riskScore < 40
                            }"
                          >
                            {{ login.riskScore }}
                          </span>
                        </td>
                        <td class="px-3 py-4 whitespace-nowrap text-center">
                          <span 
                            class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
                            :class="getStatusBadge(login).class"
                          >
                            {{ getStatusBadge(login).label }}
                          </span>
                        </td>
                        <td class="px-3 py-4 whitespace-nowrap text-center text-sm">
                          <button
                            v-if="canUpdate && login.isFlaggedAbnormal && !login.isApprovedByAdmin"
                            @click="approveLogin(login.id, login.ipAddress)"
                            :disabled="approving"
                            class="inline-flex items-center px-3 py-1.5 border border-green-300 text-green-700 text-sm font-medium rounded-md hover:bg-green-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            <svg class="h-4 w-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                            </svg>
                            {{ approving ? t('users.loginHistory.buttons.approving') : t('users.loginHistory.buttons.approve') }}
                          </button>
                          <span v-else class="text-xs text-gray-400">-</span>
                        </td>
                      </tr>
                    </tbody>
                  </table>

                  <!-- Pagination -->
                  <div v-if="filteredHistory.length > pageSize" class="bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6">
                    <div class="flex-1 flex justify-between sm:hidden">
                      <button
                        @click="prevPage"
                        :disabled="page === 1"
                        class="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        {{ t('users.loginHistory.pagination.previous') }}
                      </button>
                      <button
                        @click="nextPage"
                        :disabled="page === pages"
                        class="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        {{ t('users.loginHistory.pagination.next') }}
                      </button>
                    </div>
                    <div class="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                      <div>
                        <p class="text-sm text-gray-700">
                          {{ t('users.loginHistory.pagination.pageInfo', { page, pages }) }}
                          <span class="font-medium ml-2">
                            {{ t('users.loginHistory.pagination.total', { total: filteredHistory.length }) }}
                          </span>
                        </p>
                      </div>
                      <div class="flex items-center gap-2">
                        <select
                          v-model="pageSize"
                          @change="page = 1"
                          class="block rounded-md border-gray-300 text-sm focus:border-indigo-500 focus:ring-indigo-500"
                        >
                          <option v-for="size in pageSizeOptions" :key="size" :value="size">
                            {{ size }} {{ t('users.loginHistory.pagination.pageSize') }}
                          </option>
                        </select>
                        <nav class="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
                          <button
                            @click="prevPage"
                            :disabled="page === 1"
                            class="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            <span class="sr-only">{{ t('users.loginHistory.pagination.previous') }}</span>
                            <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                              <path fill-rule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clip-rule="evenodd" />
                            </svg>
                          </button>
                          <button
                            @click="nextPage"
                            :disabled="page === pages"
                            class="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            <span class="sr-only">{{ t('users.loginHistory.pagination.next') }}</span>
                            <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                              <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
                            </svg>
                          </button>
                        </nav>
                      </div>
                    </div>
                  </div>
                </div>
    </template>

    <template #footer>
      <button
        type="button"
        @click="handleClose"
        :disabled="approving"
        class="inline-flex w-full justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {{ t('users.loginHistory.buttons.close') }}
      </button>
    </template>
  </BaseModal>
</template>
