# HybridIdP 待辦事項

> 📝 本文件列出所有待完成的 Phases 和功能

最後更新：2025-11-04

---

## 當前優先級

### ✅ ~~Phase 4.5 - Role Management UI~~ (已完成)

**完成時間：** 2025-11-04

**實作內容：**
- ✅ Role CRUD API (GET, POST, PUT, DELETE with permissions endpoint)
- ✅ Role Management UI (RolesApp.vue with Create/Edit/Delete modals)
- ✅ Permission selector with category grouping
- ✅ System role protections (Admin, User cannot be deleted/renamed)
- ✅ User count tracking and deletion protection
- ✅ E2E testing with Playwright MCP

**詳細資訊：** 見 `progress_completed.md`

---

### 🎯 Next Up: Phase 4.6 - Permission System Implementation

**目標：** 實作完整的權限檢查系統，將 Role-based permissions 應用於 API 端點

**實作步驟（按 Small Steps 策略）：**

#### Permission Infrastructure

- [ ] **Step 1:** Permission Attribute
  - [ ] Create `[RequirePermission]` attribute (custom authorization attribute)
  - [ ] Define permission constants (e.g., "users.read", "users.write", "scopes.manage")
  - [ ] Commit: `feat(auth): Add RequirePermission attribute and constants`

#### API Implementation

- [ ] **Step 1:** DTOs
  - [ ] Create `RoleSummaryDto` (for list)
  - [ ] Create `RoleDetailDto` (for detail)
  - [ ] Create `CreateRoleDto` (for creation)
  - [ ] Create `UpdateRoleDto` (for update)
  - [ ] Commit: `feat(api): Add RoleSummaryDto and RoleDetailDto`

- [ ] **Step 2:** GET Endpoint
  - [x] Implement `GET /api/admin/roles` with pagination (skip/take/search/sort)
  - [x] Return list of roles with permission counts
  - [ ] Add unit tests for role list endpoint
  - [x] Commit: `feat(api): roles list supports paging/search/sort + permission checks`
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

## Phase 5: Dynamic Security Policies (TDD-Driven)

**目標：** 實作可動態配置的安全策略系統，包含密碼策略和同意畫面管理

### Phase 5.1: Internationalized Identity Errors

**目標：** 提供多語言化的身份驗證錯誤訊息

#### 實作步驟
- [ ] Create custom `IdentityErrorDescriber` class
- [ ] Implement translated error messages (en-US, zh-TW)
- [ ] Register error describer in DI container
- [ ] Support dynamic language switching based on user locale

#### 驗證
- [ ] Identity errors (e.g., "Password too short") appear in configured language
- [ ] Language switches correctly for different users

**預計完成時間：** 0.5 開發 session

---

### Phase 5.2: TDD for Dynamic Password Validator

**目標：** 使用 TDD 方法建立可配置的密碼驗證器測試

#### 實作步驟
- [ ] Write failing unit tests for password policy validation
  - [ ] Minimum length validation
  - [ ] Password history check (prevent reuse)
  - [ ] Complexity requirements (uppercase, lowercase, digits, special chars)
  - [ ] Password expiration
  - [ ] Common password blacklist
- [ ] Document expected behavior in tests
- [ ] Ensure tests fail initially (Red phase of TDD)

#### 驗證
- [ ] All password validator tests exist and fail as expected
- [ ] Test coverage includes edge cases

**預計完成時間：** 0.5 開發 session

---

### Phase 5.3: Implement Dynamic Password Validator

**目標：** 實作密碼驗證邏輯，通過 TDD 測試

#### 實作步驟
- [ ] Create `SecurityPolicy` entity (store policies in database)
  - [ ] MinPasswordLength, RequireUppercase, RequireDigit, etc.
  - [ ] PasswordHistoryCount, PasswordExpirationDays
- [ ] Create `ISecurityPolicyService` interface
- [ ] Implement `DynamicPasswordValidator` implementing `IPasswordValidator<ApplicationUser>`
- [ ] Implement password history tracking
- [ ] Implement password expiration logic
- [ ] Make all TDD tests pass (Green phase)

