import { adminApi } from '@/utils/adminFetch'
const base = '/api/admin/saml-attributes'
async function read(response) {
  if (!response.ok || response.redirected) {
    const data = await response.json().catch(() => ({}))
    throw new Error(response.status === 403 ? 'forbidden' : response.status === 401 || response.redirected ? 'session' : data.code || 'request_failed')
  }
  return response.json()
}
export const samlAttributesApi = {
  partners: () => adminApi.get(`${base}/partners`, { cache: 'no-store' }).then(read),
  catalog: () => adminApi.get(`${base}/catalog`, { cache: 'no-store' }).then(read),
  policy: entityId => adminApi.get(`${base}/policy?${new URLSearchParams({ entityId })}`, { cache: 'no-store' }).then(read),
  save: policy => adminApi.put(`${base}/policy`, policy, { cache: 'no-store' }).then(read),
  accounts: (search = '', skip = 0) => adminApi.get(`${base}/accounts?${new URLSearchParams({ search, skip, take: 50 })}`, { cache: 'no-store' }).then(read),
  preview: request => adminApi.post(`${base}/preview`, request, { cache: 'no-store' }).then(read)
}
