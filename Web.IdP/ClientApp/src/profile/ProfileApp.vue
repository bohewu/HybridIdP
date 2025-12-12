<template>
  <div class="container py-4"
       v-loading="{ loading, overlay: true, message: t('profile.loading') }">
    <!-- Page Header -->
    <div class="row mb-4">
      <div class="col">
        <h1 class="h2">{{ t('profile.title') }}</h1>
      </div>
    </div>

    <!-- Main Content -->
    <div v-if="!loading" class="row">
      <div class="col-lg-8">
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
