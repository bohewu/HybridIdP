<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import BaseModal from '@/components/common/BaseModal.vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const { t } = useI18n()

const props = defineProps({
  user: { type: Object, required: true },
  canUpdate: { type: Boolean, default: false }
})

const emit = defineEmits(['close'])

const sessions = ref([])
const page = ref(1)
const pageSize = ref(10)
const pageSizeOptions = [5, 10, 20, 50]
const total = ref(0)
const pages = ref(1)
const loading = ref(true)
const error = ref('')
const revoking = ref(false)

const fetchSessions = async () => {
  loading.value = true
  error.value = ''
  try {
    const response = await fetch(`/api/admin/users/${props.user.id}/sessions?page=${page.value}&pageSize=${pageSize.value}`)
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }
    const data = await response.json()
    // Support both legacy array and new paginated shape
    if (Array.isArray(data)) {
      sessions.value = data
      total.value = data.length
      pages.value = 1
    } else {
      sessions.value = data.items || []
      page.value = data.page || 1
      pageSize.value = data.pageSize || sessions.value.length || 10
      total.value = data.total || sessions.value.length
      pages.value = data.pages || 1
    }
  } catch (e) {
    error.value = t('sessions.errors.loadFailed')
    console.error('Error loading sessions:', e)
  } finally {
    loading.value = false
  }
}

const revoke = async (authorizationId) => {
  if (!props.canUpdate) return
  if (!confirm(t('sessions.confirm.revokeOne'))) return
  revoking.value = true
  try {
    const response = await fetch(`/api/admin/users/${props.user.id}/sessions/${authorizationId}/revoke`, { method: 'POST' })
    if (!response.ok && response.status !== 204) {
      throw new Error(`HTTP ${response.status}`)
    }
    await fetchSessions()
  } catch (e) {
    alert(t('sessions.errors.revokeFailed'))
    console.error('Error revoking session:', e)
  } finally {
    revoking.value = false
  }
}

const revokeAll = async () => {
  if (!props.canUpdate) return
  if (!confirm(t('sessions.confirm.revokeAll'))) return
  revoking.value = true
  try {
    const response = await fetch(`/api/admin/users/${props.user.id}/sessions/revoke-all`, { method: 'POST' })
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }
    const data = await response.json().catch(() => null)
    await fetchSessions()
    alert(t('sessions.alerts.revokedAll', { count: data?.revoked ?? 0 }))
  } catch (e) {
    alert(t('sessions.errors.revokeAllFailed'))
    console.error('Error revoking all sessions:', e)
  } finally {
    revoking.value = false
  }
}

const nextPage = () => {
  if (page.value < pages.value) {
    page.value++
    fetchSessions()
  }
}

const prevPage = () => {
  if (page.value > 1) {
    page.value--
    fetchSessions()
  }
}

const handleClose = () => {
  if (revoking.value) return
  emit('close')
}

const formatDate = (dt) => {
  if (!dt) return t('sessions.never')
  return new Date(dt).toLocaleString()
}

onMounted(() => fetchSessions())
</script>

