# HybridIdP 待辦事項

> 📝 本文件列出所有待完成的 Phases 和功能

最後更新：2025-11-06

---

## 當前優先級

### 🎯 Next Up: Phase 5.5a - Settings Key/Value Store & Dynamic Branding

Phase 4.x 全部子階段已完成（詳見 `progress_completed.md`）。接下來專注於 Phase 5.5a，建立通用的設定服務與品牌動態化，為後續 Email/Security 設定鋪路。

本階段重點：

- [x] DB：新增 `Settings` entity 與 migration（Key 唯一、UpdatedUtc）✅ commit `feat(settings): Add Settings entity, SettingsService with caching, and BrandingService`
- [x] Service：`ISettingsService` + `SettingsService`（MemoryCache、快取失效）✅ commit `feat(settings): Add Settings entity, SettingsService with caching, and BrandingService`
- [x] Branding：讀取順序 DB > appsettings > 內建預設 ✅ commit `feat(settings): Integrate BrandingService in Razor views and add Settings API`
- [x] API：Admin 設定端點（讀取/更新/快取失效）✅ commit `feat(settings): Integrate BrandingService in Razor views and add Settings API`
- [x] UI：Admin Settings（先做 Branding，Email/Security 之後）✅ commit `feat(settings): Add Settings UI with branding configuration`
- [ ] Tests：型別化讀取、快取失效、migration 覆蓋

完成後再銜接 Phase 5.1–5.5 的安全策略工作。

---

<!-- Phase 4.x 已全部完成，移至 progress_completed.md 保存記錄 -->

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

### Phase 5.5a: Settings Key/Value Store & Dynamic Branding

**目標：** 建立通用的系統設定儲存（Key/Value）機制，支援動態品牌名稱、後續 Email/Security 相關設定集中化。

**資料庫 (提案)：** `Settings` 資料表

| Column | Type | Notes |
| ------ | ---- | ----- |
| Id | uuid / bigint | 主鍵 |
| Key | text (unique) | 命名建議：`branding.appName`, `branding.productName`, `security.password.minLength` |
| Value | text | 原始字串；可 JSON 儲存複合結構 |
| DataType | varchar(50) | string, int, bool, json |
| UpdatedUtc | timestamp | 最後更新時間，用於快取失效 |
| UpdatedBy | string | 管理員帳號/Id |

**服務介面：**
```csharp
public interface ISettingsService {
  Task<string?> GetValueAsync(string key, CancellationToken ct = default);
  Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default);
  Task SetValueAsync(string key, object value, string? updatedBy = null, CancellationToken ct = default);
  Task<IDictionary<string,string>> GetByPrefixAsync(string prefix, CancellationToken ct = default);
}
```

**快取策略：**
- MemoryCache + ETag/UpdatedUtc 比對
- 讀取 Key 時若快取不存在或過期（超過 N 分鐘或 UpdatedUtc 變更）則回源 DB
- 後續可升級 Redis（Phase 6+）

**品牌整合：**
- 目前 `BrandingOptions` 讀取 appsettings → 日後改為 SettingsService fallback 順序：DB > appsettings > 內建預設
- UI 管理（未實作）：`/Admin/Settings` → Vue SPA (Phase 5.5a 或 6.1)

**API（預留路由草稿）：**
- `GET /api/admin/settings?prefix=branding.`
- `PUT /api/admin/settings/branding.appName` (body: { value: "Contoso" })
- `PUT /api/admin/settings/branding.productName`

**權限需求：**
- 新增 permissions: `settings.read`, `settings.update`

**驗證 / 測試：**
- 單元測試：設定 CRUD、類型轉換、快取失效
- 整合測試：更新品牌後重新載入頁面顯示新名稱

**風險 & 緩解：**
- 過度抽象 → 先最小可行：字串/數值型支援，再擴充 JSON
- 熱更新延遲 → 提供 `POST /api/admin/settings/invalidate-cache`

**未來延伸：** Email SMTP、Token Lifetime、Password Policy 視覺化編輯、Turnstile 參數

---
 
---

<!-- Phase 4.7 已完成，詳細紀錄請見 progress_completed.md -->

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
