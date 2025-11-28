<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import SearchInput from '@/components/common/SearchInput.vue'
import Pagination from '@/components/common/Pagination.vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

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
  'manage-sessions',
  'view-login-history',
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
      <LoadingIndicator v-if="loading" :loading="loading" size="md" :message="$t('admin.users.loading')" />

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

                  <!-- Manage Sessions button - show if canUpdate or canRead -->
                  <button
                    v-if="canUpdate || canDelete || true" @click="emit('manage-sessions', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-purple-300 text-purple-700 text-sm font-medium rounded-md hover:bg-purple-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-purple-500"
                    :title="$t('admin.users.manageSessions')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5.5 8.5l5.5 5.5 5.5-5.5" />
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16v12H4z" />
                    </svg>
                  </button>

                  <!-- View Login History button - show for all users -->
                  <button
                    @click="emit('view-login-history', user)"
                    class="inline-flex items-center px-3 py-1.5 border border-cyan-300 text-cyan-700 text-sm font-medium rounded-md hover:bg-cyan-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-cyan-500"
                    :title="$t('admin.users.loginHistory.title')"
                  >
                    <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
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

      <Pagination
        :page="page"
        :page-size="pageSize"
        :total-count="totalCount"
        :total-pages="totalPages"
        @page-change="emit('page-change', $event)"
        @page-size-change="emit('page-size-change', $event)"
      />
    </div>
  </div>
</template>
