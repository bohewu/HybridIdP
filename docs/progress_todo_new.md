# HybridIdP 待辦事項

> 📝 本文件列出所有待完成的 Phases 和功能

最後更新：2025-11-06

---

## 當前優先級

### ✅ ~~Phase 4.6 - Permission System & Menu Filtering~~ (已完成)

**完成時間：** 2025-11-06

**實作內容：**

- ✅ Permission-based authorization for all Admin API endpoints (24 endpoints)
- ✅ Claims permissions added (claims.read/create/update/delete)
- ✅ Pure backend menu filtering using PermissionHelper
- ✅ Type-safe permission constants (Permissions.*)
- ✅ Responsive layout fixes (z-index for modals)

**詳細資訊：** 見 `progress_completed.md`

---

### 🎯 Next Up: Phase 4.7 - UI Spacing & Visual Consistency

**目標：** 改進所有 Admin UI 頁面的視覺一致性和間距問題

**實作策略：** 一個頁面一個頁面處理，每個頁面改完測試後立即 commit

**實作步驟：**

- [ ] **Step 1: Users Page Spacing**
  - [ ] 修正 input 欄位間距和對齊
  - [ ] 過濾器區塊（Search + Sort）垂直居中對齊
  - [ ] 統一 button 和 input 高度
  - [ ] Table 和 filters 之間的間距
  - [ ] 測試並 commit: `fix(ui): Improve spacing and alignment on Users page`

- [ ] **Step 2: Roles Page Spacing**
  - [ ] 修正 input 欄位間距和對齊
  - [ ] 過濾器區塊垂直居中對齊
  - [ ] Modal 內部元件間距優化
  - [ ] Permission selector 間距改進
  - [ ] 測試並 commit: `fix(ui): Improve spacing and alignment on Roles page`

- [ ] **Step 3: Clients Page Spacing**
  - [ ] 修正 input 欄位間距和對齊
  - [ ] 過濾器區塊垂直居中對齊
  - [ ] Form 欄位統一間距
  - [ ] Redirect URIs 輸入區間距
  - [ ] 測試並 commit: `fix(ui): Improve spacing and alignment on Clients page`

- [ ] **Step 4: Scopes Page Spacing**
  - [ ] 修正 input 欄位間距和對齊
  - [ ] 過濾器區塊垂直居中對齊
  - [ ] Claims selector 間距改進
  - [ ] 測試並 commit: `fix(ui): Improve spacing and alignment on Scopes page`

- [ ] **Step 5: Claims Page Spacing**
  - [ ] 修正 input 欄位間距和對齊
  - [ ] 過濾器區塊垂直居中對齊
  - [ ] 測試並 commit: `fix(ui): Improve spacing and alignment on Claims page`

- [ ] **Step 6: Dashboard Spacing**
  - [ ] 統計卡片間距一致性
  - [ ] 測試並 commit: `fix(ui): Improve spacing on Dashboard`

**預計完成時間：** 1 開發 session

---

## Phase 5: Security Policies & Multi-Factor Authentication

### Phase 5.1: Password Policy Configuration ✨

**目標：** 允許管理員在 UI 中配置密碼策略（最小長度、複雜度要求等）

**功能範圍：**

- [ ] 密碼策略 entity (MinLength, RequireDigit, RequireSpecialChar, etc.)
- [ ] Security Policies Management API
- [ ] Security Policies UI (Admin only)
- [ ] Apply policies during user creation/password change

### Phase 5.2: Multi-Factor Authentication (MFA) 🔐

**目標：** 支援 TOTP (Google Authenticator, Microsoft Authenticator)

**功能範圍：**

- [ ] MFA setup flow (QR code generation)
- [ ] MFA verification during login
- [ ] Recovery codes generation
- [ ] Per-user MFA enable/disable (Admin UI)

---

## Phase 6: Future Enhancements 🚀

### Field-Level Permission Control (細粒度字段控制)

**目標：** 根據權限控制特定字段的可見性/可編輯性

**範例：**

- `users.read.email` - 可查看 Email 欄位
- `users.update.department` - 可編輯 Department 欄位
- `users.read.sensitive` - 可查看敏感資訊

**實作方向：**

- [ ] 定義 field-level permissions
- [ ] 前端組件根據權限顯示/隱藏欄位
- [ ] API DTO 根據權限過濾欄位
- [ ] 測試不同權限組合

**優先級：** 低（Phase 6 或更後）

### Internationalization (i18n) 🌍

**目標：** 支援多語系（中文、英文）

**功能範圍：**

- [ ] Backend error messages localization
- [ ] UI text localization (Vue i18n)
- [ ] Language switcher
- [ ] Resource files (.resx for backend, JSON for frontend)

### Audit Log 📝

**目標：** 記錄所有管理操作

**功能範圍：**

- [ ] AuditLog entity (User, Action, Timestamp, Details)
- [ ] Audit logging middleware
- [ ] Audit log viewer (Admin UI)
- [ ] Export audit logs

### Advanced User Search 🔍

**目標：** 增強用戶搜尋功能

**功能範圍：**

- [ ] 多欄位搜尋 (Email + Name + Department)
- [ ] Date range filters (CreatedAt)
- [ ] Advanced filters UI

---

## 已完成 Phases 摘要

### ✅ Phase 1: PostgreSQL & Entity Framework Core
### ✅ Phase 2: OpenIddict Integration & OIDC Flow
### ✅ Phase 3.1: Admin Layout & Navigation
### ✅ Phase 3.2: Admin Dashboard (Vue.js Rewrite)
### ✅ Phase 3.3-3.5: Scope Management
### ✅ Phase 3.6-3.8: Client Management
### ✅ Phase 3.9-3.11: Claim Type Management
### ✅ Phase 4.4: User Management UI
### ✅ Phase 4.5: Role Management UI
### ✅ Phase 4.6: Permission System & Menu Filtering

**詳細資訊：** 見 `progress_completed.md`

---

**下一步行動：** 開始 Phase 4.7 - UI Spacing & Visual Consistency (從 Users 頁面開始)
