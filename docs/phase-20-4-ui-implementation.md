# Phase 20.4 - WebAuthn Passkey UI Implementation

**Status**: 📋 Planned  
**Goal**: Implement frontend UI for WebAuthn Passkey functionality with SecurityPolicy-based feature toggles.

---

## Overview

### Completed ✅
- **Backend API**: `PasskeyController.cs` (register-options, register, login-options, login)
- **Database**: `UserCredential` entity
- **Service Layer**: `IPasskeyService` interface

### To Implement 🚧
- **SecurityPolicy**: MFA feature toggles (EnableTotpMfa, EnableEmailMfa, EnablePasskey, MaxPasskeysPerUser)
- **Backend APIs**: List/Delete passkeys
- **Frontend**: Passkey管理 UI in `MfaSettings.vue`
- **Composable**: `useWebAuthn.js` (JavaScript, not TypeScript)
- **i18n**: Passkey translations (en-US, zh-TW)
- **Admin UI**: SecurityPolicy MFA settings

---

## Design Decisions

### 1. UI 位置
✅ **選擇**: 在現有 `MfaSettings.vue` 增加 Passkey section  
**原因**: 統一的 MFA 管理介面，使用者體驗更好

### 2. 登入頁設計
✅ **選擇**: 獨立按鈕 "Sign in with Passkey"  
**原因**: 清楚明確，符合業界標準（Google, Microsoft）

### 3. 裝置命名
✅ **選擇**: 自動產生 "Chrome on Windows - 2025/12/17"  
**原因**: 降低使用者負擔，自動產生有意義的名稱

### 4. 數量限制
✅ **選擇**: SecurityPolicy 可配置，預設 10 個  
**原因**: 管理員可彈性調整 (1-50)

---

## Implementation Steps

### Step 0: SecurityPolicy - MFA Feature Toggles

#### 0.1 Update SecurityPolicy Entity

```csharp
// Core.Domain/Entities/SecurityPolicy.cs
public class SecurityPolicy
{
    // ... existing fields ...
    
    /// <summary>
    /// Whether TOTP (Authenticator App) MFA is available for users to enable
    /// </summary>
    public bool EnableTotpMfa { get; set; } = true;
    
    /// <summary>
    /// Whether Email OTP MFA is available for users to enable
    /// </summary>
    public bool EnableEmailMfa { get; set; } = true;
    
    /// <summary>
    /// Whether Passkey (WebAuthn) authentication is available for users
    /// </summary>
    public bool EnablePasskey { get; set; } = true;
    
    /// <summary>
    /// Maximum number of passkeys a user can register (default: 10)
    /// </summary>
    public int MaxPasskeysPerUser { get; set; } = 10;
}
```

#### 0.2 Update SecurityPolicyDto

```csharp
// Core.Application/DTOs/SecurityPolicyDto.cs

public bool EnableTotpMfa { get; set; } = true;
public bool EnableEmailMfa { get; set; } = true;
public bool EnablePasskey { get; set; } = true;

[Range(1, 50, ErrorMessage = "Max passkeys must be between 1 and 50")]
public int MaxPasskeysPerUser { get; set; } = 10;
```

#### 0.3 Create Migrations

**SQL Server:**
```powershell
cd Infrastructure.Migrations.SqlServer
dotnet ef migrations add AddMfaFeatureToggles --startup-project ..\Web.IdP
cd ..
```

**PostgreSQL:**
```powershell
$env:DATABASE_PROVIDER="PostgreSQL"
cd Infrastructure.Migrations.Postgres
dotnet ef migrations add AddMfaFeatureToggles --startup-project ..\Web.IdP
cd ..
$env:DATABASE_PROVIDER=$null
```

#### 0.4 Apply Migrations

**SQL Server:**
```powershell
cd Infrastructure.Migrations.SqlServer
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
cd ..
```

**PostgreSQL:**
```powershell
$env:DATABASE_PROVIDER="PostgreSQL"
cd Infrastructure.Migrations.Postgres
dotnet ef database update --startup-project ..\Web.IdP --context ApplicationDbContext
cd ..
$env:DATABASE_PROVIDER=$null
```

---

### Step 1: Backend APIs

#### 1.1 Add List Passkeys API

