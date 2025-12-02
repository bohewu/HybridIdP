<template>
  <div class="account-management-container">
    <div class="account-management">
      <div class="page-header">
        <div class="header-icon">👤</div>
        <h1 class="page-title">{{ t('myAccount.title') }}</h1>
      </div>
      
      <!-- Loading State -->
      <div v-if="loading" class="loading">
        <div class="spinner"></div>
        <p>{{ t('common.loading') }}</p>
      </div>

      <!-- Error State -->
      <div v-if="error" class="alert alert-danger">
        <i class="bi bi-exclamation-triangle-fill me-2"></i>
        {{ error }}
      </div>

      <!-- Content -->
      <div v-if="!loading && !error" class="content-sections">
        <!-- My Roles Section -->
        <section class="section-card">
          <div class="section-header">
            <div class="section-icon">🎭</div>
            <h2 class="section-title">{{ t('myAccount.myRoles') }}</h2>
          </div>
          <role-list 
            :roles="roles" 
            :active-role-id="activeRoleId"
            @switch-role="handleSwitchRole"
          />
        </section>

        <!-- Linked Accounts Section -->
        <section v-if="linkedAccounts.length > 1" class="section-card">
          <div class="section-header">
            <div class="section-icon">🔗</div>
            <h2 class="section-title">{{ t('myAccount.linkedAccounts') }}</h2>
          </div>
          <account-list 
            :accounts="linkedAccounts"
            @switch-account="handleSwitchAccount"
          />
        </section>
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
.account-management-container {
  min-height: calc(100vh - 200px);
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  padding: 3rem 1rem;
}

.account-management {
  max-width: 1000px;
  margin: 0 auto;
}

.page-header {
  text-align: center;
  margin-bottom: 3rem;
  animation: fadeInDown 0.6s ease-out;
}

.header-icon {
  font-size: 4rem;
  margin-bottom: 1rem;
  filter: drop-shadow(0 4px 8px rgba(0,0,0,0.1));
}

.page-title {
  font-size: 2.5rem;
  font-weight: 700;
  color: white;
  text-shadow: 0 2px 4px rgba(0,0,0,0.2);
  margin: 0;
}

.content-sections {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.section-card {
  background: white;
  border-radius: 16px;
  padding: 2rem;
  box-shadow: 0 10px 30px rgba(0,0,0,0.15);
  animation: fadeInUp 0.6s ease-out;
  transition: transform 0.3s, box-shadow 0.3s;
}

.section-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 15px 40px rgba(0,0,0,0.2);
}

.section-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1.5rem;
  padding-bottom: 1rem;
  border-bottom: 2px solid #f0f0f0;
}

.section-icon {
  font-size: 2rem;
  line-height: 1;
}

.section-title {
  font-size: 1.5rem;
  font-weight: 600;
  color: #333;
  margin: 0;
}

.loading {
  text-align: center;
  padding: 4rem;
  background: white;
  border-radius: 16px;
  box-shadow: 0 10px 30px rgba(0,0,0,0.15);
}

.loading p {
  color: #666;
  font-size: 1.1rem;
  margin-top: 1rem;
}

.spinner {
  border: 5px solid #f3f3f3;
  border-top: 5px solid #667eea;
  border-radius: 50%;
  width: 60px;
  height: 60px;
  animation: spin 1s linear infinite;
  margin: 0 auto 1rem;
}

.alert {
  border-radius: 12px;
  padding: 1.5rem;
  animation: fadeIn 0.4s ease-out;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes fadeInDown {
  from {
    opacity: 0;
    transform: translateY(-30px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(30px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@media (max-width: 768px) {
  .account-management-container {
    padding: 2rem 0.5rem;
  }
  
  .page-title {
    font-size: 2rem;
  }
  
  .section-card {
    padding: 1.5rem;
  }
}
</style>
