<template>
  <div class="account-management">
    <h2>{{ t('myAccount.title') }}</h2>
    
    <!-- Loading State -->
    <div v-if="loading" class="loading">
      <div class="spinner"></div>
      <p>{{ t('common.loading') }}</p>
    </div>

    <!-- Error State -->
    <div v-if="error" class="alert alert-danger">
      {{ error }}
    </div>

    <!-- Content -->
    <div v-if="!loading && !error">
      <!-- My Roles Section -->
      <section class="mb-4">
        <h3>{{ t('myAccount.myRoles') }}</h3>
        <role-list 
          :roles="roles" 
          :active-role-id="activeRoleId"
          @switch-role="handleSwitchRole"
        />
      </section>

      <!-- Linked Accounts Section -->
      <section v-if="linkedAccounts.length > 1">
        <h3>{{ t('myAccount.linkedAccounts') }}</h3>
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
.account-management {
  max-width: 900px;
  margin: 0 auto;
  padding: 2rem;
}

.loading {
  text-align: center;
  padding: 3rem;
}

.spinner {
  border: 4px solid #f3f3f3;
  border-top: 4px solid #3498db;
  border-radius: 50%;
  width: 40px;
  height: 40px;
  animation: spin 1s linear infinite;
  margin: 0 auto 1rem;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}
</style>