```csharp
// Web.IdP/Controllers/Account/PasskeyController.cs

[HttpGet("list")]
[ApiAuthorize]
public async Task<IActionResult> ListPasskeys(CancellationToken ct)
{
    var user = await GetAuthenticatedUserAsync();
    if (user == null) return Unauthorized();
    
    var passkeys = await _passkeyService.GetUserPasskeysAsync(user.Id, ct);
    return Ok(passkeys);
}
```

#### 1.2 Add Delete Passkey API

```csharp
[HttpDelete("{id}")]
[ApiAuthorize]
public async Task<IActionResult> DeletePasskey(Guid id, CancellationToken ct)
{
    var user = await GetAuthenticatedUserAsync();
    if (user == null) return Unauthorized();
    
    var result = await _passkeyService.DeletePasskeyAsync(user.Id, id, ct);
    if (!result) return NotFound();
    
    return Ok();
}
```

#### 1.3 Implement PasskeyService Methods

```csharp
// Infrastructure/Services/PasskeyService.cs

public async Task<List<UserCredentialDto>> GetUserPasskeysAsync(Guid userId, CancellationToken ct)
{
    return await _dbContext.UserCredentials
        .Where(c => c.UserId == userId)
        .Select(c => new UserCredentialDto
        {
            Id = c.Id,
            DeviceName = c.DeviceName,
            CreatedAt = c.RegDate,
            LastUsedAt = c.LastUsedAt // Need to add this field
        })
        .ToListAsync(ct);
}

public async Task<bool> DeletePasskeyAsync(Guid userId, int credentialId, CancellationToken ct)
{
    var credential = await _dbContext.UserCredentials
        .FirstOrDefaultAsync(c => c.UserId == userId && c.Id == credentialId, ct);
    
    if (credential == null) return false;
    
    _dbContext.UserCredentials.Remove(credential);
    await _dbContext.SaveChangesAsync(ct);
    return true;
}
```

---

### Step 2: Frontend - useWebAuthn Composable

**File**: `src/composables/useWebAuthn.js` (JavaScript only, no TypeScript)

