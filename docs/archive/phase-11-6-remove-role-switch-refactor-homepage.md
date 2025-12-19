# Phase 11.6: Simplify My Account - Two-Button Homepage

**Status**: 📋 Planned  
**Priority**: HIGH  
**Estimated Effort**: 2-3 hours

---

## 🎯 Goal

Simplify My Account to a homepage with two clear sections:
1. **Authorization Management** (授權管理)
2. **Linked Accounts Management** (帳號鏈結管理)

Remove all role switching features as they are unnecessary (users without permissions can't access admin pages anyway - protected by `[HasPermission]`).

---

## 🏗️ New Design

###  Homepage
```
┌─────────────────────────────────────────┐
│        首  頁            │
├─────────────────────────────────────────┤
│                                          │
│  ┌────────────────────────────────────┐ │
│  │  📱 授權應用程式管理                │ │
│  │  Authorization Management           │ │
│  │                                     │ │
│  │  查看和撤銷已授權的應用程式          │ │
│  │  View and revoke app authorizations│ │
│  └────────────────────────────────────┘ │
│                                          │
│  ┌────────────────────────────────────┐ │
│  │  🔗 帳號鏈結管理                    │ │
│  │  Linked Accounts Management         │ │
│  │                                     │ │
│  │  管理您的鏈結帳號 (Person架構)      │ │
│  │  Manage your linked accounts        │ │
│  └────────────────────────────────────┘ │
│                                          │
└─────────────────────────────────────────┘
```

---

## 📝 Implementation Tasks

### Task 1: Remove Role Switching Code

**Backend - Remove these:**
- `GET /api/my/roles` endpoint
- `POST /api/my/switch-role` endpoint
- `IAccountManagementService.GetMyAvailableRolesAsync()`
- `IAccountManagementService.SwitchRoleAsync()`
- `AvailableRoleDto`, `SwitchRoleRequest`, `SwitchRoleResponse` (DTOs)

**Frontend - Delete these:**
- `Web.IdP/ClientApp/src/components/account/RoleList.vue` (entire file)
- Any role switching UI in My Account views

**Tests - Remove:**
- `e2e/tests/feature-my-account/my-account-role-switching.spec.ts`
- Role-related E2E tests in `my-account-ui-states.spec.ts`
- Unit tests for `SwitchRoleAsync` and `GetMyAvailableRolesAsync`

---

### Task 2: Update Homepage with Two Buttons

**更新首頁**:

**File**: `Web.IdP/Pages/Index.cshtml` (使用者登入後的首頁)

**現狀**: 目前顯示「註冊的應用程式」列表

**改成**: 顯示兩個大按鈕/卡片
- 第一張卡片: 📱 授權應用程式管理 → 連結到新頁面 `/Account/Authorizations`
- 第二張卡片: 🔗 帳號鏈結管理 → 連結到新頁面 `/Account/LinkedAccounts`

**實作要點**:
- 使用兩張大卡片 (類似 dashboard tiles)
- 響應式設計 (手機版一欄，桌面版兩欄)
- 參考現有的頁面樣式

---

### Task 3: Add Navigation Menu Items

**更新選單**:

**File**: `Web.IdP/Pages/Shared/_Layout.cshtml` (或類似的 layout 檔案)

**新增兩個選單項目**:
1. 授權應用程式管理 (`/Account/Authorizations`)
2. 帳號鏈結管理 (`/Account/LinkedAccounts`)

---

### Task 4: Create Authorization Management Page

**新增頁面**: `Web.IdP/Pages/Account/Authorizations.cshtml` + `Authorizations.cshtml.cs`

---

**功能**:
- 顯示已授權的應用程式列表 (使用 OpenIddict Authorization 記錄)
- 撤銷授權按鈕
- 授權日期、過期時間等資訊

**術語**:
- "Authorized Applications" (已授權的應用程式)
- "Authorization Management" (授權管理)

---

### Task 5: Create Linked Accounts Page

**新增頁面**: `Web.IdP/Pages/Account/LinkedAccounts.cshtml` + `LinkedAccounts.cshtml.cs`

**功能**: 顯示和管理鏈結的帳號 (Person 架構)

---

### Task 6: Verify Linked Accounts Feature

**檢查現有功能是否完整**:
- `GET /api/my/accounts` endpoint
- `POST /api/my/switch-account` endpoint
- Linked accounts UI (如果已存在)

**如果功能不完整**:
- 暫時隱藏第二張卡片 (或標記為 "Coming Soon")
- 在此 Phase 不實作，留待後續

**如果功能已存在**:
- 確保 UI 正常運作
- 更新 E2E tests

---

### Task 7: Update Localization

**更新資源檔**:
- `Web.IdP/Resources/SharedResource.*.resx`

**新增 keys**:
- `AuthorizationManagement` (授權管理)
- `AuthorizedApplications` (已授權的應用程式)
- `LinkedAccountsManagement` (帳號鏈結管理)
- `RevokeAuthorization` (撤銷授權)

**移除 keys**:
- `MyRoles`, `SwitchToRole`, `ActiveRole` 等 role switching 相關

---

### Task 8: Update E2E Tests

**更新**:
- `e2e/tests/feature-my-account/my-account-navigation.spec.ts`
  - 測試首頁兩個按鈕可點擊
  - 測試選單連結正確
  - 測試導航到授權管理頁面
  - 測試導航到帳號鏈結頁面 (如果功能存在)

**移除**:
- 所有 role switching 相關測試

---

## ✅ Success Criteria

- [ ] `Pages/Index.cshtml` 首頁顯示兩個大按鈕/卡片
- [ ] 選單新增兩個連結: 授權管理 & 帳號鏈結
- [ ] 點擊首頁按鈕或選單 → 進入 `/Account/Authorizations` 頁面
- [ ] 點擊首頁按鈕或選單 → 進入 `/Account/LinkedAccounts` 頁面
- [ ] 授權管理頁面顯示已授權應用程式 + 撤銷功能
- [ ] 所有 role switching 代碼已移除
- [ ] 術語更新完成 ("Authorized Applications")
- [ ] E2E tests 通過
- [ ] Build 成功無錯誤

---

## 🔍 Implementation Notes

### 參考現有結構
- 查看 `Web.IdP/Pages/Index.cshtml` 了解目前首頁結構 (目前顯示「註冊的應用程式」)
- 查看 `Web.IdP/Pages/Account/` 找到其他 Account 相關頁面
- 查看 `Web.IdP/Pages/Shared/_Layout.cshtml` 找到選單位置
- 查看 `Web.IdP/Controllers/Api/MyAccountController.cs` 確認現有 API

### 帳號鏈結功能判斷
1. 檢查 `IAccountManagementService` 是否有 `GetMyLinkedAccountsAsync`
2. 檢查是否已有 UI 組件
3. 如果不完整 → 標記 "Coming Soon"，此 Phase 不實作

### 保持簡潔
- 首頁只有兩個大按鈕，不需要複雜邏輯
- 參考現有 Admin Dashboard 的卡片樣式
- 使用現有的 i18n 和 CSS 框架

---

## 📚 Related Docs

- `docs/SSO_ENTRY_PORTAL_ARCHITECTURE.md` - SSO 入口是獨立 App (Phase 12)
- `docs/PERSON_MULTI_ACCOUNT_ARCHITECTURE.md` - Person/Account 架構
- `e2e/tests/feature-my-account/` - 現有 E2E tests

---

**Estimated Time**: 2-3 hours  
**Priority**: HIGH
