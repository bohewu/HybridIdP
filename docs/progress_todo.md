# HybridIdP 待辦事項

> 📝 本文件列出所有待完成的 Phases 和功能

最後更新：2025-11-04

---

## 當前優先級

### 🎯 Next Up: Phase 4.5 - Role Management UI

**目標：** 實作角色管理介面，包含 Permission 分配功能

**實作步驟（按 Small Steps 策略）：**

#### API Implementation

- [ ] **Step 1:** DTOs
  - [ ] Create `RoleSummaryDto` (for list)
  - [ ] Create `RoleDetailDto` (for detail)
  - [ ] Create `CreateRoleDto` (for creation)
  - [ ] Create `UpdateRoleDto` (for update)
  - [ ] Commit: `feat(api): Add RoleSummaryDto and RoleDetailDto`

- [ ] **Step 2:** GET Endpoint
  - [ ] Implement `GET /api/admin/roles` with pagination
  - [ ] Return list of roles with permission counts
  - [ ] Add unit tests for role list endpoint
  - [ ] Commit: `feat(api): Implement GET /api/admin/roles with pagination`
  - [ ] Commit: `test(api): Add unit tests for role list endpoint`

- [ ] **Step 3:** GET Detail Endpoint
  - [ ] Implement `GET /api/admin/roles/{id}`
  - [ ] Return role with full permission list
  - [ ] Add unit tests
  - [ ] Commit: `feat(api): Implement GET /api/admin/roles/{id}`
  - [ ] Commit: `test(api): Add unit tests for role detail endpoint`

- [ ] **Step 4:** POST Endpoint
  - [ ] Implement `POST /api/admin/roles`
  - [ ] Validation: unique role name, permission validation
  - [ ] Add unit tests
  - [ ] Commit: `feat(api): Implement POST /api/admin/roles with validation`
  - [ ] Commit: `test(api): Add unit tests for role creation`

- [ ] **Step 5:** PUT Endpoint
  - [ ] Implement `PUT /api/admin/roles/{id}`
  - [ ] Update role name and permissions
  - [ ] Add unit tests
  - [ ] Commit: `feat(api): Implement PUT /api/admin/roles/{id}`
  - [ ] Commit: `test(api): Add unit tests for role update`

- [ ] **Step 6:** DELETE Endpoint
  - [ ] Implement `DELETE /api/admin/roles/{id}`
  - [ ] Protection: cannot delete Admin/User system roles
  - [ ] Protection: cannot delete role if users assigned
  - [ ] Add unit tests
  - [ ] Commit: `feat(api): Implement DELETE /api/admin/roles/{id} with protections`
  - [ ] Commit: `test(api): Add unit tests for role deletion`

#### UI Implementation

- [ ] **Step 7:** Razor Page & Vue Scaffolding
  - [ ] Create `Pages/Admin/Roles.cshtml` with `[Authorize(Roles = "Admin")]`
  - [ ] Create `ClientApp/src/admin/roles/style.css` (⚠️ Tailwind directives)
  - [ ] Create `ClientApp/src/admin/roles/main.js` (⚠️ import './style.css')
  - [ ] Create `ClientApp/src/admin/roles/RolesApp.vue` (basic structure)
  - [ ] Commit: `feat(ui): Add Roles.cshtml with admin authorization`
  - [ ] Commit: `feat(ui): Setup Vue SPA for role management with Tailwind`

- [ ] **Step 8:** List Component
  - [ ] Create `RoleList.vue` component
  - [ ] Implement table with columns: Name, Description, Permissions Count, Actions
  - [ ] Integrate with `GET /api/admin/roles` API
  - [ ] Add loading state and error handling
  - [ ] System roles marked with badge (Admin, User)
  - [ ] Commit: `feat(ui): Implement RoleList component with table display`

- [ ] **Step 9:** Create Role Form
  - [ ] Create `CreateRoleModal.vue` component
  - [ ] Form fields: Name, Description
  - [ ] Permission selector (multi-select with categories)
  - [ ] Validation: required name, unique name
  - [ ] Integrate with `POST /api/admin/roles` API
  - [ ] Commit: `feat(ui): Add role creation form with permission selector`

- [ ] **Step 10:** Edit Role Form
  - [ ] Create `EditRoleModal.vue` component
  - [ ] Pre-fill form with existing role data
  - [ ] Permission selector with current selections
  - [ ] System roles: name read-only, permissions editable
  - [ ] Integrate with `PUT /api/admin/roles/{id}` API
  - [ ] Commit: `feat(ui): Add role edit form with permission management`

