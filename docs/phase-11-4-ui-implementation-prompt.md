# Phase 11.4 UI Implementation Plan - AI Agent Prompt

## Context & Current State

You are continuing work on the HybridIdP project, specifically implementing **Phase 11.4: Vue.js UI for Account & Role Management**. 

### What's Already Done (Phase 11.1-11.3):
✅ **Phase 11.1**: Database schema with `UserSession.ActiveRoleId` (NOT NULL), migrations applied for SQL Server and PostgreSQL  
✅ **Phase 11.2**: `AccountManagementService` fully implemented with 12/12 tests passing, `SelectRole` page for multi-role users  
✅ **Phase 11.3**: REST API endpoints created in `MyAccountController`:
- `GET /api/my/accounts` - Get linked accounts
- `GET /api/my/roles` - Get available roles with active flag
- `POST /api/my/switch-role` - Switch roles (requires password for Admin)
- `POST /api/my/switch-account` - Switch to linked account (same Person only)

✅ **Phase 11.4 (Backend)**: Authorization updated to check only active role:
- `PermissionAuthorizationHandler` checks `active_role` claim
- `MyUserClaimsPrincipalFactory` adds `active_role` claim
- `SelectRole.cshtml.cs` updates claims after role selection
- `MyAccountController.SwitchRole` updates claims when switching

### Architecture Decisions Confirmed:
- **One User Account = Multiple Roles**: User can have multiple assigned roles but only ONE active role per session
- **Role Selection**: Multi-role users see `SelectRole` page after login to choose active role
- **Role Badge**: Active role displayed persistently in navigation bar (all pages)
- **Account Switching**: Users with same PersonId can switch between accounts (e.g., professor has faculty + student accounts)

### Technology Stack:
- **Frontend**: Vue 3 with Composition API, TypeScript
- **Build**: Vite (hot reload working on localhost:5173)
- **State**: Pinia store for user state
- **i18n**: vue-i18n for localization (Chinese Traditional + English)
- **HTTP**: Axios with CSRF token handling
- **UI**: Existing components in `Web.IdP/ClientApp/src/components/`

---

## Your Mission: Implement Phase 11.4 UI

### Goal
Create a self-service UI for users to:
1. View their linked accounts (same Person)
2. See their available roles
3. Switch between roles (with password for Admin role)
4. Switch between linked accounts
5. See their current active role in navigation bar at all times

### Deliverables

#### 1. Create My Account Page (Razor + Vue)

**File: `Web.IdP/Pages/MyAccount.cshtml`**
```cshtml
@page
@model Web.IdP.Pages.MyAccountModel
@{
    ViewData["Title"] = "My Account";
}

<div id="account-management-app" class="container">
    <account-management-app></account-management-app>
</div>

@section Scripts {
    <script type="module" src="~/dist/accountManagement.js"></script>
}
```

**File: `Web.IdP/Pages/MyAccount.cshtml.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.IdP.Pages;

[Authorize]
public class MyAccountModel : PageModel
{
    public void OnGet()
    {
    }
}
```

#### 2. Create Vue.js Entry Point

**File: `Web.IdP/ClientApp/src/accountManagement.ts`**
```typescript
import { createApp } from 'vue';
import { createPinia } from 'pinia';
import { createI18n } from 'vue-i18n';
import AccountManagementApp from './apps/AccountManagementApp.vue';
import { en, zhTW } from './i18n/messages';

const pinia = createPinia();

const i18n = createI18n({
  legacy: false,
  locale: document.documentElement.lang || 'zh-TW',
  fallbackLocale: 'en',
  messages: {
    en,
    'zh-TW': zhTW
  }
});

const app = createApp(AccountManagementApp);
app.use(pinia);
app.use(i18n);
app.mount('#account-management-app');
```

**Update `Web.IdP/ClientApp/vite.config.ts`** to add new entry point:
```typescript
export default defineConfig({
  // ... existing config
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'src/main.ts'),
        accountManagement: resolve(__dirname, 'src/accountManagement.ts') // ADD THIS
      },
      output: {
        entryFileNames: '[name].js',
        chunkFileNames: '[name]-[hash].js'
      }
    }
  }
});
```

#### 3. Create Main Vue App Component

