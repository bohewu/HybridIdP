import { adminApi } from '@/utils/adminFetch'

const base = '/api/admin/native-field-policies'
async function read(response) {
  if (!response.ok || response.redirected) {
    const data = await response.json().catch(() => ({}))
    const code = response.redirected || response.status === 401 ? 'session'
      : response.status === 403 ? 'forbidden' : response.status === 409 ? 'conflict' : data.error || 'request_failed'
    throw new Error(code)
  }
  return response.status === 204 ? null : response.json()
}
const targetPath = (target, id) => `${base}/${target === 'User' ? 'users' : 'persons'}/${encodeURIComponent(id)}`
export const nativeFieldPolicyApi = {
  sources: () => adminApi.get(`${base}/sources`, { cache: 'no-store' }).then(read),
  pause: (source, request) => adminApi.put(`${base}/sources/${source}`, request).then(read),
  target: (target, id) => adminApi.get(targetPath(target, id), { cache: 'no-store' }).then(read),
  update: (target, id, field, request) => adminApi.put(`${targetPath(target, id)}/${field}`, { ...request, allowSourceClear: false }).then(read)
}

export function policyError(t, error) {
  const known = ['session', 'forbidden', 'conflict', 'source_unavailable', 'binding_mismatch', 'owner_mismatch',
    'source_clear_unsupported', 'manual_value_required', 'value_too_long', 'not_found', 'invalid_stored_state',
    'unsupported_source', 'unsupported_field_or_operation', 'unsupported_field', 'invalid_target', 'invalid_fields']
  return t(`native-fields.errors.${known.includes(error.message) ? error.message : 'request_failed'}`)
}
