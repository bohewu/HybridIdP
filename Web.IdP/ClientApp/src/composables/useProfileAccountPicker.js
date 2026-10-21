import { ref } from 'vue'
import { profileSyncApi } from '@/services/profileSyncApi'

export function useProfileAccountPicker(canRead) {
  const search = ref(''), page = ref(0), items = ref([]), total = ref(0), selected = ref([]), loading = ref(false), error = ref('')
  let request = 0
  async function load(reset = false) {
    if (!canRead()) return
    if (reset) page.value = 0
    const id = ++request
    loading.value = true
    error.value = ''
    try {
      const data = await profileSyncApi.searchAccounts(search.value, page.value * 10)
      if (id === request) { items.value = data.items || []; total.value = data.totalCount || 0 }
    } catch (e) { if (id === request) error.value = e.message }
    finally { if (id === request) loading.value = false }
  }
  function toggle(account) {
    const index = selected.value.findIndex(row => row.id === account.id)
    if (index !== -1) selected.value.splice(index, 1)
    else if (selected.value.length < 50) selected.value.push({ id: account.id, userName: account.userName })
  }
  return { search, page, items, total, selected, loading, error, load, toggle }
}