**File: `Web.IdP/ClientApp/src/apps/AccountManagementApp.vue`**
```vue
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
import RoleList from '../components/account/RoleList.vue';
import AccountList from '../components/account/AccountList.vue';
import PasswordModal from '../components/account/PasswordModal.vue';
import { accountApi } from '../services/accountApi';
import type { AvailableRoleDto, LinkedAccountDto } from '../types/account';

const { t } = useI18n();

const loading = ref(true);
const error = ref<string | null>(null);
const roles = ref<AvailableRoleDto[]>([]);
const linkedAccounts = ref<LinkedAccountDto[]>([]);
const showPasswordModal = ref(false);
const targetRoleId = ref<string | null>(null);
const targetRoleName = ref<string>('');

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
  } catch (err: any) {
    error.value = err.message || t('myAccount.errors.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function handleSwitchRole(role: AvailableRoleDto) {
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

async function confirmRoleSwitch(password: string) {
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

async function performRoleSwitch(roleId: string, password: string | null) {
  try {
    const result = await accountApi.switchRole(roleId, password);
    if (result.success) {
      // Reload page to refresh all claims and UI
      window.location.reload();
    } else {
      error.value = result.error || t('myAccount.errors.switchRoleFailed');
    }
  } catch (err: any) {
    error.value = err.message || t('myAccount.errors.switchRoleFailed');
  }
}

async function handleSwitchAccount(account: LinkedAccountDto) {
  if (confirm(t('myAccount.confirmSwitchAccount', { email: account.email }))) {
    try {
      const result = await accountApi.switchAccount(account.userId);
      if (result.success) {
        // Redirect to home after account switch
        window.location.href = '/';
      } else {
        error.value = result.error || t('myAccount.errors.switchAccountFailed');
      }
    } catch (err: any) {
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
```

#### 4. Create Child Components

**File: `Web.IdP/ClientApp/src/components/account/RoleList.vue`**
```vue
<template>
  <div class="role-list">
    <div 
      v-for="role in roles" 
      :key="role.roleId"
      class="role-card"
      :class="{ 'active': role.isActive }"
    >
      <div class="role-info">
        <h4>
          {{ role.roleName }}
          <span v-if="role.isActive" class="badge bg-success">{{ t('myAccount.active') }}</span>
        </h4>
        <p v-if="role.description" class="text-muted">{{ role.description }}</p>
      </div>
      <div class="role-actions">
        <button
          v-if="!role.isActive"
          class="btn btn-primary"
          @click="$emit('switchRole', role)"
        >
          {{ t('myAccount.switchToRole') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import type { AvailableRoleDto } from '../../types/account';

defineProps<{
  roles: AvailableRoleDto[];
  activeRoleId: string | null;
}>();

defineEmits<{
  switchRole: [role: AvailableRoleDto];
}>();

const { t } = useI18n();
</script>

<style scoped>
.role-list {
  display: grid;
  gap: 1rem;
}

.role-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  transition: all 0.3s;
}

.role-card.active {
  border-color: #28a745;
  background-color: #f0f9f4;
}

.role-card:hover:not(.active) {
  border-color: #3498db;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

.role-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.role-info p {
  margin: 0;
  font-size: 0.9rem;
}
</style>
```

**File: `Web.IdP/ClientApp/src/components/account/AccountList.vue`**
```vue
<template>
  <div class="account-list">
    <div 
      v-for="account in accounts" 
      :key="account.userId"
      class="account-card"
      :class="{ 'current': account.isCurrent }"
    >
      <div class="account-info">
        <h4>
          {{ account.email }}
          <span v-if="account.isCurrent" class="badge bg-info">{{ t('myAccount.currentAccount') }}</span>
        </h4>
        <p class="text-muted">{{ t('myAccount.username') }}: {{ account.username }}</p>
      </div>
      <div class="account-actions">
        <button
          v-if="!account.isCurrent"
          class="btn btn-secondary"
          @click="$emit('switchAccount', account)"
        >
          {{ t('myAccount.switchToAccount') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import type { LinkedAccountDto } from '../../types/account';

defineProps<{
  accounts: LinkedAccountDto[];
}>();

defineEmits<{
  switchAccount: [account: LinkedAccountDto];
}>();

const { t } = useI18n();
</script>

<style scoped>
.account-list {
  display: grid;
  gap: 1rem;
}

.account-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  transition: all 0.3s;
}

.account-card.current {
  border-color: #17a2b8;
  background-color: #f0f8ff;
}

.account-card:hover:not(.current) {
  border-color: #6c757d;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}

.account-info h4 {
  margin: 0 0 0.5rem 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.account-info p {
  margin: 0;
  font-size: 0.9rem;
}
</style>
```