<template>
  <BaseModal
    :show="true"
    :title="t('sessions.title')"
    size="3xl"
    :loading="revoking"
    :close-on-backdrop="false"
    @close="handleClose"
  >
    <template #body>
      <div class="mb-3 p-3 bg-gray-50 rounded-md">
        <p class="text-sm text-gray-700">
          <span class="font-medium">{{ t('sessions.userLabel') }}:</span> {{ user.email }}
        </p>
      </div>
            <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-3">
              <p class="text-sm text-red-700">{{ error }}</p>
            </div>
            <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('sessions.loading')" />
            <div v-else-if="sessions.length === 0" class="py-8 text-center">
              <p class="text-sm text-gray-600">{{ t('sessions.empty') }}</p>
            </div>
            <div v-else class="overflow-x-auto max-h-[60vh] border rounded-md">
              <table class="min-w-full divide-y divide-gray-200">
                <thead class="bg-gray-50">
                  <tr>
                    <th class="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.authorizationId') }}</th>
                    <th class="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.client') }}</th>
                    <th class="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.created') }}</th>
                    <th class="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.expires') }}</th>
                    <th class="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.status') }}</th>
                    <th class="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{{ t('sessions.headers.actions') }}</th>
                  </tr>
                </thead>
                <tbody class="bg-white divide-y divide-gray-200">
                  <tr v-for="s in sessions" :key="s.authorizationId" class="hover:bg-gray-50">
                    <td class="px-4 py-2 text-sm text-gray-900 font-mono">{{ s.authorizationId }}</td>
                    <td class="px-4 py-2 text-sm text-gray-700">
                      <span v-if="s.clientDisplayName">{{ s.clientDisplayName }}</span>
                      <span v-else class="text-gray-400 italic">{{ t('sessions.noClient') }}</span>
                    </td>
                    <td class="px-4 py-2 text-sm text-gray-500">{{ formatDate(s.createdAt) }}</td>
                    <td class="px-4 py-2 text-sm text-gray-500">{{ formatDate(s.expiresAt) }}</td>
                    <td class="px-4 py-2 text-sm">
                      <span :class="['inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium', s.status === 'revoked' ? 'bg-red-100 text-red-800' : 'bg-green-100 text-green-800']">
                        {{ s.status || t('sessions.status.active') }}
                      </span>
                    </td>
                    <td class="px-4 py-2 text-sm text-right">
                      <button
                        v-if="canUpdate && s.status !== 'revoked'"
                        @click="revoke(s.authorizationId)"
                        :disabled="revoking"
                        class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-xs font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50"
                      >
                        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
                        </svg>
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
              <div class="flex items-center justify-between px-4 py-3 bg-gray-50 border-t text-sm" v-if="pages > 0">
                <div class="flex flex-col sm:flex-row sm:items-center gap-2 text-gray-600">
                  <span>{{ t('sessions.pagination.pageInfo', { page, pages }) }}</span>
                  <span class="text-gray-500">{{ t('sessions.pagination.total', { total }) }}</span>
                  <label class="flex items-center gap-1">
                    <span class="text-gray-500">{{ t('sessions.pagination.pageSize') }}</span>
                    <select v-model.number="pageSize" @change="page=1; fetchSessions()" class="border rounded px-2 py-1 text-xs">
                      <option v-for="opt in pageSizeOptions" :key="opt" :value="opt">{{ opt }}</option>
                    </select>
                  </label>
                </div>
                <div class="flex items-center gap-2">
                  <button @click="prevPage" :disabled="page === 1 || revoking" class="px-3 py-1.5 rounded-md border text-xs font-medium"
                    :class="page === 1 ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'">
                    {{ t('sessions.pagination.previous') }}
                  </button>
                  <button @click="nextPage" :disabled="page === pages || revoking" class="px-3 py-1.5 rounded-md border text-xs font-medium"
                    :class="page === pages ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'">
                    {{ t('sessions.pagination.next') }}
                  </button>
                </div>
              </div>
            </div>
    </template>

    <template #footer>
      <div class="flex gap-2 w-full sm:w-auto sm:flex-row-reverse">
        <button
          type="button"
          v-if="sessions.length > 0 && canUpdate"
          @click="revokeAll"
          :disabled="revoking"
          class="inline-flex justify-center rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-red-500 disabled:opacity-50"
        >
          {{ revoking ? t('sessions.buttons.revoking') : t('sessions.buttons.revokeAll') }}
        </button>
        <button
          type="button"
          @click="handleClose"
          :disabled="revoking"
          class="inline-flex justify-center rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 disabled:opacity-50"
        >
          {{ t('common.buttons.close') }}
        </button>
      </div>
    </template>
  </BaseModal>
</template>