#### 驗證
- [ ] All password validator unit tests pass
- [ ] Password validation respects configured policies
- [ ] Password history prevents reuse

**預計完成時間：** 1 開發 session

---

### Phase 5.4: API & UI for Security Policies

**目標：** 提供管理員介面管理安全策略

#### Backend
- [ ] API: `GET /api/admin/security/policies` (get current policies)
- [ ] API: `PUT /api/admin/security/policies` (update policies)
- [ ] DTOs: `SecurityPolicyDto`
- [ ] Validation: Ensure policies are within reasonable bounds

#### Frontend
- [ ] Vue SPA: `ClientApp/src/admin/security/SecurityApp.vue`
- [ ] Security Policy Editor with sections:
  - Password Requirements (length, complexity)
  - Password History (history count)
  - Password Expiration (days, grace period)
  - Account Lockout (max attempts, lockout duration)
- [ ] Real-time validation feedback
- [ ] Save and apply policies

#### 驗證
- [ ] Admin can view current security policies
- [ ] Admin can update policies via UI
- [ ] Changes take effect immediately for new password changes
- [ ] Validation prevents invalid policy values

**預計完成時間：** 1-2 開發 sessions

---

### Phase 5.5: Integrate Policy System

**目標：** 將安全策略系統整合到登入和密碼變更流程

#### 實作步驟
- [ ] Register `DynamicPasswordValidator` in DI
- [ ] Add password expiration check during login
- [ ] Prompt user to change password if expired
- [ ] Apply policies during password change flow
- [ ] Update user account management to show policy requirements

#### 驗證
- [ ] System correctly enforces configured password policies during login
- [ ] Password expiration triggers change password prompt
- [ ] Password history prevents reuse
- [ ] Policies apply consistently across all password change flows

**預計完成時間：** 1 開發 session
 
---

## Phase 4.7: UI Spacing & Visual Consistency Review

**目標：** 審視並統一 Admin Portal 的 spacing（padding / margin）、card / table / modal 間距與視覺風格，使各頁面看起來協調一致。

### 實作步驟
- [ ] Audit admin pages: Users, Roles, Clients, Scopes, Claims, Dashboard
- [ ] For each page, identify inconsistent spacing issues (record selector, current value, desired value)
- [ ] Propose a spacing scale (e.g., spacing-1..spacing-6) and preferred Tailwind/Bootstrap utility usage
- [ ] Create shared spacing fragment `ClientApp/src/admin/_shared/_spacing.css` or update per-feature `style.css`
- [ ] Apply small, incremental CSS fixes (atomic commits):
  - normalize card padding
  - unify table cell padding / row height
  - standardize modal body/footer spacing
  - align form field margins and label spacing
