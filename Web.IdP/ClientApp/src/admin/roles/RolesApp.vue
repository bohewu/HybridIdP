<template>
  <div class="container-fluid">
    <div class="d-flex justify-content-between align-items-center mb-4">
      <h1 class="h3">Role Management</h1>
      <div>
        <button class="btn btn-primary" @click="showCreateModal = true" :disabled="loading">
          <i class="bi bi-plus-circle me-2"></i>
          Create Role
        </button>
      </div>
    </div>

    <div v-if="error" class="alert alert-danger" role="alert">
      {{ error }}
    </div>

    <div class="card">
      <div class="card-body">
        <div class="d-flex justify-content-between mb-3">
          <input v-model="search" @keyup.enter="fetchRoles(0)" type="text" class="form-control w-auto" placeholder="Search name/description" />
          <div class="d-flex gap-2">
            <select v-model="sortBy" class="form-select w-auto">
              <option value="name">Name</option>
              <option value="createdat">Created</option>
            </select>
            <select v-model="sortDirection" class="form-select w-auto">
              <option value="asc">Asc</option>
              <option value="desc">Desc</option>
            </select>
            <button class="btn btn-outline-secondary" @click="fetchRoles(0)" :disabled="loading">Apply</button>
          </div>
        </div>

        <div class="table-responsive">
          <table class="table align-middle">
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th class="text-center">Permissions</th>
                <th class="text-center">Users</th>
                <th class="text-center">System</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="loading">
                <td colspan="5">
                  <div class="text-center py-4">Loading...</div>
                </td>
              </tr>
              <tr v-else-if="roles.length === 0">
                <td colspan="5">
                  <div class="text-center py-4">No roles found</div>
                </td>
              </tr>
              <tr v-for="r in roles" :key="r.id">
                <td class="fw-medium">{{ r.name }}</td>
                <td>{{ r.description }}</td>
                <td class="text-center">
                  <span class="badge text-bg-secondary">{{ r.permissions?.length || 0 }}</span>
                </td>
                <td class="text-center">
                  <span class="badge text-bg-info">{{ r.userCount }}</span>
                </td>
                <td class="text-center">
                  <span v-if="r.isSystem" class="badge text-bg-warning">System</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="d-flex justify-content-between align-items-center mt-3">
          <div class="text-muted">Total: {{ totalCount }}</div>
          <div class="btn-group">
            <button class="btn btn-outline-secondary" :disabled="skip === 0 || loading" @click="prevPage">Prev</button>
            <button class="btn btn-outline-secondary" :disabled="skip + take >= totalCount || loading" @click="nextPage">Next</button>
          </div>
        </div>
      </div>
    </div>

    <!-- Create Role Modal -->
    <CreateRoleModal
      v-if="showCreateModal"
      @close="showCreateModal = false"
      @created="handleRoleCreated"
    />
  </div>
  
</template>

<script setup>
import { onMounted, ref } from 'vue'
import CreateRoleModal from './components/CreateRoleModal.vue'

const roles = ref([])
const loading = ref(false)
const error = ref('')

const skip = ref(0)
const take = ref(25)
const totalCount = ref(0)

const search = ref('')
const sortBy = ref('name')
const sortDirection = ref('asc')

const showCreateModal = ref(false)

async function fetchRoles(newSkip = skip.value) {
  loading.value = true
  error.value = ''
  try {
    const params = new URLSearchParams({
      skip: String(newSkip),
      take: String(take.value),
      search: search.value || '',
      sortBy: sortBy.value,
      sortDirection: sortDirection.value
    })
    const res = await fetch(`/api/Admin/roles?${params.toString()}`, {
      headers: { 'Accept': 'application/json' }
    })
    if (!res.ok) throw new Error(`Failed to load roles (${res.status})`)
    const data = await res.json()
    roles.value = data.items || []
    totalCount.value = data.totalCount || 0
    skip.value = data.skip || 0
    take.value = data.take || take.value
  } catch (e) {
    error.value = e?.message || 'Unknown error'
  } finally {
    loading.value = false
  }
}

function prevPage() { if (skip.value > 0) fetchRoles(Math.max(0, skip.value - take.value)) }
function nextPage() { if (skip.value + take.value < totalCount.value) fetchRoles(skip.value + take.value) }

function handleRoleCreated() {
  showCreateModal.value = false
  // Refresh the list and reset to first page
  fetchRoles(0)
}

onMounted(() => fetchRoles(0))
</script>

<style scoped>
.card { border-radius: 10px; }
</style>
