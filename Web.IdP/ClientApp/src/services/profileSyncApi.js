import { adminApi } from '@/utils/adminFetch'

async function read(response) {
  if (!response.ok || response.redirected) {
    const data = await response.json().catch(() => ({}))
    const error = new Error(response.redirected || response.status === 401 ? 'session'
      : response.status === 403 ? 'forbidden' : data.error || 'request_failed')
    throw error
  }
  return response.json()
}
const base = '/api/admin/profile-sync'
export const profileSyncApi = {
  searchAccounts: (search, skip = 0) => adminApi.get(`/api/admin/users?${new URLSearchParams({ search, skip: String(skip), take: '10' })}`, { cache: 'no-store' }).then(read),
  status: () => adminApi.get(`${base}/status`, { cache: 'no-store' }).then(read),
  accounts: personId => adminApi.get(`/api/admin/people/${encodeURIComponent(personId)}/accounts`, { cache: 'no-store' }).then(read),
  preview: userId => adminApi.post(`${base}/users/${encodeURIComponent(userId)}/preview`).then(read),
  apply: (userId, previewToken) => adminApi.post(`${base}/users/${encodeURIComponent(userId)}/apply`, { previewToken }).then(read)
}