- [ ] Update `docs/implementation_guidelines.md` UI section with spacing rules and code examples
- [ ] Run Vite and perform visual verification (http://localhost:5173)
- [ ] Commit each logical change with conventional commit messages (e.g., `fix(ui): normalize card padding in users list`)

### 驗證（Acceptance criteria）
- [ ] Card padding consistent across admin pages
- [ ] Table cell padding and row heights are uniform
- [ ] Modal spacing and form layouts are consistent
- [ ] No visual regressions on mobile and desktop (quick responsive check)
- [ ] `implementation_guidelines.md` updated with spacing conventions

**預計完成時間：** 0.5-1 開發 session

---

---

### Phase 5.6: Consent Screen Management & API Resource Scopes

**目標：** 提供豐富的同意畫面自訂功能和 API 資源保護支援

#### Part 1: Consent Screen Customization

**Backend:**
- [ ] Add fields to `Scope` entity:
  - [ ] ConsentDisplayName (localized)
  - [ ] ConsentDescription (what permission allows)
  - [ ] IconUrl (optional icon)
  - [ ] IsRequired (cannot opt out)
  - [ ] DisplayOrder
- [ ] Create `Resources` table for localization
  - [ ] Support multiple languages (en-US, zh-TW)
- [ ] API: Update scope endpoints to include consent fields

**Frontend (Admin):**
- [ ] Enhance `ScopeForm.vue` with consent customization
- [ ] Multi-language editor for display name/description
- [ ] Icon upload/selection
- [ ] "Required" toggle
- [ ] Preview consent screen appearance

**Frontend (User-Facing):**
- [ ] Update `Consent.cshtml` with localized descriptions
- [ ] Group scopes by category (Profile, API Access, etc.)
- [ ] Show icons next to scopes
- [ ] Mark required scopes clearly

**驗證:**
- [ ] Admin can customize scope consent display
- [ ] Users see localized consent screen with clear descriptions
- [ ] Required scopes cannot be unchecked
- [ ] Scopes grouped by category

#### Part 2: API Resource Scopes

**Backend:**
- [ ] Create `ApiResource` entity
  - [ ] Name, DisplayName, Description, BaseUrl
  - [ ] Associated Scopes collection
- [ ] API: `GET /api/admin/resources`
- [ ] API: `POST /api/admin/resources`
- [ ] API: `PUT /api/admin/resources/{id}`
- [ ] API: `DELETE /api/admin/resources/{id}`
- [ ] API: `GET /api/admin/resources/{id}/scopes`
- [ ] OpenIddict integration (register resources, audience claim)

**Frontend:**
- [ ] Vue SPA: `ClientApp/src/admin/resources/ResourcesApp.vue`
- [ ] Create API resources (Company API, Inventory API, etc.)
- [ ] Assign scopes to resources
- [ ] Visual grouping in client configuration

**驗證:**
- [ ] Admin can create API resources
- [ ] Scopes can be assigned to resources
- [ ] Client configuration shows scopes grouped by resource
- [ ] Access tokens include audience claim

#### Part 3: Scope Authorization Policies (Whitelisting)

**Backend:**
- [ ] Manage `ClientAllowedScopes` (OpenIddict)
- [ ] Validation: Verify requested scopes against whitelist
- [ ] Update client APIs to manage allowed scopes

**Frontend:**
- [ ] Add "Allowed Scopes" multi-select in `ClientForm.vue`
- [ ] Group scopes by: Identity, API Resources, Custom
- [ ] Validation: `openid` required for OIDC clients

**驗證:**
- [ ] Client can only request whitelisted scopes
- [ ] Authorization denied for non-whitelisted scopes
- [ ] Scope selection grouped and easy to manage

**預計完成時間：** 3-4 開發 sessions

---

## Phase 6: Production Hardening

**目標：** 為生產環境做好準備，包含郵件服務、密碼管理、快取、背景工作和監控

### Phase 6.1: Email Service

**目標：** 實作真實的郵件服務（SMTP）和管理介面

#### 實作步驟
- [ ] Install NuGet packages (e.g., MailKit)
- [ ] Create `IEmailService` interface
- [ ] Implement SMTP email service
- [ ] Create `EmailSettings` entity for admin configuration
- [ ] API: `GET /api/admin/settings/email` (get email settings)
- [ ] API: `PUT /api/admin/settings/email` (update settings)
- [ ] API: `POST /api/admin/settings/email/test` (send test email)
- [ ] Vue SPA: Email settings management UI
- [ ] Email templates (welcome, password reset, etc.)

#### 驗證
- [ ] Admin can configure SMTP settings via UI
- [ ] Test email sends successfully
- [ ] Password reset emails work
- [ ] Email templates render correctly

**預計完成時間：** 1-2 開發 sessions

---

### Phase 6.2: Secret Management

**目標：** 實作安全的密碼管理策略（環境變數、Docker Secrets）

#### 實作步驟
- [ ] Document secret management strategy
- [ ] Move sensitive data from appsettings to environment variables
  - [ ] Database connection strings
  - [ ] SMTP credentials
  - [ ] OpenIddict signing keys
- [ ] Implement Docker Secrets support
- [ ] Add User Secrets for development
- [ ] Update docker-compose.yml with secrets
- [ ] Document production deployment with secrets

#### 驗證
- [ ] Sensitive data loaded from environment/secrets
- [ ] No secrets in appsettings.json
- [ ] Development uses User Secrets
- [ ] Production uses environment variables/Docker Secrets

**預計完成時間：** 1 開發 session

---

### Phase 6.3: Redis Integration

**目標：** 配置 Redis 用於快取和 OpenIddict 儲存

#### 實作步驟
- [ ] Add Redis Docker container to docker-compose.yml
- [ ] Install NuGet: `StackExchange.Redis`, `Microsoft.Extensions.Caching.StackExchangeRedis`
- [ ] Configure Redis connection in appsettings
- [ ] Implement distributed caching with Redis
- [ ] Configure OpenIddict to use Redis for token storage
- [ ] Add Redis health check

#### 驗證
- [ ] Redis container runs successfully
- [ ] Application uses Redis for caching
- [ ] OpenIddict tokens stored in Redis
- [ ] Health check reports Redis status

**預計完成時間：** 1 開發 session

---

### Phase 6.4: Background Token Cleanup

**目標：** 整合 Quartz.NET 定期清理過期 tokens

#### 實作步驟
- [ ] Install NuGet: `Quartz`, `Quartz.Extensions.Hosting`
- [ ] Create `TokenCleanupJob` implementing `IJob`
- [ ] Configure Quartz scheduler
- [ ] Schedule daily token cleanup (configurable cron)
- [ ] Add logging for cleanup operations
- [ ] Admin UI: View scheduled jobs status

#### 驗證
- [ ] Background job registered successfully
- [ ] Token cleanup job runs on schedule
- [ ] Expired tokens removed from database/Redis
- [ ] Job execution logged

**預計完成時間：** 1 開發 session

---

### Phase 6.5: Auditing & Health Checks

**目標：** 整合 Serilog 結構化日誌和健康檢查端點

#### 實作步驟

**Serilog Integration:**
- [ ] Install NuGet: `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`
- [ ] Configure Serilog in `Program.cs`
- [ ] Add structured logging to all controllers/services
- [ ] Configure log levels by namespace
- [ ] Add request/response logging middleware
- [ ] Configure log output (console, file, Seq, etc.)

**Health Checks:**
- [ ] Install NuGet: `AspNetCore.HealthChecks.NpgSql`, `AspNetCore.HealthChecks.Redis`
- [ ] Add health check endpoint: `/healthz`
- [ ] Add database health check
- [ ] Add Redis health check
- [ ] Add custom health checks (email service, external APIs)
- [ ] Health check UI (optional)

#### 驗證
- [ ] Logs output as structured JSON
- [ ] `/healthz` endpoint reports database status
- [ ] `/healthz` endpoint reports Redis status
- [ ] Health checks fail appropriately when services down
- [ ] Logs include request correlation IDs

**預計完成時間：** 1-2 開發 sessions

---

## Phase 7: User Self-Service & Account Management

**目標：** 提供用戶自助服務功能，包含帳戶管理、密碼變更、忘記密碼和登入歷史

> **Vue.js MPA Architecture Note:**  
> 建立新的 Vue SPA 入口點 `src/account-manage/main.js` 用於用戶帳戶管理頁面

### Phase 7.1: Account Management UI

**目標：** 建立用戶帳戶管理介面

#### 實作步驟
- [ ] Add `accountManage: './src/account-manage/main.js'` to `vite.config.js`
- [ ] Create Razor Page: `Pages/Account/Manage/Index.cshtml`
- [ ] Create Vue SPA: `ClientApp/src/account-manage/AccountApp.vue`
- [ ] User profile display (email, name, department)
- [ ] Edit profile form
- [ ] Navigation: Profile, Security, Activity

#### 驗證
- [ ] Authenticated user can access `/Account/Manage`
- [ ] User profile displays correctly
- [ ] User can update profile information

**預計完成時間：** 1 開發 session

---

### Phase 7.2: Change Password

**目標：** 實作用戶自助變更密碼功能

#### 實作步驟
- [ ] API: `POST /api/account/password/change`
  - [ ] Require current password
  - [ ] Validate new password against security policies
  - [ ] Update password hash
  - [ ] Add to password history
- [ ] Vue component: `ChangePassword.vue`
  - [ ] Current password input
  - [ ] New password input (with strength indicator)
  - [ ] Confirm password input
  - [ ] Validation feedback
- [ ] Integration with security policies from Phase 5

#### 驗證
- [ ] User can change their own password
- [ ] Current password required
- [ ] New password meets security policy requirements
- [ ] Password history prevents reuse
- [ ] Success confirmation shown

**預計完成時間：** 0.5-1 開發 session

---

### Phase 7.3: Forgot Password Flow

**目標：** 實作安全的忘記密碼流程（透過郵件）

#### 實作步驟
- [ ] API: `POST /api/account/password/forgot` (send reset email)
  - [ ] Generate secure reset token
  - [ ] Store token with expiration (15 minutes)
  - [ ] Send email with reset link
- [ ] API: `POST /api/account/password/reset` (reset with token)
  - [ ] Validate token
  - [ ] Validate new password
  - [ ] Update password
  - [ ] Invalidate token
- [ ] Razor Page: `Pages/Account/ForgotPassword.cshtml`
- [ ] Razor Page: `Pages/Account/ResetPassword.cshtml`
- [ ] Email template for password reset

#### 驗證
- [ ] User can request password reset
- [ ] Email received with reset link
- [ ] Reset link expires after 15 minutes
- [ ] User can set new password via link
- [ ] Token invalidated after use
- [ ] Security policies enforced

**預計完成時間：** 1-2 開發 sessions

---

### Phase 7.4: Login Activity View

**目標：** 顯示用戶最近登入活動

#### 實作步驟
- [ ] Create `LoginActivity` entity
  - [ ] UserId, Timestamp, IpAddress, UserAgent, Success, FailureReason
- [ ] Capture login events in middleware
- [ ] API: `GET /api/account/activity/logins` (get user's login history)
- [ ] Vue component: `LoginActivity.vue`
  - [ ] Table with timestamp, IP, device, status
  - [ ] Pagination
  - [ ] Filter by success/failure
- [ ] Add to Account Management UI

#### 驗證
- [ ] User can see list of recent login attempts
- [ ] Successful and failed attempts shown
- [ ] IP address and device information displayed
- [ ] Timestamps formatted correctly
- [ ] Pagination works for long history

**預計完成時間：** 1 開發 session

---

## 🎉 Project Completion

完成 Phase 7.4 後，HybridIdP 核心功能即全部完成！

**核心功能涵蓋：**
- ✅ OpenIddict OIDC 認證
- ✅ Admin Portal (Clients, Scopes, Claims, Users, Roles)
- ✅ Permission System
- ✅ Dynamic Security Policies
- ✅ Consent Screen Management
- ✅ Production Hardening (Email, Secrets, Redis, Background Jobs, Logging)
- ✅ User Self-Service (Account Management, Password Reset, Activity Logs)

**後續增強功能（docs/idp_future_enhancements.md & docs/idp_mfa_req.md）：**
- Multi-Factor Authentication (TOTP, SMS)
- Email Verification
- Content Security Policy (CSP)
- Advanced Audit Logging
- Session Management

---

## Future Enhancements (未來增強功能)

> 以下功能在核心專案完成後可以實作，詳見專門文件

### Multi-Factor Authentication (MFA)

**參考文件：** `docs/idp_mfa_req.md`

**功能範圍：**
- TOTP (Time-based One-Time Password) - Google Authenticator 相容
- SMS MFA (選用)
- Recovery Codes
- MFA Enforcement (per-user or global)
- User enrollment flow
- Login flow integration

**預計工作量：** 2-3 開發 sessions

---

### Cloudflare Turnstile Integration

**參考文件：** `docs/turnstile_integration.md`

**狀態：** Backend 已完成（`TurnstileService.cs`），Frontend 待整合

**待完成：**
- Login Page: Add Turnstile widget
- Register Page: Add Turnstile widget
- Update login/register flow to validate Turnstile token
- E2E testing with Turnstile

**預計工作量：** 0.5 開發 session

---

### Email Verification

**參考文件：** `docs/idp_future_enhancements.md`

**功能範圍：**
- Send verification email on registration
- Email confirmation token generation
- Verify email endpoint
- Resend verification email
- Block login until email verified (optional)

**預計工作量：** 1 開發 session

---

### Content Security Policy (CSP)

**參考文件：** `docs/idp_future_enhancements.md`

**功能範圍：**
- CSP header configuration
- Nonce-based inline script protection
- External resource whitelisting
- CSP violation reporting

**預計工作量：** 0.5 開發 session

---

## Backlog (功能增強和技術債務)

### 功能增強

#### User Management
- [ ] Bulk user import (CSV)
- [ ] User profile picture upload
- [ ] Advanced user search (by department, role, creation date)
- [ ] User export (CSV/Excel)

#### Session Management
- [ ] Display active sessions (device, location, last active)
- [ ] Revoke session (logout from specific device)
- [ ] Revoke all sessions (logout everywhere)
- [ ] Suspicious login detection and alerts

#### Audit & Monitoring
- [ ] Advanced audit logging
- [ ] Audit log viewer with filters
- [ ] Export audit logs (CSV/Excel)
- [ ] Real-time activity dashboard
- [ ] Security alerts (failed login attempts, permission changes)

#### UI/UX Improvements
- [ ] Dark mode support
- [ ] Customizable admin dashboard
- [ ] Remember Me 功能改進
- [ ] Password strength indicator
- [ ] Keyboard shortcuts
- [ ] Accessibility improvements (WCAG 2.1 AA compliance)

#### API Improvements
- [ ] API documentation (Swagger UI 改進)
- [ ] API versioning
- [ ] Rate limiting per endpoint
- [ ] GraphQL support (optional)

### Security Hardening

**檢查清單：**
- [ ] HTTPS enforcement in production
- [ ] HSTS headers
- [ ] Rate limiting (login, API endpoints)
- [ ] Input validation comprehensive review
- [ ] SQL injection prevention audit
- [ ] XSS prevention audit
- [ ] CSRF protection verification
- [ ] Dependency vulnerability scanning
- [ ] Security headers review (X-Frame-Options, X-Content-Type-Options, etc.)

### Performance Optimization

**待優化：**
- [ ] Database indexing review and optimization
- [ ] Query optimization (N+1 problem check)
- [ ] API response caching strategy
- [ ] Frontend bundle optimization (Vite build analysis)
- [ ] Image optimization and lazy loading
- [ ] CDN configuration for static assets
- [ ] Database connection pooling tuning

### Testing

**測試涵蓋率提升：**
- [ ] Unit test coverage to 80%+ (currently ~60%)
- [ ] Integration tests for all API endpoints
- [ ] Frontend component unit tests (Vitest)
- [ ] Load testing (Apache JMeter / k6)
- [ ] Security testing (OWASP ZAP)
- [ ] Accessibility testing

### Technical Debt

**程式碼品質：**
- [ ] Refactor large controllers into smaller handlers/services
- [ ] Code style consistency (ESLint, Prettier)
- [ ] Dead code removal
- [ ] Magic number/string extraction to constants
- [ ] Comprehensive code comments and documentation

**Architecture:**
- [ ] Event-driven architecture for audit logging
- [ ] CQRS pattern for complex operations (optional)
- [ ] Domain events for loosely coupled features

### DevOps & Deployment

**CI/CD Pipeline:**
- [ ] GitHub Actions workflow for build/test
- [ ] Automated deployment to staging
- [ ] Automated deployment to production (with approval)
- [ ] Automated database migrations
- [ ] Rollback automation

**Containerization:**
- [ ] Multi-stage Docker build optimization
- [ ] Docker Compose for full stack (local development)
- [ ] Kubernetes deployment manifests (optional)
- [ ] Helm charts (optional)

**Monitoring & Observability:**
- [ ] Application Performance Monitoring (APM)
- [ ] Error tracking (Sentry / Application Insights)
- [ ] Metrics collection (Prometheus)
- [ ] Distributed tracing (Jaeger / Zipkin)
- [ ] Centralized logging (ELK stack / Seq)

**Database:**
- [ ] Database backup automation
- [ ] Database restore procedures
- [ ] Migration rollback strategy
- [ ] Database replication (read replicas)
- [ ] Database monitoring and alerting

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