**File: `Web.IdP/ClientApp/src/components/account/PasswordModal.vue`**
```vue
<template>
  <div v-if="show" class="modal-overlay" @click.self="$emit('cancel')">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header">
          <h5>{{ t('myAccount.passwordRequired') }}</h5>
          <button type="button" class="btn-close" @click="$emit('cancel')"></button>
        </div>
        <div class="modal-body">
          <p>{{ t('myAccount.passwordRequiredDesc', { role: roleName }) }}</p>
          <div class="form-group">
            <label for="password">{{ t('myAccount.password') }}</label>
            <input
              id="password"
              v-model="password"
              type="password"
              class="form-control"
              :placeholder="t('myAccount.enterPassword')"
              @keyup.enter="handleConfirm"
              ref="passwordInput"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="$emit('cancel')">
            {{ t('common.cancel') }}
          </button>
          <button 
            type="button" 
            class="btn btn-primary" 
            :disabled="!password"
            @click="handleConfirm"
          >
            {{ t('common.confirm') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick } from 'vue';
import { useI18n } from 'vue-i18n';

const props = defineProps<{
  show: boolean;
  roleName: string;
}>();

const emit = defineEmits<{
  confirm: [password: string];
  cancel: [];
}>();

const { t } = useI18n();
const password = ref('');
const passwordInput = ref<HTMLInputElement>();

watch(() => props.show, async (newVal) => {
  if (newVal) {
    password.value = '';
    await nextTick();
    passwordInput.value?.focus();
  }
});

function handleConfirm() {
  if (password.value) {
    emit('confirm', password.value);
    password.value = '';
  }
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1050;
}

.modal-dialog {
  max-width: 500px;
  width: 90%;
}

.modal-content {
  background: white;
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid #dee2e6;
}

.modal-body {
  padding: 1.5rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem 1.5rem;
  border-top: 1px solid #dee2e6;
}

.form-group {
  margin-top: 1rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
}
</style>
```

#### 5. Create API Service

**File: `Web.IdP/ClientApp/src/services/accountApi.ts`**
```typescript
import axios from 'axios';
import type { 
  AvailableRoleDto, 
  LinkedAccountDto, 
  SwitchRoleRequest, 
  SwitchRoleResponse,
  SwitchAccountRequest,
  SwitchAccountResponse 
} from '../types/account';

const api = axios.create({
  baseURL: '/api/my',
  headers: {
    'Content-Type': 'application/json'
  }
});

// Add CSRF token to all requests
api.interceptors.request.use((config) => {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.getAttribute('value');
  if (token) {
    config.headers['RequestVerificationToken'] = token;
  }
  return config;
});

export const accountApi = {
  async getMyRoles(): Promise<AvailableRoleDto[]> {
    const { data } = await api.get<AvailableRoleDto[]>('/roles');
    return data;
  },

  async getMyAccounts(): Promise<LinkedAccountDto[]> {
    const { data } = await api.get<LinkedAccountDto[]>('/accounts');
    return data;
  },

  async switchRole(roleId: string, password: string | null): Promise<SwitchRoleResponse> {
    const request: SwitchRoleRequest = {
      roleId,
      password
    };
    const { data } = await api.post<SwitchRoleResponse>('/switch-role', request);
    return data;
  },

  async switchAccount(targetAccountId: string, reason?: string): Promise<SwitchAccountResponse> {
    const request: SwitchAccountRequest = {
      targetAccountId,
      reason
    };
    const { data } = await api.post<SwitchAccountResponse>('/switch-account', request);
    return data;
  }
};
```

#### 6. Create TypeScript Types

**File: `Web.IdP/ClientApp/src/types/account.ts`**
```typescript
export interface AvailableRoleDto {
  roleId: string;
  roleName: string;
  description?: string;
  isActive: boolean;
  requiresPasswordConfirmation: boolean;
}

export interface LinkedAccountDto {
  userId: string;
  username: string;
  email: string;
  isCurrent: boolean;
}

export interface SwitchRoleRequest {
  roleId: string;
  password: string | null;
}

export interface SwitchRoleResponse {
  success: boolean;
  newRoleId?: string;
  error?: string;
}

export interface SwitchAccountRequest {
  targetAccountId: string;
  reason?: string;
}

export interface SwitchAccountResponse {
  success: boolean;
  newAccountId?: string;
  newAccountEmail?: string;
  error?: string;
}
```

#### 7. Add i18n Translations

