<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import SearchInput from '@/components/common/SearchInput.vue'

const { t } = useI18n()

const props = defineProps({
  users: { type: Array, required: true },
  loading: { type: Boolean, default: false },
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  totalCount: { type: Number, required: true },
  sort: { type: String, default: '' },
  canUpdate: { type: Boolean, default: false },
  canDelete: { type: Boolean, default: false },
  search: { type: String, default: '' },
  isActiveFilter: { type: String, default: '' }
})

const emit = defineEmits([
  'edit',
  'manage-roles',
  'deactivate',
  'delete',
  'reactivate',
  'page-change',
  'page-size-change',
  'sort-change',
  'update:search',
  'update:isActiveFilter'
])

const totalPages = computed(() => {
  return Math.ceil(props.totalCount / props.pageSize)
})

const formatDate = (dateString) => {
  if (!dateString) return t('userDetails.never')
  return new Date(dateString).toLocaleString()
}

const getBadgeClass = (isActive) => {
  return isActive 
    ? 'bg-green-100 text-green-800' 
    : 'bg-gray-100 text-gray-800'
}

const handleSort = (field) => {
  const [currentField, currentDir] = props.sort.split(':')
  let newDir = 'asc'
  
  if (currentField === field) {
    newDir = currentDir === 'asc' ? 'desc' : 'asc'
  }
  
  emit('sort-change', `${field}:${newDir}`)
}

const getSortIcon = (field) => {
  const [currentField, currentDir] = props.sort.split(':')
  if (currentField !== field) {
    return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16V4m0 0L3 8m4-4l4 4m6 0v12m0 0l4-4m-4 4l-4-4"></path>
    </svg>`
  }
  
  if (currentDir === 'asc') {
    return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 15l7-7 7 7"></path>
    </svg>`
  }
  
  return `<svg class="w-4 h-4 inline-block" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"></path>
  </svg>`
}
</script>

