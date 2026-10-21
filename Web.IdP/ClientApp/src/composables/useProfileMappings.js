import { computed, ref } from 'vue'
import { nativeFieldPolicyApi } from '@/services/nativeFieldPolicyApi'

export const nativeMappingFields = ['Nickname', 'FirstName', 'LastName', 'Department', 'JobTitle', 'EmployeeId']
const allowedProperties = ['displayName', 'givenName', 'surname', 'department', 'title', 'employeeId']
const copy = value => JSON.parse(JSON.stringify(value))
export function useProfileMappings(canUpdate) {
  const state = ref(null), rows = ref([]), fallback = ref(null), userNamePolicies = ref([]), loading = ref(true), saving = ref(false), error = ref(''), conflict = ref(false)
  const editing = ref(-1), editDraft = ref(null)
  const sources = computed(() => state.value?.sources || [])
  const availableSources = computed(() => sources.value.filter(s => s.enabled && !s.paused && s.providerNamespace))
  const userNameSources = computed(() => {
    const current = sources.value.filter(s => s.source === 'Proof' && s.providerNamespace)
    const retained = userNamePolicies.value.filter(p => !current.some(s => s.providerNamespace === p.providerNamespace))
    return [...current, ...retained.map(p => ({ source: p.source, providerNamespace: p.providerNamespace, enabled: false }))]
      .map(s => ({ ...s, strategy: userNamePolicies.value.find(p => p.source === s.source && p.providerNamespace === s.providerNamespace)?.strategy || 'CanonicalAccount' }))
  })
  function changeUserNamePolicy(source, strategy) {
    if (!canUpdate() || saving.value || editing.value !== -1 || conflict.value || !source.userNamePolicyAvailable || !['CanonicalAccount', 'Email'].includes(strategy)) return
    const policy = { source: 'Proof', providerNamespace: source.providerNamespace, strategy }
    const index = userNamePolicies.value.findIndex(p => p.source === policy.source && p.providerNamespace === policy.providerNamespace)
    if (index === -1) userNamePolicies.value.push(policy)
    else userNamePolicies.value[index] = policy
  }
  const properties = computed(() => (state.value?.sourceProperties || []).filter(p => allowedProperties.includes(p)))
  const sourceFor = row => sources.value.find(s => s.source === row.source && s.providerNamespace === row.providerNamespace)
  const available = row => { const source = sourceFor(row); return !!source?.enabled && !source.paused }
  const fieldsFor = (source, except = -1) => nativeMappingFields.filter(field => !rows.value.some((row, i) => i !== except && row.field === field && row.source === source?.source && row.providerNamespace === source?.providerNamespace))
  const allMapped = computed(() => availableSources.value.length > 0 && availableSources.value.every(s => fieldsFor(s).length === 0))
  async function load() {
    loading.value = true
    error.value = ''
    try {
      const result = await nativeFieldPolicyApi.mappings()
      state.value = result
      rows.value = copy(result.mappings)
      fallback.value = copy(result.firstNameFallback)
      userNamePolicies.value = copy(result.userNamePolicies || [])
      editing.value = -1
      conflict.value = false
    } catch (e) { error.value = e.message }
    finally { loading.value = false }
  }
  function add(row) {
    if (!canUpdate() || saving.value || editing.value !== -1 || !available(row) || !fieldsFor(row).includes(row.field) || !properties.value.includes(row.sourceProperty)) return
    rows.value.push(copy(row))
  }
  function edit(index) { if (!canUpdate() || saving.value) return; editing.value = index; editDraft.value = copy(rows.value[index]) }
  function complete() {
    if (!canUpdate() || !available(editDraft.value) || !properties.value.includes(editDraft.value.sourceProperty) || !fieldsFor(editDraft.value, editing.value).includes(editDraft.value.field)) return
    rows.value[editing.value] = copy(editDraft.value)
    editing.value = -1
  }
  function remove(index) { if (canUpdate() && !saving.value && editing.value === -1) rows.value.splice(index, 1) }
  async function save() {
    if (!canUpdate() || saving.value || editing.value !== -1 || conflict.value || !state.value) return false
    saving.value = true
    error.value = ''
    try {
      state.value = await nativeFieldPolicyApi.saveMappings({ expectedRevision: state.value.revision, mappings: copy(rows.value), firstNameFallback: copy(fallback.value), userNamePolicies: copy(userNamePolicies.value) })
      return true
    } catch (e) { error.value = e.message; conflict.value = e.message === 'conflict'; return false }
    finally { saving.value = false }
  }
  return { state, rows, fallback, userNamePolicies, userNameSources, changeUserNamePolicy, loading, saving, error, conflict, editing, editDraft, sources, availableSources, properties, sourceFor, available, fieldsFor, allMapped, load, add, edit, complete, remove, save }
}