**Update `Web.IdP/ClientApp/src/i18n/messages.ts`:**
```typescript
// Add to zhTW (Traditional Chinese)
export const zhTW = {
  // ... existing translations
  myAccount: {
    title: '我的帳戶',
    myRoles: '我的角色',
    linkedAccounts: '關聯帳戶',
    active: '目前',
    currentAccount: '目前帳戶',
    switchToRole: '切換至此角色',
    switchToAccount: '切換至此帳戶',
    username: '使用者名稱',
    passwordRequired: '需要密碼確認',
    passwordRequiredDesc: '切換至「{role}」角色需要輸入密碼以確認身份',
    password: '密碼',
    enterPassword: '請輸入您的密碼',
    confirmSwitchAccount: '確定要切換至帳戶 {email} 嗎？',
    errors: {
      loadFailed: '載入帳戶資訊失敗',
      switchRoleFailed: '切換角色失敗',
      switchAccountFailed: '切換帳戶失敗'
    }
  }
};

// Add to en (English)
export const en = {
  // ... existing translations
  myAccount: {
    title: 'My Account',
    myRoles: 'My Roles',
    linkedAccounts: 'Linked Accounts',
    active: 'Active',
    currentAccount: 'Current',
    switchToRole: 'Switch to Role',
    switchToAccount: 'Switch to Account',
    username: 'Username',
    passwordRequired: 'Password Required',
    passwordRequiredDesc: 'Switching to "{role}" role requires password confirmation',
    password: 'Password',
    enterPassword: 'Enter your password',
    confirmSwitchAccount: 'Switch to account {email}?',
    errors: {
      loadFailed: 'Failed to load account information',
      switchRoleFailed: 'Failed to switch role',
      switchAccountFailed: 'Failed to switch account'
    }
  }
};
```

#### 8. Create Role Badge Component (Navigation Bar)

**File: `Web.IdP/ClientApp/src/components/navigation/RoleBadge.vue`**
```vue
<template>
  <div v-if="activeRole" class="role-badge">
    <span class="role-icon">👤</span>
    <span class="role-name">{{ activeRole }}</span>
    <a :href="myAccountUrl" class="role-link" :title="t('myAccount.switchRole')">
      <span class="switch-icon">🔄</span>
    </a>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';

const { t } = useI18n();
const activeRole = ref<string | null>(null);
const myAccountUrl = '/myaccount';

onMounted(() => {
  // Read active role from meta tag or data attribute
  const metaTag = document.querySelector('meta[name="active-role"]');
  if (metaTag) {
    activeRole.value = metaTag.getAttribute('content');
  }
});
</script>

<style scoped>
.role-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border-radius: 20px;
  font-size: 0.9rem;
  font-weight: 500;
  box-shadow: 0 2px 8px rgba(102, 126, 234, 0.3);
}

.role-icon {
  font-size: 1.1rem;
}

.role-name {
  letter-spacing: 0.5px;
}

.role-link {
  display: flex;
  align-items: center;
  color: white;
  text-decoration: none;
  padding: 0.25rem;
  border-radius: 50%;
  transition: background 0.2s;
}

.role-link:hover {
  background: rgba(255, 255, 255, 0.2);
}

.switch-icon {
  font-size: 1rem;
  display: block;
}
</style>
```

**Update `Web.IdP/Views/Shared/_Layout.cshtml` to add role badge:**
```cshtml
<!-- In the <head> section, add active role meta tag -->
@if (User.Identity?.IsAuthenticated == true)
{
    var activeRole = User.Claims.FirstOrDefault(c => c.Type == "active_role")?.Value 
                     ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
    <meta name="active-role" content="@activeRole" />
}

<!-- In the navigation bar, add role badge -->
@if (User.Identity?.IsAuthenticated == true)
{
    <div id="role-badge-container"></div>
    <script type="module">
      import { createApp } from '/dist/roleBadge.js';
      // Initialize role badge component
    </script>
}
```

#### 9. Update Vite Build Config

Make sure `vite.config.ts` builds both entry points:
```typescript
input: {
  main: resolve(__dirname, 'src/main.ts'),
  accountManagement: resolve(__dirname, 'src/accountManagement.ts'),
  roleBadge: resolve(__dirname, 'src/roleBadge.ts')
}
```

#### 10. Add Navigation Menu Link

**Update navigation menu to add "My Account" link:**
```cshtml
<li class="nav-item">
    <a class="nav-link" asp-page="/MyAccount">
        <i class="bi bi-person-circle"></i> @Localizer["Navigation.MyAccount"]
    </a>
</li>
```

---

## Implementation Steps

### Step 1: Create Directory Structure
```
Web.IdP/ClientApp/src/
├── apps/
│   └── AccountManagementApp.vue
├── components/
│   ├── account/
│   │   ├── RoleList.vue
│   │   ├── AccountList.vue
│   │   └── PasswordModal.vue
│   └── navigation/
│       └── RoleBadge.vue
├── services/
│   └── accountApi.ts
├── types/
│   └── account.ts
├── accountManagement.ts
└── roleBadge.ts
```