```javascript
// src/composables/useWebAuthn.js
import { ref } from 'vue';

export function useWebAuthn() {
  const isSupported = () => {
    return window.PublicKeyCredential !== undefined &&
           navigator.credentials !== undefined;
  };

  // Base64url decode
  const base64ToArrayBuffer = (base64) => {
    // Handle both base64 and base64url
    const base64url = base64.replace(/-/g, '+').replace(/_/g, '/');
    const binaryString = window.atob(base64url);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes.buffer;
  };

  // Base64url encode
  const arrayBufferToBase64 = (buffer) => {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    const base64 = window.btoa(binary);
    // Convert to base64url
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  };

  // Generate device name automatically
  const generateDeviceName = () => {
    const browser = getBrowserName();
    const os = getOSName();
    const date = new Date().toLocaleDateString('zh-TW');
    return `${browser} on ${os} - ${date}`;
  };

  const getBrowserName = () => {
    const ua = navigator.userAgent;
    if (ua.indexOf('Chrome') > -1) return 'Chrome';
    if (ua.indexOf('Safari') > -1) return 'Safari';
    if (ua.indexOf('Firefox') > -1) return 'Firefox';
    if (ua.indexOf('Edge') > -1) return 'Edge';
    return 'Browser';
  };

  const getOSName = () => {
    const ua = navigator.userAgent;
    if (ua.indexOf('Win') > -1) return 'Windows';
    if (ua.indexOf('Mac') > -1) return 'macOS';
    if (ua.indexOf('Linux') > -1) return 'Linux';
    if (ua.indexOf('Android') > -1) return 'Android';
    if (ua.indexOf('iOS') > -1 || ua.indexOf('iPhone') > -1) return 'iOS';
    return 'Unknown';
  };

  const registerPasskey = async () => {
    if (!isSupported()) {
      throw new Error('WebAuthn not supported');
    }

    // 1. Get options from server
    const optionsResp = await fetch('/api/passkey/register-options', {
      method: 'POST',
      credentials: 'include'
    });
    
    if (!optionsResp.ok) {
      throw new Error('Failed to get registration options');
    }
    
    const options = await optionsResp.json();
    
    // 2. Convert base64 to ArrayBuffer
    options.challenge = base64ToArrayBuffer(options.challenge);
    options.user.id = base64ToArrayBuffer(options.user.id);
    
    if (options.excludeCredentials) {
      options.excludeCredentials = options.excludeCredentials.map(cred => ({
        ...cred,
        id: base64ToArrayBuffer(cred.id)
      }));
    }
    
    // 3. Call WebAuthn API
    const credential = await navigator.credentials.create({
      publicKey: options
    });
    
    if (!credential) {
      throw new Error('No credential created');
    }
    
    // 4. Prepare response
    const attestationResponse = {
      id: credential.id,
      rawId: arrayBufferToBase64(credential.rawId),
      type: credential.type,
      response: {
        clientDataJSON: arrayBufferToBase64(credential.response.clientDataJSON),
        attestationObject: arrayBufferToBase64(credential.response.attestationObject)
      },
      deviceName: generateDeviceName() // Auto-generate device name
    };
    
    // 5. Send to server
    const registerResp = await fetch('/api/passkey/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(attestationResponse)
    });
    
    if (!registerResp.ok) {
      const error = await registerResp.json();
      throw new Error(error.error || 'Registration failed');
    }
    
    return await registerResp.json();
  };

  const authenticateWithPasskey = async (username) => {
    if (!isSupported()) {
      throw new Error('WebAuthn not supported');
    }

    // 1. Get assertion options
    const optionsResp = await fetch('/api/passkey/login-options', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username })
    });
    
    if (!optionsResp.ok) {
      throw new Error('Failed to get login options');
    }
    
    const options = await optionsResp.json();
    
    // 2. Convert base64 to ArrayBuffer
    options.challenge = base64ToArrayBuffer(options.challenge);
    
    if (options.allowCredentials) {
      options.allowCredentials = options.allowCredentials.map(cred => ({
        ...cred,
        id: base64ToArrayBuffer(cred.id)
      }));
    }
    
    // 3. Call WebAuthn API
    const assertion = await navigator.credentials.get({
      publicKey: options
    });
    
    if (!assertion) {
      throw new Error('No assertion created');
    }
    
    // 4. Prepare response
    const assertionResponse = {
      id: assertion.id,
      rawId: arrayBufferToBase64(assertion.rawId),
      type: assertion.type,
      response: {
        clientDataJSON: arrayBufferToBase64(assertion.response.clientDataJSON),
        authenticatorData: arrayBufferToBase64(assertion.response.authenticatorData),
        signature: arrayBufferToBase64(assertion.response.signature),
        userHandle: assertion.response.userHandle 
          ? arrayBufferToBase64(assertion.response.userHandle) 
          : null
      }
    };
    
    // 5. Send to server
    const loginResp = await fetch('/api/passkey/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(assertionResponse)
    });
    
    if (!loginResp.ok) {
      const error = await loginResp.json();
      throw new Error(error.error || 'Authentication failed');
    }
    
    return await loginResp.json();
  };

  return {
    isSupported,
    registerPasskey,
    authenticateWithPasskey
  };
}
```

---

### Step 3: Frontend - MfaSettings.vue Integration

在 `MfaSettings.vue` 中整合 Passkey 功能：

