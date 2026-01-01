<script setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ActionMenu from '@/components/common/ActionMenu.vue'
import SearchInput from '@/components/common/SearchInput.vue'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'
import Pagination from '@/components/common/Pagination.vue'

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
  canRead: { type: Boolean, default: false },
  canImpersonate: { type: Boolean, default: false },
  search: { type: String, default: '' },
  isActiveFilter: { type: String, default: '' },
  currentUserId: { type: String, default: null }
})

const emit = defineEmits([
  'edit',
  'manage-roles',
  'manage-sessions',
  'impersonate',
  'view-login-history',
  'reset-mfa',
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
  if (!dateString) return t('users.details.never')
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
            <SearchInput :model-value="search" @update:model-value="emit('update:search', $event)" :placeholder="t('users.search')" />
          </div>
          
          <!-- Filter Options -->
          <div>
            <select
              :value="isActiveFilter"
              @change="emit('update:isActiveFilter', $event.target.value)"
              class="block w-full px-4 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-google-500 transition-colors h-10"
            >
              <option value="">{{ t('users.all') }}</option>
              <option value="true">{{ t('users.active') }}</option>
              <option value="false">{{ t('users.inactive') }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <LoadingIndicator v-if="loading" :loading="loading" size="md" :message="$t('users.loading')" />

      <!-- Empty State -->
      <div v-else-if="users.length === 0" class="text-center py-12">
        <svg class="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"></path>
        </svg>
        <p class="mt-2 text-sm text-gray-600">{{ $t('users.noUsers') }}</p>
      </div>

      <!-- Users Table -->
      <div v-else class="overflow-x-auto">
        <table class="min-w-full divide-y divide-gray-200">
          <thead class="bg-gray-50">
            <tr>
              <th
                @click="handleSort('userName')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ $t('users.tableHeaders.username') }}</span>
                  <span v-html="getSortIcon('userName')"></span>
                </div>
              </th>
              <th
                @click="handleSort('email')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                <div class="flex items-center space-x-1">
                  <span>{{ $t('users.email') }}</span>
                  <span v-html="getSortIcon('email')"></span>
                </div>
              </th>
              <th
                 @click="handleSort('firstName')"
                 class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                 <div class="flex items-center space-x-1">
                  <span>{{ $t('users.name') }}</span>
                  <span v-html="getSortIcon('firstName')"></span>
                </div>
              </th>
              <th
                @click="handleSort('department')"
                class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
              >
                 <div class="flex items-center space-x-1">
                    <span>{{ $t('users.department') }}</span>
                    <span v-html="getSortIcon('department')"></span>
                 </div>
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('users.roles') }}
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('users.status') }}
              </th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('users.lastLogin') }}
              </th>
              <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                {{ $t('admin.common.actions') }}
              </th>
            </tr>
          </thead>
          <tbody class="bg-white divide-y divide-gray-200">
            <tr v-for="user in users" :key="user.id" class="hover:bg-gray-50" :data-test-id="`user-row-${user.id}`">
              <td class="px-6 py-4 whitespace-nowrap" :data-test-id="`user-email-${user.id}`">
                <div class="text-sm font-medium text-gray-900">{{ user.userName }}</div>
                <div v-if="user.employeeId" class="text-sm text-gray-500">{{ t('users.details.id', { id: user.employeeId }) }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <div class="text-sm text-gray-900">{{ user.email }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <div v-if="user.firstName || user.lastName" class="text-sm text-gray-900">
                  {{ user.firstName }} {{ user.lastName }}
                </div>
                <div v-else class="text-sm text-gray-400 italic">{{ $t('users.details.noName') }}</div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <span v-if="user.department" class="text-sm text-gray-900">{{ user.department }}</span>
                <span v-else class="text-sm text-gray-400">{{ t('users.details.noDepartment') }}</span>
              </td>
              <td class="px-6 py-4">
                <div v-if="user.roles && user.roles.length > 0" class="flex flex-wrap gap-1">
                  <span
                    v-for="role in user.roles"
                    :key="role"
                    class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-google-100 text-google-500"
                  >
                    {{ role }}
                  </span>
                </div>
                <span v-else class="text-sm text-gray-400">{{ $t('users.details.noRoles') }}</span>
              </td>
              <td class="px-6 py-4 whitespace-nowrap">
                <div class="flex items-center gap-1">
                  <span
                    :class="['inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium', getBadgeClass(user.isActive)]"
                  >
                    {{ user.isActive ? $t('users.details.active') : $t('users.details.inactive') }}
                  </span>
                  <span
                    v-if="user.twoFactorEnabled || user.emailMfaEnabled"
                    class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-700"
                    :title="$t('users.mfa.enabled')"
                  >
                    <svg class="w-3 h-3" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M2.166 4.999A11.954 11.954 0 0010 1.944 11.954 11.954 0 0017.834 5c.11.65.166 1.32.166 2.001 0 5.225-3.34 9.67-8 11.317C5.34 16.67 2 12.225 2 7c0-.682.057-1.35.166-2.001zm11.541 3.708a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/></svg>
                  </span>
                </div>
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                {{ formatDate(user.lastLoginDate) }}
              </td>
              <td class="px-6 py-4 whitespace-nowrap text-right align-middle">
                <ActionMenu>
                  <template #trigger>
                    <button class="text-gray-400 hover:text-gray-600 focus:outline-none">
                      <svg class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 5v.01M12 12v.01M12 19v.01M12 6a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2z" />
                      </svg>
                    </button>
                  </template>
                  <template #content="{ close }">
                    <button
                      v-if="canUpdate"
                      @click="emit('edit', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                    >
                      {{ t('users.edit') }}
                    </button>
                    <button
                      v-if="canUpdate"
                      @click="emit('manage-roles', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                    >
                      {{ t('users.manageRoles') }}
                    </button>
                    <button
                      v-if="canUpdate || canRead"
                      @click="emit('manage-sessions', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                    >
                      {{ t('users.manageSessions') }}
                    </button>
                    <button
                      v-if="canImpersonate && user.isActive && !(user.roles && user.roles.includes('Admin'))"
                      :disabled="user.id === currentUserId"
                      @click="emit('impersonate', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                      :class="user.id === currentUserId ? 'text-gray-400' : 'text-pink-700 hover:bg-pink-50'"
                    >
                      {{ t('users.actions.impersonate') }}
                    </button>
                    <button
                      @click="emit('view-login-history', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                    >
                      {{ t('users.actions.viewLoginHistory') }}
                    </button>
                    <button
                      v-if="canUpdate && (user.twoFactorEnabled || user.emailMfaEnabled)"
                      @click="emit('reset-mfa', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-blue-600 hover:bg-blue-50"
                    >
                      {{ t('users.mfa.reset') }}
                    </button>
                    <div class="border-t border-gray-100 my-1"></div>
                    <button
                      v-if="user.isActive && canDelete"
                      @click="emit('deactivate', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-orange-600 hover:bg-orange-50"
                    >
                      {{ t('users.actions.deactivate') }}
                    </button>
                    <button
                      v-if="!user.isActive && canUpdate"
                      @click="emit('reactivate', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-green-600 hover:bg-green-50"
                    >
                      {{ t('users.actions.reactivate') }}
                    </button>
                    <button
                      v-if="canDelete"
                      @click="emit('delete', user); close()"
                      class="text-left w-full block px-4 py-2 text-sm text-red-600 hover:bg-red-50"
                    >
                      {{ t('users.actions.deletePermanently') }}
                    </button>
                  </template>
                </ActionMenu>
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
