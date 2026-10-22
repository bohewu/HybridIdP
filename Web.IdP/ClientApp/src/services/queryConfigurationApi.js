import { adminApi } from '@/utils/adminFetch'

const base = '/api/admin/query-configuration'
async function read(response) {
  if (!response.ok || response.redirected) {
    const data = await response.json().catch(() => ({}))
    throw new Error(response.redirected || response.status === 401 ? 'session'
      : response.status === 403 ? 'forbidden' : response.status === 409 ? 'conflict' : data.error || 'request_failed')
  }
  return response.json()
}
const approvalPath = (capability, sourceId, userId) =>
  `${base}/${encodeURIComponent(capability)}/${encodeURIComponent(sourceId)}/users/${encodeURIComponent(userId)}/approval`
export const queryConfigurationApi = {
  get: () => adminApi.get(base, { cache: 'no-store' }).then(read),
  save: request => adminApi.put(base, request).then(read),
  approval: (capability, sourceId, userId) => adminApi.get(approvalPath(capability, sourceId, userId), { cache: 'no-store' }).then(read),
  saveApproval: (capability, sourceId, userId, request) => adminApi.put(approvalPath(capability, sourceId, userId), request).then(read)
}

export function queryError(t, error) {
  const known = ['session', 'forbidden', 'conflict', 'source_unavailable', 'invalid_mapping', 'invalid_stored_state', 'missing_input', 'algorithm_approval_required', 'not_found']
  return t(`query-fields.errors.${known.includes(error.message) ? error.message : 'request_failed'}`)
}

// Only server reason codes in this allowlist become user-visible explanations.
export function querySourceStatus(t, source) {
  if (source?.available) return t('query-fields.available')
  const reasons = ['deployment_disabled', 'source_untrusted', 'source_paused', 'invalid_configuration', 'unsupported_inputs']
  return t(`query-fields.reasons.${reasons.includes(source?.unavailableReason) ? source.unavailableReason : 'source_unavailable'}`)
}

export function queryMappingSupported(sources, item) {
  const deployed = sources?.find(candidate => candidate.capability === item.capability && candidate.sourceId === item.sourceId && candidate.providerNamespace === item.providerNamespace)
  const setKey = attributes => [...attributes].sort().join('|')
  return !!(deployed?.available && (item.queryRule ?? null) === (deployed.queryRule ?? null)
    && deployed.acceptedAttributeSets.some(set => setKey(set) === setKey(item.mappings.map(mapping => mapping.attribute)))
    && item.mappings.every(mapping => deployed.availableCandidates.some(candidate => candidate.attribute === mapping.attribute && candidate.valueSources.includes(mapping.valueSource))))
}