```vue
<script setup>
import { ref, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { useWebAuthn } from '@/composables/useWebAuthn';

const { t } = useI18n();
const { isSupported, registerPasskey } = useWebAuthn();

// Security Policy (feature flags)
const securityPolicy = ref({
  enableTotpMfa: true,
  enableEmailMfa: true,
  enablePasskey: true,
  maxPasskeysPerUser: 10
});

// Passkey state
const passkeys = ref([]);
const passkeyCount = ref(0);
const passkeyLoading = ref(false);
const passkeyError = ref('');
const passkeySuccess = ref('');

// Load security policy
async function loadSecurityPolicy() {
  try {
    const response = await fetch('/api/admin/security-policy', { credentials: 'include' });
    if (response.ok) {
      const policy = await response.json();
      securityPolicy.value = {
        enableTotpMfa: policy.enableTotpMfa ?? true,
        enableEmailMfa: policy.enableEmailMfa ?? true,
        enablePasskey: policy.enablePasskey ?? true,
        maxPasskeysPerUser: policy.maxPasskeysPerUser ?? 10
      };
    }
  } catch (err) {
    console.error('Failed to load security policy:', err);
  }
}

// Load passkeys
async function loadPasskeys() {
  try {
    const response = await fetch('/api/passkey/list', { credentials: 'include' });
    if (response.ok) {
      passkeys.value = await response.json();
      passkeyCount.value = passkeys.value.length;
    }
  } catch (err) {
    console.error('Failed to load passkeys:', err);
  }
}

// Add passkey
async function addPasskey() {
  if (!isSupported()) {
    passkeyError.value = t('mfa.passkey.notSupported');
    return;
  }
  
  if (passkeyCount.value >= securityPolicy.value.maxPasskeysPerUser) {
    passkeyError.value = t('mfa.passkey.maxLimitReached', { 
      max: securityPolicy.value.maxPasskeysPerUser 
    });
    return;
  }
  
  passkeyLoading.value = true;
  passkeyError.value = '';
  passkeySuccess.value = '';
  
  try {
    await registerPasskey();
    passkeySuccess.value = t('mfa.passkey.registrationSuccess');
    await loadPasskeys();
    setTimeout(() => { passkeySuccess.value = ''; }, 3000);
  } catch (err) {
    passkeyError.value = err.message || t('mfa.passkey.registrationFailed');
  } finally {
    passkeyLoading.value = false;
  }
}

// Delete passkey
async function deletePasskey(id) {
  if (!confirm(t('mfa.passkey.deleteConfirm'))) return;
  
  try {
    const response = await fetch(`/api/passkey/${id}`, {
      method: 'DELETE',
      credentials: 'include'
    });
    
    if (response.ok) {
      await loadPasskeys();
    } else {
      passkeyError.value = t('mfa.passkey.deleteFailed');
    }
  } catch (err) {
    passkeyError.value = err.message;
  }
}

// Format date
function formatDate(dateString) {
  if (!dateString) return '-';
  return new Date(dateString).toLocaleDateString('zh-TW');
}

onMounted(async () => {
  await loadMfaStatus(); // existing function
  await loadSecurityPolicy();
  
  if (securityPolicy.value.enablePasskey && isSupported()) {
    await loadPasskeys();
  }
});
</script>

<template>
  <div class="mfa-settings">
    <!-- Existing TOTP MFA Section -->
    <div v-if="securityPolicy.enableTotpMfa" class="mfa-status">
      <!-- ... existing TOTP UI ... -->
    </div>
    
    <!-- Existing Email MFA Section -->
    <div v-if="securityPolicy.enableEmailMfa" class="email-mfa-section">
      <!-- ... existing Email MFA UI ... -->
    </div>
    
    <!-- Passkey Section -->
    <div v-if="securityPolicy.enablePasskey && isSupported()" class="passkey-section">
      <div class="mfa-status">
        <div class="status-icon">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 2L2 7v5c0 5.5 3.84 10.74 9 12 5.16-1.26 9-6.5 9-12V7l-10-5z"></path>
            <path d="M12 11v3M12 17h.01"></path>
          </svg>
        </div>
        <div class="status-text">
          <h3>{{ t('mfa.passkey.title') }}</h3>
          <p>{{ t('mfa.passkey.description') }}</p>
          <p class="passkey-count">
            {{ passkeyCount }}/{{ securityPolicy.maxPasskeysPerUser }} 
            {{ t('mfa.passkey.registered') }}
          </p>
        </div>
        <button 
          class="btn-enable" 
          @click="addPasskey"
          :disabled="passkeyLoading || passkeyCount >= securityPolicy.maxPasskeysPerUser"
        >
          {{ passkeyLoading ? '...' : t('mfa.passkey.addPasskey') }}
        </button>
      </div>
      
      <!-- Passkey List -->
      <div v-if="passkeys.length > 0" class="passkey-list">
        <div v-for="passkey in passkeys" :key="passkey.id" class="passkey-item">
          <div class="passkey-info">
            <strong>{{ passkey.deviceName }}</strong>
            <span class="passkey-meta">
              {{ t('mfa.passkey.createdAt') }}: {{ formatDate(passkey.createdAt) }}
            </span>
          </div>
          <button class="btn-delete-small" @click="deletePasskey(passkey.id)">
            {{ t('mfa.passkey.delete') }}
          </button>
        </div>
      </div>
      
      <!-- Messages -->
      <p v-if="passkeyError" class="error-message">{{ passkeyError }}</p>
      <p v-if="passkeySuccess" class="success-message">{{ passkeySuccess }}</p>
      
      <!-- Max limit reached -->
      <p v-if="passkeyCount >= securityPolicy.maxPasskeysPerUser" class="warning-text">
        {{ t('mfa.passkey.maxLimitReached', { max: securityPolicy.maxPasskeysPerUser }) }}
      </p>
    </div>
  </div>
</template>

<style scoped>
/* Passkey Section */
.passkey-section {
  margin-top: 24px;
  padding-top: 24px;
  border-top: 1px solid #dadce0;
}

.passkey-count {
  margin-top: 4px;
  font-size: 12px;
  color: #5f6368;
}

.passkey-list {
  margin-top: 16px;
}

.passkey-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px;
  background: #f8f9fa;
  border-radius: 4px;
  margin-bottom: 8px;
}

.passkey-info {
  flex: 1;
}

.passkey-info strong {
  display: block;
  font-size: 14px;
  color: #202124;
  margin-bottom: 4px;
}

.passkey-meta {
  font-size: 12px;
  color: #5f6368;
}

.btn-delete-small {
  background: white;
  color: #c5221f;
  border: 1px solid #dadce0;
  padding: 6px 12px;
  border-radius: 4px;
  font-size: 12px;
  cursor: pointer;
}

.btn-delete-small:hover {
  background: #fce8e6;
}

.success-message {
  color: #137333;
  font-size: 13px;
  margin-top: 8px;
}
</style>
```

