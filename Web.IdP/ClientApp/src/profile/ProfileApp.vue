<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Header -->
    <header class="bg-white shadow-sm">
      <div class="max-w-4xl mx-auto px-4 py-4">
        <h1 class="text-2xl font-bold text-gray-900">
          {{ t('profile.title') }}
        </h1>
      </div>
    </header>

    <!-- Main Content -->
    <main class="max-w-4xl mx-auto px-4 py-6">
      <div v-loading="{ loading, overlay: true, message: t('profile.loading') }">
        <!-- Profile Info Card (Read-only) -->
        <ProfileInfoCard 
          v-if="profile" 
          :profile="profile" 
        />

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
    </main>
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