- [ ] **Step 11:** Delete Confirmation
  - [ ] Create `DeleteRoleModal.vue` component
  - [ ] Warning message for system roles (cannot delete)
  - [ ] Warning if users assigned (show count)
  - [ ] Integrate with `DELETE /api/admin/roles/{id}` API
  - [ ] Commit: `feat(ui): Add role deletion with protection warnings`

- [ ] **Step 12:** E2E Testing & Verification
  - [ ] Test role list loading (pagination)
  - [ ] Test create role (e.g., "Content Editor" with permissions)
  - [ ] Test edit role (add/remove permissions)
  - [ ] Test delete custom role (successful)
  - [ ] Test delete system role (rejected)
  - [ ] Test delete role with users (rejected)
  - [ ] Commit: `test(e2e): Add role management E2E tests`

- [ ] **Step 13:** Documentation Update
  - [ ] Update `progress_completed.md` - add Phase 4.5 summary
  - [ ] Update `progress_todo.md` - mark Phase 4.5 as completed
  - [ ] Commit: `docs: Update progress - Phase 4.5 completed`

**預計完成時間：** 1-2 開發 sessions

---

## Phase 4.6: Permission System

### 目標
實作細粒度權限系統，取代簡單的 Admin/User 角色檢查

### 功能範圍
- [ ] Permission 定義（例如：`clients.read`, `clients.write`, `users.manage`）
- [ ] Permission 與 Role 關聯
- [ ] Permission-based Authorization
- [ ] Permission 檢查 UI（按鈕/功能基於權限顯示/隱藏）

### 實作步驟
- [ ] Define permission constants in `Core.Domain/Constants/Permissions.cs`
- [ ] Implement `PermissionRequirement` and `PermissionHandler`
- [ ] Update API Controllers to use `[Authorize(Policy = "RequirePermission")]`
- [ ] Update UI to check permissions before showing actions
- [ ] Add permission management to Role Management UI
- [ ] E2E testing with different permission sets

**預計完成時間：** 1-2 開發 sessions

---

## Phase 5: Multi-Factor Authentication (MFA)

### Phase 5.1: TOTP (Time-based One-Time Password)

**目標：** 實作 TOTP 雙因素認證（Google Authenticator 相容）

### 功能範圍
- [ ] User Settings: Enable/Disable MFA
- [ ] TOTP Secret Generation
- [ ] QR Code Display for App Setup
- [ ] Verification Code Input
- [ ] Recovery Codes Generation
- [ ] MFA Enforcement (per-user or global)

### 實作步驟

#### Backend
- [ ] Install NuGet: `OtpNet` (TOTP library)
- [ ] Add `TwoFactorEnabled`, `TwoFactorSecret` to `ApplicationUser`
- [ ] Implement `IMfaService` interface
- [ ] Implement `MfaService` (generate secret, verify code, recovery codes)
- [ ] API: `POST /api/account/mfa/enable` (generate secret, return QR code)
- [ ] API: `POST /api/account/mfa/verify` (verify TOTP code)
- [ ] API: `POST /api/account/mfa/disable` (disable MFA)
- [ ] API: `GET /api/account/mfa/recovery-codes` (generate backup codes)
- [ ] Update login flow: check `TwoFactorEnabled`, prompt for code
- [ ] Unit tests for `MfaService`

#### Frontend
- [ ] User Settings Page (`/Account/Settings`)
- [ ] MFA Enable Flow:
  1. User clicks "Enable MFA"
  2. Backend generates secret, returns QR code data
  3. Display QR code (use `qrcode.js`)
  4. User scans with Google Authenticator
  5. User enters verification code
  6. Backend verifies code, enables MFA
  7. Display recovery codes (download/print)
- [ ] MFA Disable Flow (require password + current TOTP code)
- [ ] Login Flow Update: TOTP input after password

#### E2E Testing
- [ ] Test enable MFA flow
- [ ] Test login with MFA
- [ ] Test disable MFA
- [ ] Test recovery codes

**預計完成時間：** 2-3 開發 sessions

### Phase 5.2: Cloudflare Turnstile (已部分完成)

**目標：** 整合 Turnstile 取代傳統 CAPTCHA

**狀態：** Backend 已完成，Frontend 待整合

### 待完成
- [ ] Login Page: Add Turnstile widget
- [ ] Register Page: Add Turnstile widget
- [ ] Update login/register flow to validate Turnstile token
- [ ] E2E testing with Turnstile

