<template>
  <div class="max-w-7xl mx-auto py-12 px-4 sm:px-6 lg:px-8"
       v-loading="{ loading, overlay: true, message: t('profile.loading') }">
    <!-- Page Header -->
    <div class="mb-8">
      <h1 class="text-2xl font-bold text-gray-900">{{ t('profile.title') }}</h1>
    </div>

    <!-- Main Content -->
    <div v-if="!loading" class="space-y-6">
      <!-- Profile Info Card (Read-only) -->
      <ProfileInfoCard 
        v-if="profile" 
        :profile="profile" 
        @updated="loadProfile"
      />

      <!-- Edit Profile and Change Password - Side by side on larger screens -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Edit Profile Form (Editable Person fields) -->
        <EditProfileForm 
          v-if="profile && profile.person" 
          :profile="profile"
          :csrf-token="csrfToken"
          @updated="loadProfile" 
        />

        <!-- Change Password Form -->
        <ChangePasswordForm 
          v-if="profile"
          :allow-password-change="profile.allowPasswordChange"
          :has-local-password="profile.hasLocalPassword"
          :external-logins="profile.externalLogins"
          :csrf-token="csrfToken"
        />
      </div>

      <!-- Security Section (MFA) -->
      <div class="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
        <div class="px-6 py-4 border-b border-gray-200">
          <h2 class="text-lg font-medium text-gray-900">{{ t('mfa.securityTitle') || 'Security' }}</h2>
        </div>
        <MfaSettings @status-changed="loadProfile" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import ProfileInfoCard from './components/ProfileInfoCard.vue'
import EditProfileForm from './components/EditProfileForm.vue'
import ChangePasswordForm from './components/ChangePasswordForm.vue'
import MfaSettings from '../components/account/MfaSettings.vue'
import i18n from '../i18n'

const profile = ref(null)
const loading = ref(true)
const csrfToken = ref('')
const { t } = useI18n()

onMounted(() => {
  // Read CSRF token from data attribute
  const mountEl = document.getElementById('profile-app')
  if (mountEl?.dataset?.csrfToken) {
    csrfToken.value = mountEl.dataset.csrfToken
  }

  // Check URL query params for status messages (e.g. from External Account linking)
  const urlParams = new URLSearchParams(window.location.search)
  const error = urlParams.get('error')
  const success = urlParams.get('success')

  if (error) {
    console.log('[Profile] Error param:', error)
    console.log('[Profile] i18n locale:', i18n.global.locale.value)
    console.log('[Profile] Available messages:', Object.keys(i18n.global.messages.value[i18n.global.locale.value]))
    console.log('[Profile] Profile module:', i18n.global.messages.value[i18n.global.locale.value].profile)
    console.log('[Profile] Profile.common keys:', i18n.global.messages.value[i18n.global.locale.value].profile?.common ? Object.keys(i18n.global.messages.value[i18n.global.locale.value].profile.common) : 'N/A')
    
    if (error === 'LoginAlreadyAssociated') {
      const msg = t('profile.common.errors.loginAlreadyAssociated')
      console.log('[Profile] Resolved message:', msg)
      alert(msg || 'This account is already linked to another user.')
    } else if (error === 'LinkFailed') {
      const msg = t('profile.common.errors.linkFailed')
      console.log('[Profile] Resolved message:', msg)
      alert(msg || 'Failed to link account.')
    } else {
      const msg = t('profile.common.errors.unknown')
      console.log('[Profile] Resolved message:', msg)
      alert(msg || 'An error occurred.')
    }
  } else if (success) {
    console.log('[Profile] Success param:', success)
    if (success === 'LinkAdded') {
      const msg = t('profile.common.success.linkAdded')
      console.log('[Profile] Resolved message:', msg)
      alert(msg || 'Account linked successfully.')
    }
  }

  // Clean up URL
  if (error || success) {
    const newUrl = window.location.pathname
    window.history.replaceState({}, '', newUrl)
  }

  loadProfile()
})

const loadProfile = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/profile', {
      credentials: 'include'
    })
    
    if (res.ok) {
      profile.value = await res.json()
    } else if (res.status === 401) {
      // Redirect to login
      window.location.href = '/Account/Login?returnUrl=/Account/Profile'
    } else {
      console.error('Failed to load profile:', res.statusText)
    }
  } catch (error) {
    console.error('Failed to load profile:', error)
  } finally {
    loading.value = false
  }
}

// loadProfile defined below, onMounted calls it above
</script>