<template>
  <div class="user-list">
    <!-- Unified Card: Filters + Table + Pagination -->
    <div class="bg-white shadow-sm rounded-lg border border-gray-200">
      <!-- Filter Section -->
      <div class="p-4 border-b border-gray-200">
        <div class="flex flex-col md:flex-row md:items-center gap-3">
          <!-- Search Input -->
          <div class="flex-1">
            <SearchInput :model-value="search" @update:model-value="emit('update:search', $event)" :placeholder="t('admin.users.search')" />
          </div>
          
          <!-- Filter Options -->
          <div>
            <select
              :value="isActiveFilter"
              @change="emit('update:isActiveFilter', $event.target.value)"
              class="block w-full px-4 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 transition-colors h-10"
            >
              <option value="">{{ t('admin.users.all') }}</option>
              <option value="true">{{ t('admin.users.active') }}</option>
              <option value="false">{{ t('admin.users.inactive') }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="loading" class="flex flex-col items-center justify-center py-12">
        <svg class="animate-spin h-10 w-10 text-indigo-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
        <p class="mt-2 text-sm text-gray-600">{{ $t('admin.users.loading') }}</p>
      </div>

      <!-- Empty State -->
      <div v-else-if="users.length === 0" class="text-center py-12">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"></path>
        </svg>
        <p class="mt-2 text-sm text-gray-600">{{ $t('admin.users.noUsers') }}</p>
      </div>

      <!-- Users Table -->
      <div v-else class="overflow-x-auto">
        <table class="min-w-full divide-y divide-gray-200">
          <thead class="bg-gray-50">
            <tr>
              <th
                @click="handleSort('email')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ $t('tableHeaders.email') }}</span>
                  <span v-html="getSortIcon('email')"></span>
                </div>
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('tableHeaders.name') }}
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('tableHeaders.department') }}
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('tableHeaders.roles') }}
              </th>
              <th
                @click="handleSort('isActive')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ $t('tableHeaders.status') }}</span>
                  <span v-html="getSortIcon('isActive')"></span>
                </div>
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('tableHeaders.lastLogin') }}
              </th>
              <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('tableHeaders.actions') }}
              </th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
            <tr v-for="user in users" :key="user.id" class="hover:bg-gray-50">
              <td class="px-6 py-4 whitespace-nowrap">
                <div class="text-sm font-medium text-gray-900">{{ user.email }}</div>
                <div v-if="user.employeeId" class="text-sm text-gray-500">{{ t('userDetails.id', { id: user.employeeId }) }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <div v-if="user.firstName || user.lastName" class="text-sm text-gray-900">
                  {{ user.firstName }} {{ user.lastName }}
                </div>
                <div v-else class="text-sm text-gray-400 italic">{{ $t('userDetails.noName') }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <span v-if="user.department" class="text-sm text-gray-900">{{ user.department }}</span>
                <span v-else class="text-sm text-gray-400">{{ t('userDetails.noDepartment') }}</span>
              </td>
              <td class="px-6 py-4">
                <div v-if="user.roles && user.roles.length > 0" class="flex flex-wrap gap-1">
                  <span
                    v-for="role in user.roles"
                    :key="role"
                    class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-indigo-100 text-indigo-800"
                  >
                    {{ role }}
                  </span>
                </div>
                <span v-else class="text-sm text-gray-400">{{ $t('userDetails.noRoles') }}</span>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <span
                  :class="['inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium', getBadgeClass(user.isActive)]"
                >
                  {{ user.isActive ? $t('userDetails.active') : $t('userDetails.inactive') }}
                </span>
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {{ formatDate(user.lastLoginDate) }}
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-center">
                <div class="inline-flex gap-1">
                  <!-- Edit button - only show if canUpdate -->
                  <button
                    v-if="canUpdate"
                    @click="emit('edit', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                    :title="$t('admin.users.edit')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"></path>
                    </svg>
                  </button>
                  
                  <!-- Manage Roles button - only show if canUpdate -->
                  <button
                    v-if="canUpdate"
                    @click="emit('manage-roles', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-blue-300 text-blue-700 text-sm font-medium rounded-md hover:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                    :title="$t('admin.users.manageRoles')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"></path>
                    </svg>
                  </button>
                  
                  <!-- Deactivate button - only show if active and canDelete -->
                  <button
                    v-if="user.isActive && canDelete"
                    @click="emit('deactivate', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-orange-300 text-orange-700 text-sm font-medium rounded-md hover:bg-orange-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-orange-500"
                    :title="$t('userActions.deactivate')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636"></path>
                    </svg>
                  </button>
                  
                  <!-- Delete button - only show if canDelete (with warning) -->
                  <button
                    v-if="canDelete"
                    @click="emit('delete', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-red-300 text-red-700 text-sm font-medium rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                    :title="$t('userActions.deletePermanently')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"></path>
                    </svg>
                  </button>
                  
                  <!-- Reactivate button - only show if inactive and canUpdate -->
                  <button
                    v-if="!user.isActive && canUpdate"
                    @click="emit('reactivate', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-green-300 text-green-700 text-sm font-medium rounded-md hover:bg-green-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                    :title="$t('userActions.reactivate')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                    </svg>
                  </button>
                  
                  <!-- Show message if no permissions -->
                  <span v-if="!canUpdate && !canDelete" class="text-xs text-gray-400 italic">
                    {{ $t('admin.users.noActions') }}
                  </span>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="!loading && users.length > 0" class="flex flex-col sm:flex-row sm:justify-between sm:items-center gap-3 px-4 py-3 border-t border-gray-200">
        <div class="flex-1 flex justify-between sm:hidden">
          <button
            @click="emit('page-change', page - 1)"
            :disabled="page === 1"
            class="relative inline-flex items-center justify-center px-4 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          >
            {{ $t('pagination.previous') }}
          </button>
          <button
            @click="emit('page-change', page + 1)"
            :disabled="page === totalPages"
            class="relative inline-flex items-center justify-center px-4 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
          >
            {{ $t('pagination.next') }}
          </button>
        </div>
        <div class="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
          <div>
            <p class="text-sm text-gray-700">
              {{ $t('pagination.showing', { 
                from: (page - 1) * pageSize + 1, 
                to: Math.min(page * pageSize, totalCount), 
                total: totalCount 
              }) }}
            </p>
          </div>
          <div class="flex items-center gap-2">
            <select
              :value="pageSize"
              @change="emit('page-size-change', Number($event.target.value))"
              class="block px-3 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md transition-colors h-10"
            >
              <option :value="10">{{ $t('pagination.perPage', { count: 10 }) }}</option>
              <option :value="25">{{ $t('pagination.perPage', { count: 25 }) }}</option>
              <option :value="50">{{ $t('pagination.perPage', { count: 50 }) }}</option>
              <option :value="100">{{ $t('pagination.perPage', { count: 100 }) }}</option>
            </select>
            <nav class="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
              <button
                @click="emit('page-change', page - 1)"
                :disabled="page === 1"
                class="relative inline-flex items-center justify-center px-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
              >
                <span class="sr-only">{{ $t('pagination.previous') }}</span>
                <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M12.707 5.293a1 1 0 010 1.414L9.414 10l3.293 3.293a1 1 0 01-1.414 1.414l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 0z" clip-rule="evenodd" />
                </svg>
              </button>
              <button
                v-for="p in Math.min(totalPages, 10)"
                :key="p"
                @click="emit('page-change', p)"
                :class="[
                  'relative inline-flex items-center justify-center px-4 border text-sm font-medium transition-colors h-10',
                  p === page
                    ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                    : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                ]"
              >
                {{ p }}
              </button>
              <button
                @click="emit('page-change', page + 1)"
                :disabled="page === totalPages"
                class="relative inline-flex items-center justify-center px-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors h-10"
              >
                <span class="sr-only">{{ $t('pagination.next') }}</span>
                <svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z" clip-rule="evenodd" />
                </svg>
              </button>
            </nav>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
