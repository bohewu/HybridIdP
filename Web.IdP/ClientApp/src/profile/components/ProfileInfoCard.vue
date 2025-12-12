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

        <template v-if="profile?.person">
          <div class="sm:col-span-2 mt-2 pt-4 border-t border-gray-100">
             <h4 class="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-4 flex items-center gap-2">
                <i class="bi bi-person-vcard"></i>
                {{ t('profile.info.personalDetails') }}
             </h4>
          </div>

          <div class="sm:col-span-1">
            <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
              <i class="bi bi-type text-gray-400"></i>
              {{ t('profile.info.fullName') }}
            </dt>
            <dd class="mt-1 text-sm text-gray-900 font-medium pl-6">{{ profile.personFullName }}</dd>
          </div>

          <div v-if="profile.person.nationalId" class="sm:col-span-1">
            <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
              <i class="bi bi-person-badge text-gray-400"></i>
              {{ t('profile.info.nationalId') }}
            </dt>
            <dd class="mt-1 text-sm text-gray-900 font-medium pl-6 tracking-wide">{{ profile.person.nationalId }}</dd>
          </div>

          <div v-if="profile.person.passportNumber" class="sm:col-span-1">
            <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
              <i class="bi bi-airplane text-gray-400"></i>
              {{ t('profile.info.passport') }}
            </dt>
            <dd class="mt-1 text-sm text-gray-900 font-medium pl-6 tracking-wide">{{ profile.person.passportNumber }}</dd>
          </div>

          <div v-if="profile.person.residentCertificateNumber" class="sm:col-span-1">
            <dt class="text-sm font-medium text-gray-500 flex items-center gap-2">
              <i class="bi bi-postcard text-gray-400"></i>
              {{ t('profile.info.residentCert') }}
            </dt>
            <dd class="mt-1 text-sm text-gray-900 font-medium pl-6 tracking-wide">{{ profile.person.residentCertificateNumber }}</dd>
          </div>

          <!-- Identity Verification Status Check (Global) -->
          <div v-if="profile.identityVerified" class="sm:col-span-2 pt-2">
             <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-green-100 text-green-800 border border-green-200">
                 <i class="bi bi-patch-check-fill mr-1.5"></i> 
                 {{ t('profile.info.identityVerified') }}
                 <span v-if="profile.identityDocumentType" class="ml-1 text-green-700 opacity-75">
                    ({{ profile.identityDocumentType }})
                 </span>
             </span>
          </div>
        </template>
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
