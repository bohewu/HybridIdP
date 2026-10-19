import { adminApi } from '@/utils/adminFetch'

const base = '/api/admin/claims-preview'

async function read(response) {
  if (!response.ok || response.redirected) {
    const data = await response.json().catch(() => ({}))
    throw new Error(response.status === 403 ? 'forbidden' : response.status === 401 || response.redirected ? 'session' : data.code || 'request_failed')
  }
  return response.json()
}

export const claimsPreviewApi = {
  clients: (skip = 0) => adminApi.get(`${base}/clients?${new URLSearchParams({ skip, take: 50 })}`, { cache: 'no-store' }).then(read),
  accounts: (search = '', skip = 0) => adminApi.get(`${base}/accounts?${new URLSearchParams({ search, skip, take: 50 })}`, { cache: 'no-store' }).then(read),
  scopes: clientId => adminApi.get(`${base}/scopes?${new URLSearchParams({ clientId })}`, { cache: 'no-store' }).then(read),
  preview: request => adminApi.post(base, request, { cache: 'no-store' }).then(read)
}