**參考文件：** `docs/turnstile_integration.md`

**預計完成時間：** 0.5 開發 session

### Phase 5.3: SMS MFA (Optional)

**目標：** 實作 SMS 雙因素認證

**優先級：** Low（先完成 TOTP）

---

## Phase 6: Advanced Features

### Phase 6.1: Audit Logging

**目標：** 記錄所有管理員操作和安全事件

### 功能範圍
- [ ] AuditLog entity (User, Action, Timestamp, Details)
- [ ] Middleware to capture API calls
- [ ] Log create/update/delete operations
- [ ] Log login attempts (success/failure)
- [ ] Admin UI: Audit Log Viewer (filterable, searchable)

**預計完成時間：** 1 開發 session

### Phase 6.2: Email Notifications

**目標：** 發送重要事件通知

### 功能範圍
- [ ] Email service integration (SMTP / SendGrid)
- [ ] Email templates (Razor Email Templates)
- [ ] Notifications:
  - Welcome email (new user created)
  - Password reset email
  - MFA enabled/disabled email
  - Suspicious login alert

**預計完成時間：** 1-2 開發 sessions

### Phase 6.3: Session Management

**目標：** 用戶可查看和管理活躍 sessions

### 功能範圍
- [ ] Display active sessions (device, location, last active)
- [ ] Revoke session (logout from specific device)
- [ ] Revoke all sessions (logout everywhere)

**預計完成時間：** 1 開發 session

---

## Phase 7: Production Readiness

### Phase 7.1: Security Hardening

**檢查清單：**
- [ ] HTTPS enforcement
- [ ] HSTS headers
- [ ] CSP (Content Security Policy)
- [ ] Rate limiting (login, API)
- [ ] Input validation review
- [ ] SQL injection prevention audit
- [ ] XSS prevention audit
- [ ] CSRF protection verification

### Phase 7.2: Performance Optimization

**待優化：**
- [ ] Database indexing review
- [ ] Query optimization (N+1 problem check)
- [ ] API response caching
- [ ] Frontend bundle optimization (Vite build analysis)
- [ ] Image optimization
- [ ] CDN configuration

### Phase 7.3: Monitoring & Observability

**目標：** 生產環境監控

### 功能範圍
- [ ] Health check endpoints (`/health`)
- [ ] Application Insights / Serilog integration
- [ ] Error tracking (Sentry)
- [ ] Performance metrics
- [ ] Database connection monitoring

### Phase 7.4: Deployment

**待完成：**
- [ ] Docker containerization (Web.IdP)
- [ ] docker-compose for full stack
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Environment configuration (staging/production)
- [ ] Database migration strategy
- [ ] Backup and restore procedures
- [ ] Rollback plan

---

## Backlog (未分類功能)

### 功能增強
- [ ] Remember Me 功能改進
- [ ] Password strength indicator
- [ ] User profile picture upload
- [ ] Bulk user import (CSV)
- [ ] Export audit logs (CSV/Excel)
- [ ] API documentation (Swagger UI 改進)
- [ ] Dark mode support

### 技術債務
- [ ] Refactor large controllers into smaller handlers
- [ ] Add more unit test coverage (target: 80%+)
- [ ] Integration tests for all API endpoints
- [ ] Frontend component unit tests (Vitest)
- [ ] Code style consistency (ESLint, Prettier)
- [ ] Accessibility (WCAG 2.1 AA compliance)

---

## 注意事項

### ⚠️ 每個新功能必須：

1. **遵循 Small Steps Git 策略**
   - API → Tests → UI 分別 commit
   - 每個 endpoint/component 獨立 commit

2. **更新文件**
   - 完成後更新 `progress_completed.md`
   - 標記 `progress_todo.md` 完成項目
   - 必要時更新 `implementation_guidelines.md`

3. **測試**
   - Unit tests for services
   - API tests (Swagger UI 手動測試或 E2E)
   - E2E tests for critical flows (Playwright MCP)

4. **Tailwind CSS 設定**
   - 新 Vue SPA 必須建立 `style.css`
   - `main.js` 必須 `import './style.css'`

5. **Authorization 檢查**
   - Razor Page: `[Authorize(Roles = "Admin")]`
   - API Controller: `[Authorize(Roles = "Admin")]` or Permission-based

---

**下一步行動：** 開始 Phase 4.5 - Role Management UI

**參考：** `WORKFLOW.md` 查看詳細開發流程