---

### Step 4: i18n Translations

#### English (en-US)

`src/i18n/locales/en-US/mfa.json`:

```json
{
  "passkey": {
    "title": "Passkeys",
    "description": "Sign in securely using your device's biometric or PIN",
    "addPasskey": "Add Passkey",
    "registered": "registered",
    "deviceName": "Device Name",
    "createdAt": "Created",
    "lastUsed": "Last Used",
    "delete": "Remove",
    "deleteConfirm": "Remove this passkey? You won't be able to use it to sign in.",
    "deleteFailed": "Failed to delete passkey",
    "signInWithPasskey": "Sign in with Passkey",
    "notSupported": "Passkeys are not supported on this device or browser",
    "registrationSuccess": "Passkey registered successfully",
    "registrationFailed": "Failed to register passkey",
    "authenticationFailed": "Passkey authentication failed",
    "listFailed": "Failed to load passkeys",
    "maxLimitReached": "Maximum limit reached ({max} passkeys)"
  }
}
```

#### Chinese Traditional (zh-TW)

`src/i18n/locales/zh-TW/mfa.json`:

```json
{
  "passkey": {
    "title": "Passkey 無密碼登入",
    "description": "使用裝置的生物辨識或 PIN 碼安全登入",
    "addPasskey": "新增 Passkey",
    "registered": "個已註冊",
    "deviceName": "裝置名稱",
    "createdAt": "建立時間",
    "lastUsed": "最後使用",
    "delete": "移除",
    "deleteConfirm": "確定要移除此 Passkey？您將無法再使用它登入。",
    "deleteFailed": "刪除 Passkey 失敗",
    "signInWithPasskey": "使用 Passkey 登入",
    "notSupported": "此裝置或瀏覽器不支援 Passkey",
    "registrationSuccess": "Passkey 註冊成功",
    "registrationFailed": "Passkey 註冊失敗",
    "authenticationFailed": "Passkey 驗證失敗",
    "listFailed": "無法載入 Passkey 列表",
    "maxLimitReached": "已達到上限（最多 {max} 個）"
  }
}
```

---

### Step 5: Admin UI - Security Settings

在 Admin 的 Security Settings 頁面加入 MFA 功能開關 (待定位置)：

