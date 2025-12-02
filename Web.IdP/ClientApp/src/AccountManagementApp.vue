<template>
  <div class="account-management-container">
    <div class="account-management">
      <!-- Google-style Header -->
      <div class="page-header">
        <h1 class="page-title">{{ t('myAccount.title') }}</h1>
      </div>
      
      <!-- Loading State -->
      <div v-if="loading" class="loading-container">
        <div class="loading-spinner"></div>
        <p>{{ t('common.loading') }}</p>
      </div>

      <!-- Error State -->
      <div v-if="error" class="error-banner">
        <i class="bi bi-exclamation-circle"></i>
        <span>{{ error }}</span>
      </div>

      <!-- Content -->
      <div v-if="!loading && !error" class="content-grid">
        <!-- My Roles Section -->
        <div class="section-card">
          <div class="section-header">
            <h2 class="section-title">{{ t('myAccount.myRoles') }}</h2>
          </div>
          <role-list 
            :roles="roles" 
            :active-role-id="activeRoleId"
            @switch-role="handleSwitchRole"
          />
        </div>

        <!-- Linked Accounts Section -->
        <div v-if="linkedAccounts.length > 1" class="section-card">
          <div class="section-header">
            <h2 class="section-title">{{ t('myAccount.linkedAccounts') }}</h2>
          </div>
          <account-list 
            :accounts="linkedAccounts"
            @switch-account="handleSwitchAccount"
          />
        </div>
      </div>
    </div>

    <!-- Password Modal for Admin Role -->
    <password-modal
      v-model:show="showPasswordModal"
      :role-name="targetRoleName"
      @confirm="confirmRoleSwitch"
      @cancel="cancelRoleSwitch"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useI18n } from 'vue-i18n';
import RoleList from './components/account/RoleList.vue';
import AccountList from './components/account/AccountList.vue';
import PasswordModal from './components/account/PasswordModal.vue';
import { accountApi } from './services/accountApi';

const { t } = useI18n();

const loading = ref(true);
const error = ref(null);
const roles = ref([]);
const linkedAccounts = ref([]);
const showPasswordModal = ref(false);
const targetRoleId = ref(null);
const targetRoleName = ref('');

const activeRoleId = computed(() => {
  return roles.value.find(r => r.isActive)?.roleId || null;
});

onMounted(async () => {
  await loadData();
});

async function loadData() {
  loading.value = true;
  error.value = null;
  
  try {
    const [rolesData, accountsData] = await Promise.all([
      accountApi.getMyRoles(),
      accountApi.getMyAccounts()
    ]);
    
    roles.value = rolesData;
    linkedAccounts.value = accountsData;
  } catch (err) {
    error.value = err.message || t('myAccount.errors.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function handleSwitchRole(role) {
  if (role.isActive) {
    return; // Already active
  }
  
  // Check if password is required (Admin role)
  if (role.requiresPasswordConfirmation) {
    targetRoleId.value = role.roleId;
    targetRoleName.value = role.roleName;
    showPasswordModal.value = true;
  } else {
    await performRoleSwitch(role.roleId, null);
  }
}

async function confirmRoleSwitch(password) {
  if (targetRoleId.value) {
    await performRoleSwitch(targetRoleId.value, password);
  }
  showPasswordModal.value = false;
  targetRoleId.value = null;
  targetRoleName.value = '';
}

function cancelRoleSwitch() {
  showPasswordModal.value = false;
  targetRoleId.value = null;
  targetRoleName.value = '';
}

async function performRoleSwitch(roleId, password) {
  try {
    const result = await accountApi.switchRole(roleId, password);
    if (result.success) {
      // Reload page to refresh all claims and UI
      window.location.reload();
    } else {
      error.value = result.error || t('myAccount.errors.switchRoleFailed');
    }
  } catch (err) {
    error.value = err.message || t('myAccount.errors.switchRoleFailed');
  }
}

async function handleSwitchAccount(account) {
  if (confirm(t('myAccount.confirmSwitchAccount', { email: account.email }))) {
    try {
      const result = await accountApi.switchAccount(account.userId);
      if (result.success) {
        // Redirect to home after account switch
        window.location.href = '/';
      } else {
        error.value = result.error || t('myAccount.errors.switchAccountFailed');
      }
    } catch (err) {
      error.value = err.message || t('myAccount.errors.switchAccountFailed');
    }
  }
}
</script>

<style scoped>
/* Google Material Design Style */
.account-management-container {
  min-height: calc(100vh - 120px);
  background: #f5f5f5;
  padding: 24px 16px;
}

.account-management {
  max-width: 1200px;
  margin: 0 auto;
}

.page-header {
  padding: 32px 0 24px;
  margin-bottom: 24px;
}

.page-title {
  font-size: 32px;
  font-weight: 400;
  color: #202124;
  margin: 0;
  letter-spacing: 0;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.content-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}

.section-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 0;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 1px 3px 1px rgba(60,64,67,.15);
  overflow: hidden;
  transition: box-shadow 0.2s cubic-bezier(0.4, 0.0, 0.2, 1);
}

.section-card:hover {
  box-shadow: 0 1px 3px 0 rgba(60,64,67,.3), 0 4px 8px 3px rgba(60,64,67,.15);
}

.section-header {
  padding: 20px 24px;
  border-bottom: 1px solid #e8eaed;
}

.section-title {
  font-size: 16px;
  font-weight: 500;
  color: #202124;
  margin: 0;
  letter-spacing: 0.25px;
  font-family: 'Google Sans', 'Roboto', sans-serif;
}

.loading-container {
  text-align: center;
  padding: 64px 24px;
  background: #ffffff;
  border-radius: 8px;
  box-shadow: 0 1px 2px 0 rgba(60,64,67,.3), 0 1px 3px 1px rgba(60,64,67,.15);
}

.loading-container p {
  color: #5f6368;
  font-size: 14px;
  margin-top: 16px;
  font-family: 'Roboto', sans-serif;
}

.loading-spinner {
  border: 3px solid #e8eaed;
  border-top: 3px solid #1a73e8;
  border-radius: 50%;
  width: 40px;
  height: 40px;
  animation: spin 0.8s linear infinite;
  margin: 0 auto;
}

.error-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  background: #fce8e6;
  color: #c5221f;
  padding: 16px 24px;
  border-radius: 8px;
  margin-bottom: 16px;
  font-size: 14px;
  font-family: 'Roboto', sans-serif;
}

.error-banner i {
  font-size: 20px;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

@media (min-width: 1024px) {
  .content-grid {
    grid-template-columns: repeat(auto-fit, minmax(500px, 1fr));
  }
}

@media (max-width: 768px) {
  .account-management-container {
    padding: 16px 8px;
  }
  
  .page-header {
    padding: 24px 0 16px;
  }
  
  .page-title {
    font-size: 24px;
  }
  
  .section-header {
    padding: 16px 16px;
  }
}
</style>
