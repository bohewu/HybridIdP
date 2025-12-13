<template>
  <div class="px-4 py-6">
    <!-- Header -->
    <PageHeader 
      :title="$t('claims.pageTitle')" 
      :subtitle="$t('claims.pageSubtitle')"
    >
      <template #actions>
        <button
          @click="openCreateModal"
          class="inline-flex items-center justify-center px-4 py-2 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          :disabled="loading"
          data-test-id="claims-create-btn"
        >
          <svg class="h-5 w-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          {{ $t('claims.createButton') }}
        </button>
      </template>
    </PageHeader>

    <!-- ... (omitted) ... -->

          <div class="flex-1">
            <SearchInput v-model="search" :placeholder="$t('claims.searchPlaceholder')" data-test-id="claims-search-input" />
          </div>

    <!-- ... (omitted) ... -->

              <td class="px-6 py-4 whitespace-nowrap text-center">
                <div class="inline-flex gap-1">
                  <button
                    @click="openEditModal(claim)"
                    class="inline-flex items-center px-3 py-1.5 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                    :title="$t('claims.actions.edit')"
                    data-test-id="claims-edit-btn"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </button>
                  <button
                    v-if="!claim.isStandard"
                    @click="deleteClaim(claim)"
                    class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-sm font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50 disabled:cursor-not-allowed"
                    :disabled="claim.scopeCount > 0"
                    :title="$t('claims.actions.delete')"
                    data-test-id="claims-delete-btn"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                  </button>
                </div>
              </td>
            </tr>
            </template>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <Pagination
        v-if="!loading && totalCount > 0"
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :total-pages="totalPages"
        @page-change="handlePageChange"
        @page-size-change="handlePageSizeChange"
      />
    </div>

    <!-- Create/Edit Modal -->
    <ClaimFormModal
      :show="showModal"
      :claim="editingClaim"
      :error="modalError"
      @close="closeModal"
      @save="saveClaim"
    />
  </div>
</template>

<script setup>
import { ref, onMounted, watch, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import SearchInput from '@/components/common/SearchInput.vue'
import Pagination from '@/components/common/Pagination.vue'
import ClaimFormModal from './components/ClaimFormModal.vue'

const { t } = useI18n()

const claims = ref([])
const loading = ref(true)
const error = ref(null)
const showModal = ref(false)
const editingClaim = ref(null)
const modalError = ref(null)

// Pagination and filtering
const search = ref('')
const sortBy = ref('name')
const sortDirection = ref('asc')
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)

const totalPages = computed(() => Math.ceil(totalCount.value / pageSize.value))

async function fetchClaims() {
  loading.value = true
  error.value = null
  try {
    const params = new URLSearchParams({
      skip: ((page.value - 1) * pageSize.value).toString(),
      take: pageSize.value.toString(),
      sortBy: sortBy.value,
      sortDirection: sortDirection.value
    })
    
    if (search.value.trim()) {
      params.append('search', search.value.trim())
    }
    
    const response = await fetch(`/api/admin/claims?${params}`)
    if (!response.ok) throw new Error('Failed to fetch claims')
    
    const data = await response.json()
    claims.value = data.items || []
    totalCount.value = data.totalCount || 0
  } catch (err) {
    error.value = err.message
    claims.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

const handlePageChange = (newPage) => {
  page.value = newPage
}

const handlePageSizeChange = (newSize) => {
  pageSize.value = newSize
  page.value = 1
}

function openCreateModal() {
  editingClaim.value = null
  modalError.value = null
  showModal.value = true
}

function openEditModal(claim) {
  editingClaim.value = claim
  modalError.value = null
  showModal.value = true
}

function closeModal() {
  showModal.value = false
  editingClaim.value = null
}

async function saveClaim(formData) {
  error.value = null

  try {
    const url = editingClaim.value
      ? `/api/admin/claims/${editingClaim.value.id}`
      : '/api/admin/claims'
    
    const method = editingClaim.value ? 'PUT' : 'POST'
    
    const response = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(formData)
    })

    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.message || t('claims.saveError'))
    }

    closeModal()
    await fetchClaims()
  } catch (err) {
    modalError.value = err.message
    // Do not throw back to form as we handled it via prop
  }
}

async function deleteClaim(claim) {
  if (!confirm(t('claims.confirmDelete', { name: claim.name }))) {
    return
  }

  try {
    const response = await fetch(`/api/admin/claims/${claim.id}`, {
      method: 'DELETE'
    })

    if (!response.ok) {
      const data = await response.json()
      throw new Error(data.message || t('claims.deleteError'))
    }

    await fetchClaims()
  } catch (err) {
    error.value = err.message
  }
}

watch([search, sortBy, sortDirection, page, pageSize], () => {
  fetchClaims()
})

onMounted(() => {
  fetchClaims()
})
</script>
