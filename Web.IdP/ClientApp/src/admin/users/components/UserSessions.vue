<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps({
  user: { type: Object, required: true },
  canUpdate: { type: Boolean, default: false }
})

const emit = defineEmits(['close'])

const sessions = ref([])
const loading = ref(true)
const error = ref('')
const revoking = ref(false)

const fetchSessions = async () => {
  loading.value = true
  error.value = ''
  try {
    const response = await fetch(`/api/admin/users/${props.user.id}/sessions`)
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }
    sessions.value = await response.json()
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
  <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity z-50" @click.self="handleClose">
    <div class="fixed inset-0 z-50 overflow-y-auto">
      <div class="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
        <div class="relative transform rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 w-full sm:max-w-3xl">
          <div class="bg-white px-4 pb-4 pt-5 sm:p-6 sm:pb-4">
            <h3 class="text-lg font-semibold leading-6 text-gray-900 mb-4">
              {{ t('sessions.title') }}
            </h3>
            <div class="mb-3 p-3 bg-gray-50 rounded-md">
              <p class="text-sm text-gray-700">
                <span class="font-medium">{{ t('sessions.userLabel') }}:</span> {{ user.email }}
              </p>
            </div>
            <div v-if="error" class="mb-4 bg-red-50 border-l-4 border-red-400 p-3">
              <p class="text-sm text-red-700">{{ error }}</p>
            </div>
            <div v-if="loading" class="flex flex-col items-center justify-center py-10">
              <svg class="animate-spin h-8 w-8 text-indigo-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
              </svg>
              <p class="mt-2 text-sm text-gray-600">{{ t('sessions.loading') }}</p>
            </div>
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
            </div>
          </div>
          <div class="bg-gray-50 px-4 py-2.5 sm:flex sm:flex-row-reverse sm:px-6">
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
                {{ t('buttons.close') }}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