### Step 2: Create Razor Page
- Create `Web.IdP/Pages/MyAccount.cshtml`
- Create `Web.IdP/Pages/MyAccount.cshtml.cs`

### Step 3: Build Vue Components
- Create all Vue components in order: PasswordModal → RoleList → AccountList → AccountManagementApp
- Create API service and TypeScript types
- Add i18n translations

### Step 4: Update Build Configuration
- Update `vite.config.ts` with new entry points
- Test hot reload: `npm run dev`
- Build for production: `npm run build`

### Step 5: Integrate with Layout
- Add role badge to `_Layout.cshtml`
- Add navigation menu link
- Test responsiveness

### Step 6: Testing
- Manual testing: Login with multi-role user → Select role → View My Account page → Switch roles
- Test password prompt for Admin role
- Test account switching for users with same PersonId
- Test role badge display on all pages

---

## Testing Checklist

### Functional Tests
- [ ] Single-role user: Auto-login without SelectRole page
- [ ] Multi-role user: See SelectRole page after login
- [ ] My Account page loads successfully
- [ ] Role list displays all assigned roles with active flag
- [ ] Switch to non-Admin role: Works without password
- [ ] Switch to Admin role: Shows password modal
- [ ] Password modal: Cancel button closes modal
- [ ] Password modal: Confirm with correct password switches role
- [ ] Password modal: Confirm with wrong password shows error
- [ ] Account list shows only when user has multiple accounts (same PersonId)
- [ ] Switch account: Redirects to home page with new account
- [ ] Role badge: Displays active role on all pages
- [ ] Role badge: Click redirects to My Account page

### UI/UX Tests
- [ ] Loading spinner shows during API calls
- [ ] Error messages display clearly
- [ ] Active role/account visually distinguished
- [ ] Buttons disabled when operation in progress
- [ ] Responsive design works on mobile
- [ ] i18n: All text translated (zh-TW and en)
- [ ] Accessibility: Keyboard navigation works
- [ ] Accessibility: Screen reader friendly

### Edge Cases
- [ ] User with no roles: Should not happen (enforced by business logic)
- [ ] User with one role: No role selection needed
- [ ] User attempts to switch to role not assigned: Blocked by API
- [ ] User attempts to switch to account with different PersonId: Blocked by API
- [ ] Network error during role switch: Error message displayed
- [ ] Session expires during operation: Redirects to login

---

## Common Pitfalls & Solutions

### Issue: CSRF Token Missing
**Solution**: Ensure `accountApi.ts` adds CSRF token from hidden input field:
```typescript
const token = document.querySelector('input[name="__RequestVerificationToken"]')?.getAttribute('value');
```

### Issue: Vite Build Not Including New Entry Point
**Solution**: Clear Vite cache and rebuild:
```bash
cd Web.IdP/ClientApp
rm -rf node_modules/.vite
npm run build
```

### Issue: Role Badge Not Showing
**Solution**: Check meta tag in `_Layout.cshtml` and ensure `roleBadge.ts` entry point is built.

### Issue: Page Reload After Role Switch Not Working
**Solution**: Use `window.location.reload()` instead of router navigation to ensure server re-generates all claims.

---

## Success Criteria

✅ User can view all their assigned roles with active role highlighted  
✅ User can switch between roles (with password for Admin)  
✅ User can view linked accounts (same PersonId)  
✅ User can switch between linked accounts  
✅ Role badge displays active role persistently in navigation  
✅ All text localized in Chinese Traditional and English  
✅ Responsive design works on desktop, tablet, mobile  
✅ Error handling with user-friendly messages  
✅ Unit tests pass (if applicable)  

---

## Timeline Estimate
- **Step 1-2**: Directory structure and Razor pages (30 min)
- **Step 3**: Vue components and API service (2-3 hours)
- **Step 4**: Build configuration (30 min)
- **Step 5**: Layout integration (1 hour)
- **Step 6**: Testing and bug fixes (1-2 hours)
- **Total**: 5-7 hours

---

## Quick Start Commands

```bash
# Terminal 1: Start Vite dev server
cd Web.IdP/ClientApp
npm run dev

# Terminal 2: Start ASP.NET Core
cd Web.IdP
dotnet run --launch-profile https

# Access:
# - IdP: https://localhost:7139
# - My Account: https://localhost:7139/myaccount
# - Vite HMR: http://localhost:5173
```

---

## Notes
- All backend APIs already working (Phase 11.3 complete)
- Authorization already updated (Phase 11.4 backend complete)
- Focus on UI/UX and user experience
- Follow existing Vue 3 patterns in the codebase
- Use existing Bootstrap 5 styles
- Maintain consistency with current IdP design

Good luck! 🚀