```vue
<!-- Admin/Security/SecuritySettingsApp.vue -->
<template>
  <div class="security-settings">
    <h2>安全性設定</h2>
    
    <!-- MFA Feature Toggles -->
    <section class="settings-section">
      <h3>多因素驗證 (MFA) 功能</h3>
      
      <div class="setting-row">
        <label class="checkbox-label">
          <input type="checkbox" v-model="policy.enableTotpMfa" />
          <span>啟用 TOTP（驗證器應用程式）</span>
        </label>
        <p class="setting-description">
          允許使用者使用 Google Authenticator、Authy 等驗證器應用程式進行雙因素驗證
        </p>
      </div>
      
      <div class="setting-row">
        <label class="checkbox-label">
          <input type="checkbox" v-model="policy.enableEmailMfa" />
          <span>啟用 Email OTP（電子郵件驗證碼）</span>
        </label>
        <p class="setting-description">
          允許使用者透過電子郵件接收一次性驗證碼進行雙因素驗證
        </p>
      </div>
      
      <div class="setting-row">
        <label class="checkbox-label">
          <input type="checkbox" v-model="policy.enablePasskey" />
          <span>啟用 Passkey（WebAuthn 生物辨識）</span>
        </label>
        <p class="setting-description">
          允許使用者使用 Windows Hello、Touch ID、Face ID 等生物辨識方式登入
        </p>
      </div>
      
      <div v-if="policy.enablePasskey" class="setting-row indent">
        <label>
          <span class="label-text">每位使用者最多可註冊 Passkey 數量</span>
          <input 
            type="number" 
            v-model.number="policy.maxPasskeysPerUser" 
            min="1" 
            max="50" 
            class="number-input"
          />
        </label>
        <p class="setting-description">
          建議值：5-10 個（包含手機、電腦、平板等裝置）
        </p>
      </div>
    </section>
    
    <button class="btn-primary" @click="savePolicy">儲存設定</button>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';

const policy = ref({
  enableTotpMfa: true,
  enableEmailMfa: true,
  enablePasskey: true,
  maxPasskeysPerUser: 10
});

async function loadPolicy() {
  const response = await fetch('/api/admin/security-policy');
  if (response.ok) {
    policy.value = await response.json();
  }
}

async function savePolicy() {
  const response = await fetch('/api/admin/security-policy', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(policy.value)
  });
  
  if (response.ok) {
    alert('設定已儲存');
  }
}

onMounted(() => {
  loadPolicy();
});
</script>

<style scoped>
.settings-section {
  background: white;
  border-radius: 8px;
  padding: 24px;
  margin-bottom: 24px;
}

.setting-row {
  padding: 16px 0;
  border-bottom: 1px solid #e0e0e0;
}

.setting-row:last-child {
  border-bottom: none;
}

.setting-row.indent {
  padding-left: 32px;
  background: #f8f9fa;
  margin-left: -24px;
  margin-right: -24px;
  padding-right: 24px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  cursor: pointer;
  font-weight: 500;
}

.checkbox-label input[type="checkbox"] {
  margin-right: 12px;
  width: 18px;
  height: 18px;
}

.setting-description {
  margin: 8px 0 0 30px;
  font-size: 13px;
  color: #5f6368;
}

.label-text {
  display: block;
  margin-bottom: 8px;
  font-weight: 500;
}

.number-input {
  padding: 8px 12px;
  border: 1px solid #dadce0;
  border-radius: 4px;
  font-size: 14px;
  width: 100px;
}
</style>
```

---

## Verification Plan

### Manual Testing Steps

1. ✅ **Admin Configuration**
   - Navigate to Admin → Security Settings
   - Toggle MFA features on/off
   - Verify settings save correctly

2. ✅ **User Passkey Registration**
   - Login as normal user
   - Navigate to Profile → Security
   - Verify Passkey section appears (if enabled in policy)
   - Click "Add Passkey"
   - Complete browser WebAuthn prompt (Windows Hello/Touch ID/Face ID)
   - Verify passkey appears in list with auto-generated name

3. ✅ **Passkey Management**
   - Verify passkey count display (e.g., "2/10 個已註冊")
   - Try adding more passkeys until limit reached
   - Verify "Add" button becomes disabled at limit
   - Delete a passkey
   - Verify count updates correctly

4. ✅ **Feature Toggle Verification**
   - Admin disables Passkey feature
   - User refreshes page
   - Verify Passkey section disappears

5. ✅ **Browser Compatibility**
   - Windows 10/11: Test with Windows Hello (PIN/Fingerprint/Face)
   - macOS: Test with Touch ID
   - iOS Safari: Test with Face ID/Touch ID
   - Android Chrome: Test with Fingerprint

---

## Implementation Checklist

