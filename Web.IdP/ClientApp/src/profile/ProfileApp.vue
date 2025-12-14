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
      />

      <!-- Edit Profile and Change Password - Side by side on larger screens -->
      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <!-- Edit Profile Form (Editable Person fields) -->
        <EditProfileForm 
          v-if="profile && profile.person" 
          :profile="profile" 
          @updated="loadProfile" 
        />

        <!-- Change Password Form -->
        <ChangePasswordForm 
          v-if="profile"
          :allow-password-change="profile.allowPasswordChange"
          :has-local-password="profile.hasLocalPassword"
          :external-logins="profile.externalLogins"
        />
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

const { t } = useI18n()
const loading = ref(true)
const profile = ref(null)

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

onMounted(() => {
  loadProfile()
})
</script>
