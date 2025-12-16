<template>
  <!-- Account Info -->
  <div class="bg-white shadow-sm rounded-lg border border-gray-200 mb-6" data-testid="profile-info-card">
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
              {{ t('profile.info.verified') }}
            </span>
          </dd>
        </div>

        <template v-if="profile?.person">
          <div class="sm:col-span-2 mt-2 pt-4 border-t border-gray-100 px-4 sm:px-6">
             <h4 class="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-4 flex items-center gap-2">
                <i class="bi bi-person-vcard"></i>
                {{ t('profile.info.personalDetails') }}
             </h4>
          </div>

          <div class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6 bg-gray-50/50" v-if="profile?.person">
             <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
               <i class="bi bi-type text-gray-400"></i>
               {{ t('profile.info.fullName') }}
             </dt>
             <dd class="mt-1 text-sm text-gray-900 font-medium sm:mt-0 sm:col-span-2">{{ profile.personFullName }}</dd>
           </div>

          <!-- Identity Verification Status Check (Global) -->
          <div v-if="profile.identityVerified" class="py-3 sm:py-4 sm:grid sm:grid-cols-3 sm:gap-4 sm:px-6">
             <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
                <i class="bi bi-patch-check text-green-500"></i>
                {{ t('profile.info.status') }}
             </dt>
             <dd class="mt-1 sm:mt-0 sm:col-span-2">
                 <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 border border-green-200">
                     <i class="bi bi-patch-check-fill mr-1.5"></i> 
                     {{ t('profile.info.identityVerified') }}
                     <span v-if="profile.identityDocumentType" class="ml-1 text-green-700 opacity-75">
                        ({{ profile.identityDocumentType }})
                     </span>
                 </span>
             </dd>
          </div>
        </template>
      </dl>
      
      <!-- Security Status Hints -->
      <div class="border-t border-gray-200 px-4 py-5 sm:px-6 bg-gradient-to-r from-blue-50/50 to-transparent">
        <h4 class="text-sm font-medium text-gray-700 mb-3 flex items-center gap-2">
          <svg class="w-4 h-4 text-blue-500" fill="currentColor" viewBox="0 0 20 20">
            <path fill-rule="evenodd" d="M2.166 4.999A11.954 11.954 0 0010 1.944 11.954 11.954 0 0017.834 5c.11.65.166 1.32.166 2.001 0 5.225-3.34 9.67-8 11.317C5.34 16.67 2 12.225 2 7c0-.682.057-1.35.166-2.001zm11.541 3.708a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
          </svg>
          {{ t('profile.security.title') }}
        </h4>
        <div class="space-y-2">
          <!-- MFA Status -->
          <div class="flex items-center gap-3 text-sm">
            <span v-if="profile.twoFactorEnabled" class="inline-flex items-center gap-1.5 text-green-700">
              <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
              </svg>
              {{ t('profile.security.mfaEnabled') }}
            </span>
            <span v-else class="inline-flex items-center gap-1.5 text-amber-600">
              <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
              </svg>
              {{ t('profile.security.mfaNotEnabled') }}
            </span>
          </div>
          <!-- Password Status -->
          <div class="flex items-center gap-3 text-sm">
            <span v-if="profile.hasLocalPassword" class="inline-flex items-center gap-1.5 text-green-700">
              <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
              </svg>
              {{ t('profile.security.passwordSet') }}
            </span>
            <span v-else class="inline-flex items-center gap-1.5 text-gray-500">
              <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
              </svg>
              {{ t('profile.security.noPassword') }}
            </span>
          </div>
        </div>
      </div>
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