- [ ] Step 0: SecurityPolicy updates (DB migration)
- [ ] Step 1: Backend APIs (List, Delete) + **System Tests**
- [ ] Step 2: Composable `useWebAuthn.js` (JavaScript)
- [ ] Step 3: Frontend `MfaSettings.vue` integration
- [ ] Step 4: i18n translations (en-US, zh-TW)
- [ ] Step 5: Admin UI Security Settings
- [ ] Testing: System tests for all new endpoints
- [ ] Verification: Manual testing on all browsers
- [ ] Documentation: Update user guide

---

## Testing Strategy

### System Tests for New APIs

**File**: `Tests.SystemTests/PasskeyApiTests.cs`

目前已有的測試（4個）：
- ✅ `RegisterOptions_ValidUser_ReturnsOptions`
- ✅ `Register_WithoutOptions_ReturnsBadRequest`
- ✅ `LoginOptions_ReturnsOptions`
- ✅ `Login_InvalidSignature_ReturnsBadRequest`

**需要新增的測試**：

```csharp
[Fact]
public async Task ListPasskeys_Authenticated_ReturnsUserPasskeys()
{
    // Arrange: User is authenticated
    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
    
    // Act
    var response = await _httpClient.GetAsync("/api/passkey/list");
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var passkeys = await response.Content.ReadFromJsonAsync<List<PasskeyDto>>();
    Assert.NotNull(passkeys);
    // Initially should be empty list
    Assert.Empty(passkeys);
}

[Fact]
public async Task ListPasskeys_Unauthenticated_Returns401()
{
    // Arrange: No authentication header
    _httpClient.DefaultRequestHeaders.Authorization = null;
    
    // Act
    var response = await _httpClient.GetAsync("/api/passkey/list");
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task DeletePasskey_ValidId_ReturnsSuccess()
{
    // Arrange
    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
    // Note: This test requires a real passkey ID, might need to register one first
    // Or use a known test passkey ID from seeded data
    var passkeyId = 1; // Replace with actual seeded ID
    
    // Act
    var response = await _httpClient.DeleteAsync($"/api/passkey/{passkeyId}");
    
    // Assert
    Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
}

[Fact]
public async Task DeletePasskey_Unauthenticated_Returns401()
{
    // Arrange
    _httpClient.DefaultRequestHeaders.Authorization = null;
    
    // Act
    var response = await _httpClient.DeleteAsync("/api/passkey/1");
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task RegisterPasskey_ExceedsLimit_ReturnsBadRequest()
{
    // Arrange: Set SecurityPolicy.MaxPasskeysPerUser = 1 (via admin API)
    // Then register 1 passkey successfully
    // Act: Try to register 2nd passkey
    // Assert: Should return 400 Bad Request with error message
    
    // Note: This test requires:
    // 1. Admin API to update SecurityPolicy
    // 2. Full WebAuthn registration flow (complex)
    // Mark as [Fact(Skip = "Requires full WebAuthn flow")] initially
}
```

### 測試覆蓋重點

| 功能 | 測試項目 | 優先級 |
|------|---------|--------|
| **List API** | 認證檢查 (401) | 🔴 必須 |
| **List API** | 回傳格式正確 | 🔴 必須 |
| **List API** | 只回傳自己的 passkeys | 🟡 重要 |
| **Delete API** | 認證檢查 (401) | 🔴 必須 |
| **Delete API** | 成功刪除 | 🔴 必須 |
| **Delete API** | 不能刪除別人的 | 🟡 重要 |
| **Register** | 檢查數量限制 | 🟡 重要 |
| **Register** | 檢查功能開關 | 🟢 Nice to have |

### 測試執行

```powershell
# Run all passkey tests
dotnet test Tests.SystemTests --filter "FullyQualifiedName~PasskeyApiTests"

# Run specific test
dotnet test Tests.SystemTests --filter "FullyQualifiedName~PasskeyApiTests.ListPasskeys_Authenticated_ReturnsUserPasskeys"
```

---

## Notes

- **TypeScript**: Not used, pure JavaScript only
- **Device Naming**: Auto-generated based on browser + OS + date
- **Passkey Limit**: Configurable via SecurityPolicy (default: 10, range: 1-50)
- **Feature Toggles**: All MFA methods can be individually enabled/disabled by admins
- **Backwards Compatibility**: All features default to `true` (enabled)
