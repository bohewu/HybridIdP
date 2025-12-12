<template>
  <div class="bg-white shadow rounded-lg p-6 mb-6">
    <h2 class="text-xl font-semibold text-gray-900 mb-4">
      {{ t('profile.accountInfo') }}
    </h2>

    <div class="space-y-3">
      <!-- Username -->
      <div class="flex justify-between">
        <span class="text-sm font-medium text-gray-500">{{ t('profile.username') }}</span>
        <span class="text-sm text-gray-900">{{ profile.userName }}</span>
      </div>

      <!-- Email -->
      <div class="flex justify-between">
        <span class="text-sm font-medium text-gray-500">{{ t('profile.email') }}</span>
        <div class="flex items-center gap-2">
          <span class="text-sm text-gray-900">{{ profile.email }}</span>
          <span v-if="profile.emailConfirmed" class="text-green-600" title="Email verified">
            <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
            </svg>
          </span>
        </div>
      </div>

      <!-- External Logins -->
      <div v-if="profile.externalLogins && profile.externalLogins.length > 0">
        <span class="text-sm font-medium text-gray-500">{{ t('profile.externalLogins') }}</span>
        <div class="mt-2 space-y-1">
          <div v-for="login in profile.externalLogins" :key="login.loginProvider" 
               class="flex items-center gap-2 text-sm text-gray-700">
            <svg class="w-4 h-4 text-gray-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd"/>
            </svg>
            <span>{{ login.providerDisplayName || login.loginProvider }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Person Info Section (if linked) -->
    <div v-if="profile.person" class="mt-6 pt-6 border-t border-gray-200">
      <h3 class="text-lg font-semibold text-gray-900 mb-4">
        {{ t('profile.personInfo') }}
      </h3>
      
      <div class="space-y-3">
        <div class="flex justify-between">
          <span class="text-sm font-medium text-gray-500">{{ t('profile.fullName') }}</span>
          <span class="text-sm text-gray-900">{{ profile.person.fullName }}</span>
        </div>
        
        <div v-if="profile.person.employeeId" class="flex justify-between">
          <span class="text-sm font-medium text-gray-500">{{ t('profile.employeeId') }}</span>
          <span class="text-sm text-gray-900">{{ profile.person.employeeId }}</span>
        </div>
        
        <div v-if="profile.person.department" class="flex justify-between">
          <span class="text-sm font-medium text-gray-500">{{ t('profile.department') }}</span>
          <span class="text-sm text-gray-900">{{ profile.person.department }}</span>
        </div>
        
        <div v-if="profile.person.jobTitle" class="flex justify-between">
          <span class="text-sm font-medium text-gray-500">{{ t('profile.jobTitle') }}</span>
          <span class="text-sm text-gray-900">{{ profile.person.jobTitle }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { defineProps } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  profile: {
    type: Object,
    required: true
  }
})

const { t } = useI18n()
</script>
