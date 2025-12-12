<template>
  <!-- Account Info -->
  <div class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6">
    <div class="px-4 py-5 sm:px-6">
      <h3 class="text-lg leading-6 font-medium text-gray-900">{{ t('profile.accountInfo') }}</h3>
    </div>
    <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
      <dl class="sm:divide-y sm:divide-gray-200">
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.username') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2">{{ profile.userName }}</dd>
        </div>
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.email') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2 flex items-center gap-2">
            {{ profile.email }}
            <span v-if="profile.emailConfirmed" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
              <svg class="w-3 h-3 mr-1" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
              </svg>
              Verified
            </span>
          </dd>
        </div>
      </dl>
    </div>
  </div>

  <!-- Person Info (if linked) -->
  <div v-if="profile.person" class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6">
    <div class="px-4 py-5 sm:px-6">
      <h3 class="text-lg leading-6 font-medium text-gray-900">{{ t('profile.personInfo') }}</h3>
    </div>
    <div class="border-t border-gray-200 px-4 py-5 sm:p-0">
      <dl class="sm:divide-y sm:divide-gray-200">
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.fullName') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2">{{ profile.person.fullName || '-' }}</dd>
        </div>
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.employeeId') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2">{{ profile.person.employeeId || '-' }}</dd>
        </div>
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.department') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2">{{ profile.person.department || '-' }}</dd>
        </div>
        <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
          <dt class="text-sm font-medium text-gray-500">{{ t('profile.jobTitle') }}</dt>
          <dd class="mt-1 text-sm text-gray-900 sm:mt-0 sm:col-span-2">{{ profile.person.jobTitle || '-' }}</dd>
        </div>
      </dl>
    </div>
  </div>

  <!-- External Logins -->
  <div v-if="profile.externalLogins && profile.externalLogins.length > 0" class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6">
    <div class="px-4 py-5 sm:px-6">
      <h3 class="text-lg leading-6 font-medium text-gray-900">{{ t('profile.externalLogins') }}</h3>
    </div>
    <div class="border-t border-gray-200">
      <ul class="divide-y divide-gray-200">
        <li v-for="login in profile.externalLogins" :key="login.loginProvider" class="px-4 py-4 sm:px-6 flex items-center">
          <svg class="w-5 h-5 text-gray-400 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1"/>
          </svg>
          <span class="text-sm text-gray-900">{{ login.providerDisplayName || login.loginProvider }}</span>
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup>
import { defineProps } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps({
  profile: {
    type: Object,
    required: true
  }
})

const { t } = useI18n()
</script>
