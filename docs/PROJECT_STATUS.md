# HybridIdP 專案狀態

## 🎯 簡介

本文件整合了 HybridAuth IdP 專案的已完成功能摘要和待辦事項，提供一個清晰的專案進度概覽。

**當前狀態（2025-11-20）：**
- ✅ **Phase 1-6：核心功能已完成** (OIDC Flow, Admin UI, User/Role/Client/Scope Management, Security Policies, MFA, API Resources, Session Management)
- ✅ **Phase 6.1：單元測試覆蓋率已達標** (226 tests passing, 87%+ coverage achieved)
- ✅ **Phase 6.2：ClaimsController 重構已完成** (23 unit tests, thin controller pattern)
- ✅ **Phase 6.3：ScopeClaimsController 整合已完成** (8 unit tests, integrated into ScopeService)
- ✅ **Phase 6.4：異常登入偵測-管理者解除封鎖已完成** (3 unit tests, admin unblock functionality)
- ✅ **Phase 7.1：Audit Logging Infrastructure 已完成** (AuditEvent entity, service layer, domain events, EF migration, 10 unit tests, API endpoints)
- ✅ **Phase 7.1a：AuditService 整合至重點系統** (Domain Events 解耦整合, User/Role/Client/Scope 服務稽核, TDD 測試驅動) - UserManagementService ✅, ClientService ✅, RoleManagementService ✅, ScopeService ✅
- ✅ **Phase 7.2：Audit Log Viewer UI 已完成** (Vue.js audit log viewer, sorting/pagination/filtering, i18n support, CSV/Excel export, 7 audit events displayed)
- ✅ **Phase 7.3：異常登入管理 UI 已完成** (LoginHistoryDialog with abnormal login approval, visual indicators, E2E testing)
- ✅ **Phase 7.4：即時活動儀表板已完成** (SignalR real-time dashboard, monitoring cards, security metrics, activity stats, E2E tested)

### Recent progress (2025-11-27)

Short summary of E2E work completed and stabilization efforts today:

- Removed leftover debug logging from Playwright tests & helper scripts to keep test output clean.
- Added focused Playwright E2E tests for the ClientScopeManager UI (add/remove allowed scopes, pagination, search, required-scope toggle persistence).
- Introduced a TypeScript script + helper to programmatically recreate or update the canonical `testclient-public` client used by tests.
- Hardened Playwright global setup and helpers: global-setup now polls `/api/admin/health` before logging in, and helpers include an `ensureAdminAvailable` wait helper to avoid ERR_CONNECTION_REFUSED flakes when services are starting.
- Fixed Admin UI persistence bug: ClientForm now always sends both `allowedScopes` and `requiredScopes` on save to ensure required-scope updates persist reliably.

Next steps / notes:

- Full E2E runs should be executed in a stable environment (use `e2e/wait-for-idp-ready.ps1` or `scripts/run-e2e.ps1 -StartServices`) so Playwright doesn't start before services are ready.
- If you want me to continue, I can run a full headless test pass and triage any failing specs.

**架構狀態分析：**
- ✅ 已重構完成（Thin Controller + Service Pattern）：
  - ClientsController → ClientService (240 行，41 單元測試 ✅)
  - UsersController → UserManagementService (250 行，14 單元測試 ✅)
  - RolesController → RoleManagementService (156 行，14 單元測試 ✅)
  - ScopesController → ScopeService (109 行，24 單元測試 ✅)
  - ApiResourcesController → ApiResourceService (130 行，23 單元測試 ✅)
  - SettingsController → SettingsService (89 行，14 單元測試 ✅)
  - SecurityPolicyController → SecurityPolicyService (52 行，已有單元測試)
  - LoginService (6 單元測試 ✅)
  - JitProvisioningService (2 單元測試 ✅)
  - ClientAllowedScopesService (12 單元測試 ✅)
  - LoginHistoryService (8 單元測試 ✅)
  
- ✅ 所有 Controllers 已重構完成（Thin Controller + Service Pattern）
  - ClaimsController (252→80 行) - ~~**Phase 6.2 優先級最高**~~ ✅ **Phase 6.2 已完成**
  - ScopeClaimsController (154 行) - ~~**Phase 6.3 整合至 ScopeService**~~ ✅ **Phase 6.3 已完成，已刪除**

**測試覆蓋率現況：**
- 總單元測試：**226 tests (100% passing)** ✅
- 覆蓋率：**~87%** (已達標！)
- 測試分布：
  - ClientService: 41 tests (sorting, paging, search, CRUD validation)
  - ScopeService: 32 tests (list/create/update/delete with resources & consent + scope claims GET/PUT) ✅ **Updated!**
  - ClaimsService: 23 tests (list/filter/sort/pagination, CRUD with standard claim protection)
  - ApiResourceService: 23 tests (full CRUD with scope associations)
  - UserManagementService: 14 tests (list/filter/search, roles, audit)
  - RoleManagementService: 14 tests (CRUD with permissions validation)
  - SettingsService: 14 tests (get/set, type conversion, caching)
  - ClientAllowedScopesService: 12 tests (scope validation)
  - LoginHistoryService: 8 tests (record/detect abnormal login + admin approval)
  - DynamicPasswordValidator: 8 tests
  - LoginService: 6 tests (auth with lockout)
  - JitProvisioningService: 2 tests

---

## ✅ 已完成功能

> 本節記錄所有已完成的 Phases，採用摘要格式以節省 token

最後更新：2025-11-18

### Phase 1: PostgreSQL & Entity Framework Core ✅

**完成時間：** Phase 1 完成

**功能摘要：**
-   PostgreSQL Docker 容器配置 (docker-compose.yml)
-   ApplicationDbContext 配置（PostgreSQL provider）
-   ApplicationUser 和 ApplicationRole 實體定義
-   初始資料庫遷移建立
-   基本測試用戶：admin@example.com / Admin123! (Admin 角色)

**技術細節：**
-   Database: PostgreSQL 17
-   ORM: Entity Framework Core 9
-   Connection String: 環境變數配置於 appsettings.Development.json

### Phase 2: OpenIddict Integration & OIDC Flow ✅

**完成時間：** Phase 2 完成

**功能摘要：**
-   OpenIddict 6.x 整合（Authorization Code Flow with PKCE）
-   ASP.NET Core Identity 整合
-   TestClient 應用程式實作（MVC 客戶端）
-   Custom Claims Factory (preferred_username, department)
-   JIT Provisioning Service (OIDC 使用者自動建立)

**API Endpoints:**
-   `/connect/authorize` - OIDC Authorization endpoint
-   `/connect/token` - Token endpoint
-   `/connect/userinfo` - UserInfo endpoint

**驗證結果：**
-   ✅ 完整 OIDC 登入流程
-   ✅ Consent 頁面正常運作
-   ✅ Claims 正確傳遞至 TestClient
-   ✅ Department claim 顯示於 Profile 頁面

### Phase 3.1: Admin Layout & Navigation ✅

**完成時間：** Phase 3.1 完成

**功能摘要：**
-   Hybrid 架構確立（Bootstrap 5 + Vue.js 3 + Tailwind CSS）
-   `_AdminLayout.cshtml` 佈局建立（Bootstrap 5 CDN）
-   管理員角色授權檢查 `[Authorize(Roles = "Admin")]`
-   響應式側邊欄導航（260px 固定寬度，手機版可收合）

**UI Routes:**
-   `/Admin` - Dashboard
-   `/Admin/Clients` - OIDC Client Management
-   `/Admin/Scopes` - Scope Management
-   `/Admin/Users` - User Management
-   `/Admin/Roles` - Role Management (待實作)

**技術架構：**
-   外層佈局：Bootstrap 5.3.2 (CDN)
-   內容區域：Vue.js 3.5.13 (Vite 5.4.21)
````markdown
# HybridIdP 專案狀態 - 索引（已拆分）

本文件原先為大型單一檔案（詳細記錄、截圖與完整測試輸出），為了便於維護與快速查閱，已將內容拆分為多個 Phase 檔案，並提供一個精簡的進度摘要。

快速入口：

- `docs/PROJECT_PROGRESS.md` — 專案進度摘要（每個 Phase 的完成度與連結）
- `docs/phase-1-database-ef-core.md`
- `docs/phase-2-openiddict-oidc.md`
- `docs/admin-ui-phase-3.md`
- `docs/phase-4-user-role-client.md`
- `docs/phase-5-security-i18n-consent.md`
- `docs/phase-6-code-quality-tests.md`
- `docs/phase-7-audit-monitoring.md`
- `docs/backlog-and-debt.md`
- `docs/notes-and-guidelines.md`

說明：

- 若要快速查看當前進度與應處理項目，請先開啟 `docs/PROJECT_PROGRESS.md`。
- 若需深入某個 Phase 的實作細節（包含 API、測試與截圖），請按 `PROJECT_PROGRESS.md` 中的對應連結前往各 Phase 檔案。
- 本檔保留為「索引/歸檔」，不再維護為單一巨檔；所有新增或變更的進度請更新 `docs/PROJECT_PROGRESS.md` 與相應的 `docs/phase-*.md`。

---

````
-   GET /api/admin/users/{id} (詳細資料，包含 roles 和 claims)
-   POST /api/admin/users (建立用戶)
-   PUT /api/admin/users/{id} (更新用戶)
-   DELETE /api/admin/users/{id} (刪除用戶)
-   POST /api/admin/users/{id}/activate (啟用)
-   POST /api/admin/users/{id}/deactivate (停用)
-   POST /api/admin/users/{id}/roles (管理角色)

**UI Features:**
-   User 列表（Email, Name, Department, Roles, Status）
-   搜尋功能（Email/Username）
-   角色篩選（All/Admin/User）
-   建立 User 表單（Email, Password, Name, Department）
-   編輯 User（更新基本資料）
-   Manage Roles（角色多選）
-   Activate/Deactivate 切換
-   刪除確認

**驗證結果（Playwright MCP）：**
-   ✅ 列表載入正常（11 users，分頁顯示）
-   ✅ 搜尋功能正常（testuser@example.com）
-   ✅ 建立用戶成功（testuser@example.com / IT / Active）
-   ✅ 編輯用戶成功（Department: "Engineering - Backend Team"）
-   ✅ Manage Roles 成功（分配 User 角色）
-   ✅ Activate/Deactivate 切換正常
-   ✅ Tailwind CSS 樣式正常（已修復 style.css import 問題）

**Commits:**
-   `4a1b3fc` - fix: Add missing Tailwind CSS import to Users management page
-   `3a052bd` - docs: Add Tailwind CSS setup warnings to requirements
-   `e3ddd27` - docs: Add Vite dev server warnings to testing guide
-   `0c14d6f` - docs: Add comprehensive git commit strategy (Option A) to requirements

### Phase 4.5: Role Management UI ✅

**完成時間：** 2025-11-04

**功能摘要：**
-   Role CRUD 完整實作
-   Permission 分配管理（按類別分組）
-   系統角色保護（Admin, User 不可刪除/重命名）
-   分配用戶數追蹤
-   權限選擇器（Category-level 全選功能）

**API Endpoints:**
-   GET /api/admin/roles (分頁列表，包含 userCount 和 permissionCount)
-   GET /api/admin/roles/{id} (詳細資料)
-   POST /api/admin/roles (建立角色)
-   PUT /api/admin/roles/{id} (更新角色)
-   DELETE /api/admin/roles/{id} (刪除角色，檢查系統角色和分配用戶)
-   GET /api/admin/roles/permissions (所有可用的權限列表)

**UI Features:**
-   Role 列表（Name, Description, Permissions Count, Users Count, Is System）
-   建立 Role Modal（Name, Description, Permissions selector with categories）
-   編輯 Role Modal（系統角色 Name 欄位禁用，權限預選）
-   刪除 Role Modal（系統角色和有用戶分配的角色顯示保護警告）
-   權限分類顯示（Clients, Scopes, Users, Roles, Audit, Settings）
-   Category-level checkboxes（indeterminate state 支援）

**驗證結果（Playwright MCP）：**
-   ✅ 列表載入正常（Admin: 1 user, User: 3 users, 均為 0 permissions）
-   ✅ 建立角色成功（"Content Editor" with users.read, scopes.read）
-   ✅ 編輯角色成功（添加 users.update，權限數從 2 增至 3）
-   ✅ 系統角色保護正常（Admin 顯示 "Users Assigned" 警告，無法刪除）
-   ✅ 刪除功能正常（Content Editor 成功刪除）
-   ✅ Vite 配置正確（admin-roles entry point）

**Commits:**
-   `2f8a045` - feat(ui): Add CreateRoleModal with permission selector
-   `41b3e7d` - feat(ui): Add EditRoleModal and DeleteRoleModal with protections
-   `7329767` - fix(config): Add admin-roles entry to vite.config.js and fix Roles.cshtml script tag

### Phase 4.6: Permission System Implementation ✅

**完成時間：** 2025-01-10

**目標：** 為所有 Admin API 端點實施細粒度的基於權限的授權

**Permission Infrastructure（已存在）：**
-   Permission Constants (`Core.Domain/Constants/Permissions.cs`)
    -   6 categories: Clients, Scopes, Users, Roles, Audit, Settings
    -   17 total permissions (clients.read/create/update/delete, etc.)
-   Authorization Components:
    -   `PermissionRequirement` - IAuthorizationRequirement 實作
    -   `PermissionAuthorizationHandler` - 檢查 Admin role bypass & role-based permissions
    -   `HasPermissionAttribute` - Policy-based authorization attribute
    -   Program.cs - Policy registration for all permissions

**實施內容：**
-   Applied `[HasPermission]` to 24 Admin API endpoints:
    -   **Clients:** 5 endpoints (Read/Create/Update/Delete)
    -   **Scopes:** 5 endpoints (Read/Create/Update/Delete)
    -   **Users:** 7 endpoints (Read/Create/Update/Delete + Reactivate + Update Roles)
    -   **Claims:** 7 endpoints (Read/Create/Update/Delete + Scope Claims Read/Update)
-   Roles endpoints already had HasPermission (verified)

**Authorization Behavior:**
-   Admin role: Full access to all endpoints (bypass)
-   Other roles: Permission checked against `ApplicationRole.Permissions` string (comma-separated)
-   Unauthorized: 403 Forbidden response

**Commits:**
-   `d076500` - feat(auth): Apply permission-based authorization to Clients, Scopes, and Users endpoints
-   `00c58ab` - feat(auth): Apply permission-based authorization to Claims management endpoints

**技術細節:**
-   Modified: `Web.IdP/Api/AdminController.cs` (24 endpoints updated)
-   Permission Check: PermissionAuthorizationHandler checks user's roles for required permission
-   Claims as Scopes: Claim management uses Scopes.* permissions (logical grouping)

### Phase 4.7: UI Spacing & Visual Consistency Review ✅

**完成時間：** 2025-11-08

**功能摘要：**
-   引入統一的 Spacing Scale 與語義化間距 class
-   新增共享樣式 `ClientApp/src/admin/shared/spacing.css`
-   匯入共享樣式於 `admin/shared/admin-shared.css`（不影響既有功能）
-   調整與統一：輸入欄位間距、模態 body/footer、表格儲存格 padding（依據既有修正補完）
-   在 `DEVELOPMENT_GUIDE.md` 新增「UI 間距規範」章節（使用方式與範例）

**涵蓋頁面：**
-   Users、Roles、Clients、Scopes、Claims、Dashboard（以不破壞既有行為為原則提供通用 utilities）

**驗證結果：**
-   ✅ 既有功能不受影響（僅新增 class 與共享樣式）
-   ✅ 自訂語義化 class 可逐步採用，與 Tailwind/Bootstrap 共存
-   ✅ 文件已更新，未來頁面可直接套用一致間距

### Phase 5.1: Internationalized Identity Errors ✅

**完成時間：** 2025-11-09

**功能摘要：**
-   實作多語言化的 ASP.NET Core Identity 錯誤訊息。
-   建立 `SharedResource.resx` (英文) 和 `SharedResource.zh-TW.resx` (繁體中文) 資源檔。
-   建立自訂 `LocalizedIdentityErrorDescriber` 類別，用於從資源檔中獲取翻譯後的錯誤訊息。
-   在 `Web.IdP/Program.cs` 中配置應用程式的本地化服務，並將 `LocalizedIdentityErrorDescriber` 註冊到 Identity 服務中。
-   支援根據瀏覽器 `Accept-Language` 標頭動態切換語言。

**技術實作：**
-   `Web.IdP/Resources/SharedResource.resx`
-   `Web.IdP/Resources/SharedResource.zh-TW.resx`
-   `Infrastructure/Identity/LocalizedIdentityErrorDescriber.cs`
-   `Web.IdP/Program.cs` (配置 `AddLocalization`, `Configure<RequestLocalizationOptions>`, `AddErrorDescriber`)
-   `Infrastructure/Infrastructure.csproj` (新增 `Microsoft.Extensions.Localization` 參考)

**驗證結果：**
-   ✅ 專案成功編譯，無相關錯誤。
-   ✅ `LocalizedIdentityErrorDescriber` 中的 `InvalidUserName` 參數 nullability 警告已解決。
-   ✅ 應用程式已準備好根據用戶語言設定顯示本地化的 Identity 錯誤訊息。

### Phase 5.2: TDD for Dynamic Password Validator ✅

**完成時間：** 2025-11-09

**功能摘要：**
-   建立 `DynamicPasswordValidatorTests.cs` 測試檔案，包含針對密碼策略驗證的單元測試。
-   測試涵蓋了最小長度、非英數字元、數字、小寫字母、大寫字母等基本複雜度要求。
-   建立 `Infrastructure/Identity/DynamicPasswordValidator.cs` 類別的骨架，使其能夠編譯並被測試專案引用。
-   驗證所有新撰寫的測試在 `DynamicPasswordValidator` 尚未實作實際驗證邏輯時，均按預期失敗（TDD 的 Red 階段）。

**技術實作：**
-   `Tests.Application.UnitTests/DynamicPasswordValidatorTests.cs` (包含多個測試案例)
-   `Infrastructure/Identity/DynamicPasswordValidator.cs` (初始骨架，暫時返回 `IdentityResult.Success`)

**驗證結果：**
-   ✅ `DynamicPasswordValidatorTests` 中的所有測試均已編譯成功。
-   ✅ 所有測試均按預期失敗，確認了 TDD 的 Red 階段已達成。
-   ⚠️ 注意：`SettingsServiceTests` 中存在與本任務無關的測試失敗，將在後續處理。

### Phase 5.4: API & UI for Security Policies ✅

**完成時間：** 2025-11-09

**功能摘要：**
-   實作了 `SecurityPolicyDto`，用於在前端和後端之間傳輸安全策略數據，並包含數據驗證屬性。
-   擴展了 `ISecurityPolicyService` 介面和 `SecurityPolicyService` 實作，新增 `UpdatePolicyAsync` 方法，用於更新安全策略。`SecurityPolicyService` 現在能夠從 `SecurityPolicyDto` 更新現有策略，並在更新後使快取失效。
-   創建了 `SecurityPolicyController`，提供了 `GET /api/admin/security/policies` 端點用於獲取當前安全策略，以及 `PUT /api/admin/security/policies` 端點用於更新安全策略。
-   API 端點受到 `settings.read` 和 `settings.update` 權限的保護。
-   實作了 Vue SPA (`ClientApp/src/admin/security/SecurityApp.vue`)，提供管理員介面來管理安全策略。
-   UI 包含密碼要求、密碼歷史、密碼過期和帳戶鎖定等策略編輯區塊。
-   UI 提供實時驗證反饋，並支援保存和應用策略。

**技術實作：**
-   `Core.Application/DTOs/SecurityPolicyDto.cs`
-   `Core.Application/ISecurityPolicyService.cs` (新增 `UpdatePolicyAsync` 方法)
-   `Infrastructure/Services/SecurityPolicyService.cs` (實作 `UpdatePolicyAsync` 方法，包含日誌和快取失效)
-   `Web.IdP/Api/Admin/SecurityPolicyController.cs` (GET 和 PUT 端點)
-   `Core.Application/IApplicationDbContext.cs` (新增 `DbSet<SecurityPolicy> SecurityPolicies { get; }` 以解決編譯錯誤)
-   `ClientApp/src/admin/security/SecurityApp.vue` (Vue SPA for Security Policy Editor)
-   `Pages/Admin/Security.cshtml` (Razor Page for mounting Vue SPA)

**驗證結果：**
-   ✅ 後端專案成功編譯，無錯誤。
-   ✅ API 端點已準備就緒，可供前端 UI 調用。
-   ✅ 管理員可以透過 UI 查看和更新安全策略。
-   ✅ 策略變更會立即生效，並在 UI 中提供驗證反饋。

### Phase 5.5: Integrate Policy System ✅

**完成時間：** 2025-11-09

**功能摘要：**
-   成功將 `DynamicPasswordValidator<ApplicationUser>` 註冊到 ASP.NET Core Identity 的服務容器中，確保密碼驗證流程能夠使用動態策略。
-   由於未來與 Active Directory 整合的規劃，使用者自助密碼變更、帳號管理顯示策略要求以及密碼過期檢查等相關任務已暫時移至待辦事項 (Backlog) 區塊。

**技術實作：**
-   `Web.IdP/Program.cs` (註冊 `DynamicPasswordValidator<ApplicationUser>`)

**驗證結果：**
-   ✅ `DynamicPasswordValidator` 已正確註冊並可被 Identity 系統使用。
-   ✅ 專案編譯成功，無相關錯誤。

### Phase 5.5a: Settings Key/Value Store & Dynamic Branding ✅

**完成時間：** 2025-11-09

**功能摘要：**
-   建立通用的設定服務與品牌動態化，為後續 Email/Security 設定鋪路。
-   DB：新增 `Settings` entity 與 migration（Key 唯一、UpdatedUtc）
-   Service：`ISettingsService` + `SettingsService`（MemoryCache、快取失效）
-   Branding：讀取順序 DB > appsettings > 內建預設
-   API：Admin 設定端點（讀取/更新/快取失效）
-   UI：Admin Settings（先做 Branding，Email/Security 之後）
-   Tests：E2E via Playwright MCP - Settings CRUD, cache invalidation, branding display

**驗證結果：**
-   ✅ Settings Key/Value Store with dynamic branding fully working, tested end-to-end.

### Phase 6.1: Service Layer Unit Tests ✅

**完成時間：** 2025-11-12

**目標：** 提升服務層單元測試覆蓋率至 80%+，確保核心業務邏輯的穩定性與可維護性

**功能摘要：**
-   為所有核心服務補充完整單元測試，涵蓋正常流程與邊界情況
-   採用批次測試策略（一次補完一個服務的所有測試 → 運行 → 單次提交）
-   使用 Moq 框架模擬依賴，xUnit 作為測試框架
-   針對 EF Core 查詢，實作同步/異步兼容的解決方案

**測試涵蓋範圍：**
-   **ClientService** (41 tests): 列表查詢（排序/分頁/搜尋）、CRUD 驗證（類型推斷、URI 過濾、權限預設）、密鑰重生
-   **ScopeService** (24 tests): 列表/搜尋/排序/分頁、建立（重複檢查、明確資源）、更新（資源替換、部分 consent 欄位）、刪除（使用中檢查、例外處理）
-   **ApiResourceService** (23 tests): 完整 CRUD、scope 關聯、cascade delete
-   **UserManagementService** (14 tests): 列表/過濾/搜尋、角色指派、稽核欄位、最後登入時間
-   **RoleManagementService** (14 tests): 權限驗證、系統角色保護、使用者計數
-   **SettingsService** (14 tests): 型別轉換、快取機制、前綴搜尋
-   **ClientAllowedScopesService** (12 tests): scope 驗證與權限管理
-   **LoginService** (6 tests): 驗證流程、帳戶鎖定、legacy auth
-   **JitProvisioningService** (2 tests): 使用者自動建立與更新
-   **DynamicPasswordValidator** (8 tests): 密碼強度驗證

**技術實作：**
-   `Tests.Application.UnitTests/ClientServiceTests.cs` (41 tests)
-   `Tests.Application.UnitTests/ScopeServiceTests.cs` (24 tests)
-   `Tests.Application.UnitTests/UserManagementTests.cs` (14 tests)
-   `Tests.Application.UnitTests/RoleManagementServiceTests.cs` (14 tests)
-   `Tests.Application.UnitTests/SettingsServiceTests.cs` (14 tests)
-   `Tests.Application.UnitTests/ApiResourceServiceTests.cs` (23 tests)
-   `Tests.Application.UnitTests/ClientAllowedScopesServiceTests.cs` (12 tests)
-   `Tests.Application.UnitTests/LoginServiceTests.cs` (6 tests)
-   `Tests.Application.UnitTests/JitProvisioningServiceTests.cs` (2 tests)
-   `Tests.Application.UnitTests/DynamicPasswordValidatorTests.cs` (8 tests)
-   `Infrastructure/Services/UserManagementService.cs` (重構為同步查詢以支援測試)

**驗證結果：**
-   ✅ **158 tests 全部通過** (100% passing rate)
-   ✅ **測試覆蓋率：~85%** (已達標！超越 80% 目標)
-   ✅ 所有核心服務層邏輯均有完整測試保護
-   ✅ 測試執行時間：< 3 秒（高效快速）
-   ✅ CI/CD ready：測試可在任何環境獨立運行

---

## 技術堆疊總結 (已完成)

-   **完成的 Phases:** 16
-   **API Endpoints:** 36+ (24 with permission-based auth)
-   **UI Pages:** 8
-   **Commits:** 58 (採用 Small Steps 策略)
-   **測試涵蓋率:**
    -   Unit Tests: Core.Application, Infrastructure
    -   E2E Tests: OIDC Flow, Admin Portal CRUD (Clients, Scopes, Users, Roles)

---

## 📝 待辦事項

> 本節列出所有待完成的 Phases 和功能

最後更新：2025-11-06

### Phase 5.6 Part 1: Consent Screen Customization ✅

**完成時間：** 2025-11-10

**目標：** 提供豐富的同意畫面自訂功能，讓管理員可以為每個 scope 定義友善的顯示名稱、說明、圖示、類別和必要性標記

#### 實施內容

**Database Schema:**
-   ✅ 建立 `ScopeExtension` 表格，包含以下欄位：
    -   `ConsentDisplayName` (nvarchar(200), nullable) - 同意畫面顯示名稱
    -   `ConsentDescription` (nvarchar(500), nullable) - 權限說明
    -   `IconUrl` (nvarchar(200), nullable) - 圖示 URL 或 CSS 類別 (如 "bi bi-shield-check")
    -   `IsRequired` (bool, default false) - 必要 scope，使用者無法取消勾選
    -   `DisplayOrder` (int, default 0) - 顯示順序（數字越小越前面）
    -   `Category` (nvarchar(100), nullable) - 類別分組 (如 "個人資料", "API 存取")
    -   `ScopeId` (Guid, FK) - 關聯到 OpenIddict Scopes，具唯一索引
-   ✅ 建立 `Resource` 表格（預備未來 i18n 支援）
    -   Composite unique key on (Key, Culture)
-   ✅ EF Core Migration: `20251110105526_AddScopeExtensionAndResourceTables`

**Backend API:**
-   ✅ 擴展 `ScopeDtos.cs` (ScopeSummary, CreateScopeRequest, UpdateScopeRequest)
    -   新增 6 個 consent 相關屬性（全部 nullable）
-   ✅ 更新 `ScopesController.cs` 4 個端點：
    -   `GetScopes`: 使用 `ToDictionaryAsync` 高效 join ScopeExtensions
    -   `Create`: 若提供 consent 欄位則建立 ScopeExtension
    -   `Update`: 更新或建立 ScopeExtension（nullable 欄位處理）
    -   `Delete`: 級聯刪除關聯的 ScopeExtension

**Frontend (Admin UI):**
-   ✅ 增強 `ScopeForm.vue` 新增「Consent Screen Customization」區塊
    -   6 個輸入欄位：ConsentDisplayName, ConsentDescription, IconUrl, Category (select), DisplayOrder (number), IsRequired (checkbox)
-   ✅ 完整 i18n 支援（16 個翻譯 keys，支援 en-US 和 zh-TW）
    -   翻譯涵蓋：section title/help、所有欄位 label/placeholder/help、類別選項
-   ✅ 表單驗證與 payload 構建（null fallback 處理）

**Frontend (User-Facing Consent Screen):**
-   ✅ 重構 `Authorize.cshtml.cs` PageModel：
    -   新增 `ScopeInfo` nested class（8 個屬性）
    -   實作 `LoadScopeInfosAsync` 方法：join OpenIddict scopes 與 ScopeExtensions，按 DisplayOrder 和 Name 排序
-   ✅ 完全重寫 `Authorize.cshtml` Razor view：
    -   Category 分組顯示（使用 LINQ `.GroupBy()`）
    -   顯示 category 標題（當有多個類別時）
    -   Bootstrap Icons 或自訂圖示渲染（fallback to standard icons）
    -   ConsentDisplayName 或 DisplayName 顯示
    -   IsRequired scope 顯示黃色 "Required" 徽章
    -   ConsentDescription 以小字灰色文字顯示

#### E2E 驗證結果（Playwright MCP）

**測試場景：** 完整 consent customization 流程
1.  ✅ 管理員登入 Admin Portal
2.  ✅ 建立測試 scope "test_consent" with 完整 consent fields：
    -   ConsentDisplayName: "Access Your Test Data"
    -   ConsentDescription: "This allows the application to read your test data for E2E testing purposes"
    -   IconUrl: "bi bi-shield-check"
    -   Category: "個人資料" (Profile)
    -   DisplayOrder: 10
    -   IsRequired: true (勾選)
3.  ✅ 編輯 scope 驗證資料持久化：所有欄位正確載入和顯示
4.  ✅ 觸發 OIDC 授權流程（手動構建 authorize URL with test_consent scope）
5.  ✅ 驗證 consent screen 顯示：
    -   ✅ Category 分組：顯示 "General" 和 "Profile" 兩個群組
    -   ✅ Custom icon：shield icon (bi bi-shield-check) 正確渲染
    -   ✅ Custom display name："Access Your Test Data" 顯示
    -   ✅ Required badge：黃色 "Required" 徽章顯示在 scope 旁
    -   ✅ Custom description：說明文字以灰色小字顯示在下方
    -   ✅ Display order：test_consent scope 顯示在 Profile 群組中

**截圖證據：**
-   Before: `consent-screen-before-customization.png` - 舊版簡單列表
-   After: `consent-screen-with-customization.png` - 新版分類、圖示、說明、徽章完整顯示

#### Git Commits（Small Steps 策略）

```bash
feat(db): Add ScopeExtension and Resource tables for consent customization
feat(api): Extend Scope DTOs with 6 consent customization fields
feat(api): Update ScopesController CRUD to handle ScopeExtension
feat(ui): Add Consent Screen Customization section to ScopeForm with i18n
feat(ui): Refactor user consent screen with grouping, icons, descriptions
```

#### 技術亮點

-   **Efficient DB Query**: `ToDictionaryAsync` 避免 N+1 query 問題
-   **Nullable Design**: 所有 consent 欄位為 optional，向後相容既有 scopes
-   **i18n Ready**: Resource table 已準備好支援未來多語系 consent text
-   **Bootstrap Icons**: 支援 CSS class (如 "bi bi-envelope") 或 image URL
-   **Category Grouping**: LINQ `.GroupBy()` 動態分組，可擴展至任意類別
-   **Required Badge**: 視覺化標記必要 scope，提升使用者理解

#### 已知限制與未來增強

-   ⚠️ 刪除有 client 使用的 scope 會失敗（400 error）- 需改善錯誤訊息
-   📝 Resource table 尚未使用（預留給 Part 2 多語系 i18n）
-   📝 Consent screen 未實作「取消勾選必要 scope」的 UI 禁用邏輯
-   📝 Icon preview 功能尚未實作（admin 端只有文字輸入）

#### 後續計劃

**Phase 5.6 Part 2: API Resource Scopes**（待實作）
-   API Resource 實體與管理介面
-   Scope 分配到 API Resources
-   Access token audience claim

**Phase 5.6 Part 3: Scope Authorization Policies**（待實作）
-   Client 允許的 scopes 白名單管理
-   授權請求驗證與拒絕邏輯

---

### Phase 5.6 Part 2: API Resource Scopes ✅

**完成時間：** 2025-11-11

**目標：** 實作 API Resource 管理，將 scopes 分組至不同的 API 資源，組織和管理 OAuth2 授權範圍

#### 實施內容

**Database Schema:**
-   ✅ 建立 `ApiResource` entity 與 migration
    -   Id, Name (unique), DisplayName, Description, BaseUrl
    -   CreatedAt, UpdatedAt timestamps
    -   Scopes collection (One-to-Many)
-   ✅ 建立 `ApiResourceScope` entity（Join table）
    -   ApiResourceId (FK), ScopeId (FK to OpenIddict)
    -   Many-to-Many relationship
-   ✅ EF Core Migration: `20251111113128_AddApiResourceAndApiResourceScopeTables`
    -   Unique index on ApiResource.Name
    -   Cascade delete configured

**Backend API:**
-   ✅ DTOs (`Core.Application/DTOs/ApiResourceDtos.cs`):
    -   `ApiResourceSummary` (list view with ScopeCount)
    -   `ApiResourceDetail` (with Scopes array)
    -   `ResourceScopeInfo` (ScopeId, Name, DisplayName)
    -   `CreateApiResourceRequest` ([Required] Name, validation attributes)
    -   `UpdateApiResourceRequest` (nullable fields)
-   ✅ Service Layer (`Infrastructure/Services/ApiResourceService.cs`):
    -   `IApiResourceService` interface with 6 methods
    -   `ApiResourceService` implementation with:
        -   Pagination & sorting (name/displayName)
        -   Search filtering
        -   Scope management (add/remove)
        -   Duplicate name validation
        -   Cascade delete with scope cleanup
        -   Comprehensive logging
-   ✅ Thin Controller (`Web.IdP/Api/ApiResourcesController.cs`):
    -   6 endpoints with `[HasPermission(Permissions.Scopes.*)]`
    -   GET /api/admin/resources (list with pagination)
    -   GET /api/admin/resources/{id} (detail with scopes)
    -   POST /api/admin/resources (create, returns 201)
    -   PUT /api/admin/resources/{id} (update)
    -   DELETE /api/admin/resources/{id} (delete)
    -   GET /api/admin/resources/{id}/scopes (scopes only)
-   ✅ Service registration in `Program.cs`

**Frontend (Admin UI):**
-   ✅ Vue SPA (`ClientApp/src/admin/resources/`):
    -   `ResourcesApp.vue` (269 lines) - Main app with CRUD handlers
    -   `components/ResourceList.vue` - Table with formatting
    -   `components/ResourceForm.vue` - Modal form with scope multi-select
    -   `main.js` - Vue 3 app initialization
    -   `style.css` - Tailwind CSS imports
-   ✅ Razor Page (`Pages/Admin/Resources.cshtml`):
    -   `[Authorize(Policy = Permissions.Scopes.Read)]`
    -   Mounts Vue SPA at `#resources-app`
-   ✅ Navigation Update (`_AdminLayout.cshtml`):
    -   Added "Resources" menu item in OIDC Management section
-   ✅ i18n Support:
    -   Frontend translations in `ClientApp/src/i18n/locales/en-US.json`
    -   Chinese translations in `zh-TW.json`
    -   50+ translation keys for resources section
    -   Backend translations in `Web.IdP/Resources/*.resx`

**Unit Tests:**
-   ✅ Comprehensive test suite (`Tests.Application.UnitTests/ApiResourceServiceTests.cs`):
    -   19 unit tests covering all service methods
    -   In-memory database provider (EF Core)
    -   Moq for ApplicationDbContext
    -   Test coverage:
        -   GetResourcesAsync: All/Filter/Sort/Pagination (4 tests)
        -   GetResourceByIdAsync: Found/NotFound/WithScopes (3 tests)
        -   CreateResourceAsync: Success/Duplicate/WithScopes (3 tests)
        -   UpdateResourceAsync: Success/NotFound/UpdateScopes/RemoveScopes (4 tests)
        -   DeleteResourceAsync: Success/NotFound/CascadeDeleteScopes (3 tests)
        -   GetResourceScopesAsync: Success/NotFound (2 tests)
    -   ✅ All 19 tests passing (execution time: 2.45s)

#### E2E 驗證結果

**API Endpoint Tests (Playwright MCP):**
-   ✅ GET /api/admin/resources - 200 OK, returned 2 resources
-   ✅ POST /api/admin/resources - 201 Created, resource "test-api" created
-   ✅ GET /api/admin/resources/{id} - 200 OK, returned resource with scopes
-   ✅ PUT /api/admin/resources/{id} - 200 OK, updated description and scopes
-   ✅ DELETE /api/admin/resources/{id} - 200 OK, resource deleted
-   ✅ GET /api/admin/resources/{id}/scopes - 200 OK, returned scope list
-   ✅ Unauthorized test - 401 when token missing

**UI Tests (Playwright MCP):**
1.  ✅ **CREATE Test:**
    -   Logged in as admin@hybridauth.local
    -   Navigated to /Admin/Resources
    -   Clicked "建立新資源" button
    -   Filled form: name="payment-api", displayName="Payment API"
    -   Description: "API for payment processing and transactions"
    -   BaseUrl: "https://api.payment.example.com"
    -   Selected scopes: email ✓, openid ✓
    -   Submitted → Resource created successfully
    -   List shows 2 resources (payment-api, test-api)

2.  ✅ **READ Test:**
    -   List displays resources with proper formatting
    -   Scope count badges: "2 個範圍" displayed correctly
    -   Clickable base URL shown
    -   Last updated timestamp formatted in Chinese locale

3.  ✅ **UPDATE Test:**
    -   Clicked "編輯" button for payment-api
    -   Modal loaded with existing data
    -   Added "profile" scope (3 scopes total)
    -   Updated description
    -   Saved → Success message displayed
    -   List refreshed showing "3 個範圍"
    -   Timestamp updated to reflect change

4.  ✅ **DELETE Test:**
    -   Clicked "刪除" button for test-api
    -   Confirmation dialog: "您確定要刪除此 API 資源嗎？所有範圍關聯都將被移除。"
    -   Accepted → Resource deleted
    -   List refreshed showing only payment-api
    -   Pagination updated: "顯示第 1 至 1 項結果，共 1 項"

5.  ✅ **i18n Validation:**
    -   All labels properly translated in Chinese
    -   Page title: "API 資源管理"
    -   Buttons: "建立新資源", "編輯", "刪除"
    -   Form labels and placeholders all in Chinese
    -   Validation messages in Chinese

#### Git Commits（Small Steps 策略）

```bash
feat(db): Add ApiResource and ApiResourceScope entities with migration
feat(api): Add ApiResource DTOs with validation
feat(api): Implement IApiResourceService and ApiResourceService with CRUD operations
feat(api): Add ApiResourcesController with thin controller pattern
feat(api): Add DbSets to IApplicationDbContext for API resources
feat(api): Add backend i18n translations for API resources
test(api): Add comprehensive unit tests for ApiResourceService (19 tests)
docs(api): Add API resource endpoint test results documentation
feat(ui): Add Vue SPA for API resource management (CRUD UI)
feat(ui): Add Resources Razor page to mount Vue SPA
feat(ui): Add frontend i18n translations for resources
```

**Total Commits:** 10 (following small step strategy)

#### 技術亮點

-   **Service-Repository Pattern**: Thin controller delegates all logic to service layer
-   **Pagination & Sorting**: Efficient database queries with LINQ
-   **Scope Management**: Many-to-Many relationship with join entity pattern
-   **Cascade Delete**: Automatically removes ApiResourceScope entries
-   **Duplicate Prevention**: Unique constraint and validation on Name field
-   **Comprehensive Testing**: 19 unit tests + 7 API endpoint tests + full UI E2E testing
-   **i18n Support**: Separate frontend (vue-i18n) and backend (Resources) translations
-   **Authorization**: Permission-based access control (Permissions.Scopes.*)
-   **Vue 3 Composition API**: Modern reactive patterns with `<script setup>`
-   **Tailwind CSS**: Utility-first styling with consistent design system

#### 架構說明

**API Resources 用途:**
API Resources 用於組織相關的 scopes，將它們歸類到特定的 API 服務中。例如：
-   **Payment API** (payment-api): payment:read, payment:write, payment:refund
-   **User API** (user-api): user.profile:read, user.profile:update

**OAuth2 驗證流程:**
1.  Client 向 IdP 請求 token，指定需要的 scopes
2.  IdP 發行 token 時，在 JWT 的 `aud` (audience) claim 中包含相關的 API Resource names
3.  Client 使用 token 呼叫 API
4.  API Server 驗證 token 的 `aud` claim 是否包含自己的 resource name
5.  若 `aud` 不符，拒絕請求（403 Forbidden）

**Token 範例:**
```json
{
  "aud": ["payment-api", "user-api"],
  "scope": "payment:read user.profile:read",
  "client_id": "mobile-app"
}
```

**關鍵欄位:**
-   **Name**: 唯一識別符，用於 JWT `aud` claim
-   **BaseUrl**: API 的基礎 URL（僅用於文件說明，不參與驗證）
-   **Scopes**: 與此 resource 關聯的權限列表

#### 已知限制與未來增強

-   ⚠️ 目前僅實作 CRUD 管理，尚未整合至 OpenIddict token 發行流程
-   📝 BaseUrl 欄位僅供文件參考，實際驗證使用 JWT `aud` claim
-   📝 未實作 Client 選擇 API Resources 的 UI（需在 Phase 5.6 Part 3 實作）
-   📝 Access token 中的 `aud` claim 需額外配置 OpenIddict

---

### Phase 5.6 Part 3: Scope Authorization Policies (Whitelisting) - Backend ✅

**完成時間：** 2025-11-11

**目標：** 實作 Client 允許的 scopes 白名單管理，防止未授權的 scope 請求

#### 實施內容（後端）

**Backend Service & API:**
-   ✅ Service Interface (`Core.Application/IClientAllowedScopesService.cs`):
    -   `GetAllowedScopesAsync(Guid clientId)` - 取得允許的 scopes
    -   `SetAllowedScopesAsync(Guid clientId, IEnumerable<string> scopes)` - 設定允許的 scopes
    -   `IsScopeAllowedAsync(Guid clientId, string scope)` - 檢查單一 scope 是否允許
    -   `ValidateRequestedScopesAsync(Guid clientId, IEnumerable<string> requestedScopes)` - 驗證並過濾請求的 scopes
-   ✅ Service Implementation (`Infrastructure/Services/ClientAllowedScopesService.cs`):
    -   使用 `IOpenIddictApplicationManager` 管理 client permissions
    -   過濾 `scp:` prefix 的 permissions（OpenIddict scope 格式）
    -   更新時保留非 scope permissions（endpoints, grant types）
    -   Client 不存在時拋出 `InvalidOperationException`
-   ✅ Thin Controller (`Web.IdP/Api/ClientsController.cs`):
    -   GET `/api/admin/clients/{id}/scopes` - 回傳 `{ scopes: string[] }`
    -   PUT `/api/admin/clients/{id}/scopes` - 請求 body: `{ scopes: string[] }`
    -   POST `/api/admin/clients/{id}/scopes/validate` - 請求 body: `{ requestedScopes: string[] }`，回傳 `{ allowedScopes: string[] }`
    -   Authorization: `[HasPermission(DomainPermissions.Clients.*)]`
-   ✅ Service registration in `Program.cs` (line 142)

**Unit Tests:**
-   ✅ Comprehensive test suite (`Tests.Application.UnitTests/ClientAllowedScopesServiceTests.cs`):
    -   14 unit tests covering all service methods
    -   Moq for `IOpenIddictApplicationManager`
    -   Test coverage:
        -   GetAllowedScopesAsync: 3 tests (found, not found, no scope permissions)
        -   SetAllowedScopesAsync: 3 tests (success, not found, preserve non-scope)
        -   IsScopeAllowedAsync: 3 tests (allowed, not allowed, client not found)
        -   ValidateRequestedScopesAsync: 5 tests (all allowed, partial, none, not found, empty)
    -   ✅ All 14 tests passing (execution time: 1.1s)

#### E2E 驗證結果（Backend API）

**API Endpoint Tests (Playwright MCP):**
-   ✅ GET `/api/admin/clients/{id}/scopes` - 200 OK, returned `["openid", "profile", "email", "roles", "test_consent"]`
-   ✅ PUT `/api/admin/clients/{id}/scopes` - 200 OK, updated scopes to `["openid", "profile", "email"]`, persistence verified
-   ✅ POST `/api/admin/clients/{id}/scopes/validate` - 200 OK, correctly filtered requested scopes (removed "notallowed")
    -   Request: `["openid", "profile", "notallowed", "email"]`
    -   Response: `["openid", "profile", "email"]`

**Test Client ID:** `e33bdff0-2367-4d60-858c-e324f11f8583`

#### Git Commits（Small Steps 策略）

```bash
5c55b7c - feat(api): Add IClientAllowedScopesService interface
1d56d88 - test(api): Add comprehensive unit tests for ClientAllowedScopesService (14 tests)
832550d - feat(api): Implement ClientAllowedScopesService with OpenIddict integration
cf7fe4e - feat(api): Add thin controller endpoints for client allowed scopes
```

**Total Commits:** 4 (following small step strategy)

#### 技術亮點

-   **OpenIddict Integration**: 直接使用 OpenIddict 的 Permission 系統管理 scopes
-   **Permission Prefix**: 使用 `scp:` prefix 區分 scopes 與其他 permissions
-   **Preserve Non-Scope Permissions**: 更新 scopes 時自動保留 endpoints 和 grant types
-   **Comprehensive Testing**: 14 unit tests + 3 API endpoint E2E tests
-   **Service Pattern**: Thin controller 完全委派業務邏輯給 service layer
-   **Validation**: 內建 scope 驗證與過濾機制
-   **Error Handling**: Client 不存在時明確拋出例外

#### 架構說明

**OpenIddict Permission 格式:**
-   Endpoints: `ept:authorization`, `ept:token`, `ept:userinfo`
-   Grant Types: `gt:authorization_code`, `gt:client_credentials`
-   Scopes: `scp:openid`, `scp:profile`, `scp:email`, `scp:custom_scope`

**Scope Whitelisting 驗證流程:**
1.  Client 向 IdP 請求 token，指定需要的 scopes（如 `openid profile email custom_scope`）
2.  IdP 呼叫 `ValidateRequestedScopesAsync` 驗證並過濾
3.  只有在 whitelist 中的 scopes 會被包含在 token 中
4.  未授權的 scopes 被靜默移除（不會拋出錯誤）

**API 使用範例:**
```bash
# 取得允許的 scopes
GET /api/admin/clients/{id}/scopes
Response: { "scopes": ["openid", "profile", "email"] }

# 更新允許的 scopes
PUT /api/admin/clients/{id}/scopes
Request: { "scopes": ["openid", "profile", "email", "roles"] }

# 驗證請求的 scopes
POST /api/admin/clients/{id}/scopes/validate
Request: { "requestedScopes": ["openid", "profile", "invalid_scope"] }
Response: { "allowedScopes": ["openid", "profile"] }
```

---

### Phase 5.6 Part 3: Scope Authorization Policies (Whitelisting) - Frontend ✅

**完成時間：** 2025-11-11

**目標：** 在 ClientForm.vue 中實作 Allowed Scopes UI

#### 實施內容（前端）

**Frontend Implementation:**
-   ✅ Added "Allowed Scopes" multi-select section in `ClientForm.vue`
-   ✅ Fetch available scopes from `/api/admin/scopes` endpoint (take=1000 to get all)
-   ✅ Group scopes by category with computed property:
    -   **Identity Scopes**: openid, profile, email, address, phone, offline_access
    -   **API Resource Scopes**: Scopes with `resources` array (detected from scope entity)
    -   **Custom Scopes**: Other uncategorized scopes
-   ✅ Integrated API endpoints:
    -   GET `/api/admin/clients/{id}/scopes` - Load existing allowed scopes
    -   PUT `/api/admin/clients/{id}/scopes` - Save allowed scopes
-   ✅ i18n translations added (en-US, zh-TW):
    -   `allowedScopes`, `allowedScopesHelp`, `allowedScopesRequired`
    -   `allowedScopesOpenidRequired`, `allowedScopesLoading`, `allowedScopesNone`
    -   `scopeCategories.identity`, `scopeCategories.apiResource`, `scopeCategories.custom`
-   ✅ Validation: Zod schema validates `openid` scope is included
-   ✅ UI: Checkbox multi-select grouped by category, with scope descriptions

**State Management:**
-   Reactive state: `availableScopes`, `scopesLoading`, `scopesError`
-   Computed property: `categorizedScopes` for grouping logic
-   Form data: Added `allowedScopes` array to `formData`
-   Auto-fetch scopes on component mount
-   Load client allowed scopes when editing (watch for `props.client`)

**UX Features:**
-   Loading indicator while fetching scopes
-   Error display if scope loading fails
-   Empty state message if no scopes available
-   Display scope name, display name, and description
-   Field-level validation error display

#### E2E 驗證結果（Frontend UI）

**Playwright MCP Tests (手動執行):**
-   ✅ Scope selection UI interaction - Toggled "Roles" checkbox successfully
-   ✅ Saving allowed scopes - Saved "Roles" scope, verified persistence on reload
-   ✅ Scope validation - Unchecked "openid" scope triggered error: "OIDC 用戶端必須包含 'openid' 範圍"
-   ✅ Category grouping display - Three categories displayed correctly:
    -   身分範圍 (Identity Scopes): Email, OpenID, Profile
    -   API 資源範圍 (API Resource Scopes): Roles
    -   自訂範圍 (Custom Scopes): Test Consent
-   ✅ i18n translations - Switched language, verified English translations:
    -   "Allowed Scopes", "Identity Scopes", "API Resource Scopes", "Custom Scopes"
    -   Help text displayed correctly in both languages

**Test Client:** test_client (e33bdff0-2367-4d60-858c-e324f11f8583)

#### Git Commits（Small Steps 策略）

```bash
# 將在下一步執行 git add/commit
feat(ui): Add i18n translations for Allowed Scopes UI
feat(ui): Add Allowed Scopes section to ClientForm.vue with category grouping
feat(ui): Implement scope fetching and state management
feat(ui): Add openid scope validation
feat(ui): Integrate GET/PUT allowed scopes API endpoints
```

**Total Commits:** 預計 4-5 個 (following small step strategy)

#### 技術亮點

-   **Category Auto-Detection**: Scopes automatically grouped by identity standards vs API resources
-   **Computed Property Pattern**: Efficient reactive grouping with Vue 3 composition API
-   **Async Loading**: Non-blocking scope fetch with loading/error states
-   **Zod Validation**: Client-side validation ensures `openid` scope requirement
-   **i18n Complete**: Full bilingual support (en-US, zh-TW)
-   **UX Polish**: Loading indicators, error messages, empty states, help text
-   **Persistence**: Seamless load/save via dedicated API endpoints

#### 架構說明

**Scope Categorization Logic:**
```javascript
const identityScopes = ['openid', 'profile', 'email', 'address', 'phone', 'offline_access']
- If scope.name in identityScopes → Identity Scopes
- Else if scope.resources.length > 0 → API Resource Scopes  
- Else → Custom Scopes
```

**Component Lifecycle:**
1. Component mounts → Fetch all scopes from `/api/admin/scopes?skip=0&take=1000`
2. Watch `props.client` → If editing, fetch client's allowed scopes
3. User selects scopes → Update `formData.allowedScopes` array
4. User submits → Validate (require `openid`), then:
   - Save client basic info
   - Call PUT `/api/admin/clients/{id}/scopes` with selected scopes
5. Success → Close modal, refresh client list

**UI Component Structure:**
```
ClientForm.vue
├── Permissions Section (existing)
└── Allowed Scopes Section (new)
    ├── Loading State (spinner + text)
    ├── Error State (error message)
    ├── Empty State ("no scopes available")
    └── Scope Categories (if scopes loaded)
        ├── Identity Scopes (heading + checkboxes)
        ├── API Resource Scopes (heading + checkboxes)
        └── Custom Scopes (heading + checkboxes)
```

---

### Phase 5.6 Part 4: Consent Screen Multi-language & Interactive Controls ✅

**完成時間：** 2025-11-20

**目標：** 實作同意畫面多語言支援、圖示預覽、以及必要範圍的取消/停用邏輯

#### 實施內容

**Localization Service:**
-   ✅ `ILocalizationService` interface with `GetLocalizedStringAsync(string key, string culture)`
-   ✅ `LocalizationService` implementation querying Resource table with fallback to "en-US"
-   ✅ Unit tests: 5 comprehensive tests covering success, fallback, and error cases

**Database Schema Updates:**
-   ✅ Updated `ScopeExtension` entity:
    -   Renamed `ConsentDisplayName` → `ConsentDisplayNameKey`
    -   Renamed `ConsentDescription` → `ConsentDescriptionKey`
    -   Both fields now store localization keys instead of direct text
-   ✅ Updated EF Core configuration and migrations

**Backend API Updates:**
-   ✅ Modified `Authorize.cshtml.cs`:
    -   Added `ILocalizationService` dependency injection
    -   Updated `LoadScopeInfosAsync` to fetch localized text from Resource table
    -   Added `FilterGrantedScopes` method for partial grant support
    -   Modified `OnPostAsync` to handle `granted_scopes[]` form parameter
    -   Required scopes are always included regardless of user selection
-   ✅ Updated `ScopeService` and DTOs to handle key-based fields
-   ✅ Updated all unit tests to use new property names

**Frontend Updates:**
-   ✅ Updated `ScopeForm.vue`:
    -   Changed form fields from `consentDisplayName`/`consentDescription` to `consentDisplayNameKey`/`consentDescriptionKey`
    -   Added icon preview component showing Bootstrap icons or images
    -   Updated form validation and submission
-   ✅ Updated i18n files (en-US, zh-TW):
    -   Added form labels and help text for localization keys
    -   Added icon preview and category labels
    -   Added placeholder text and validation messages

**Consent Screen Interactive Controls:**
-   ✅ Transformed static scope list to interactive checkboxes
-   ✅ Required scopes are pre-checked and disabled (cannot be unchecked)
-   ✅ Backend supports partial grants while enforcing required scopes
-   ✅ Improved UX with better visual hierarchy and accessibility

**Sample Data:**
-   ✅ Created `setup-test-localization.sql` with sample Resource entries:
    -   English: scope.profile.display, scope.profile.description, etc.
    -   Traditional Chinese: 存取您的個人資料, 此範圍允許應用程式存取您的基本個人資料...

#### 測試覆蓋率

**Unit Tests:**
-   ✅ `LocalizationServiceTests`: 5 tests (100% passing)
-   ✅ `ScopeServiceTests`: Updated 32 tests for new property names
-   ✅ All existing tests: 263 tests passing (0 regressions)

**Integration Tests:**
-   ✅ Build verification: All projects compile successfully
-   ✅ Database schema: EF Core migrations applied correctly

#### Git Commits

```bash
eff4a68 - feat: Implement Phase 5.6 Part 3 - Multi-language consent text, icon preview, and scope consent controls
```

#### 技術亮點

-   **Localization Architecture**: Resource table with key-culture-value pattern
-   **Fallback Strategy**: Automatic fallback to default culture ("en-US")
-   **Partial Grants**: Support for users to decline optional scopes
-   **Required Scope Enforcement**: Backend ensures required scopes are always granted
-   **Interactive Consent**: Checkbox-based UI with proper accessibility
-   **Icon Preview**: Live preview for Bootstrap icons and custom images
-   **i18n Complete**: Full bilingual support for all new UI elements

#### TODO: 待實作項目

**E2E Playwright Tests:**
-   Consent page interaction tests (checkbox selection, required scope disable)
-   Multi-language text display verification
-   Icon rendering validation
-   Partial grant flow testing

**TestClient Integration Tests:**
-   Consent flow automation with TestClient
-   Multi-language consent verification
-   Required scope enforcement testing
-   Partial grant API validation

**Scope Authorization Policies 前端連動:**
-   Verify client allowed scopes validation in authorization flow
-   Ensure ClientForm.vue properly manages allowed scopes enforcement

---

### Phase 5.7: Client Service Refactoring & Secret Management ✅

**完成時間：** 2025-11-11

**目標：** 重構 ClientService 的密碼驗證邏輯，修復單元測試，並進行完整的 E2E 驗證

#### 實施內容

**Bug Fixes:**
-   ✅ Fixed `CreateClientAsync` validation logic:
    -   Moved "Public client with secret" validation **before** type inference
    -   Reorganized confidential client secret generation logic
    -   Ensured auto-generated secrets are 32-byte base64url encoded
-   ✅ Fixed 8 unit test cases with incorrect parameter order:
    -   `CreateClientRequest` constructor: (ClientId, ClientSecret, DisplayName, ApplicationType, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
    -   `UpdateClientRequest` constructor: (ClientId, ClientSecret, DisplayName, Type, ConsentType, RedirectUris, PostLogoutRedirectUris, Permissions)
    -   Tests were passing wrong values to wrong parameters (e.g., DisplayName to ClientSecret position)

**Test Results:**
-   ✅ **Unit Tests**: 125/125 tests passing (100% success rate, 0 regressions)
    -   Fixed tests:
        -   `CreateClientAsync_ShouldThrowArgumentException_WhenPublicClientHasSecret`
        -   `CreateClientAsync_ShouldGenerateSecret_WhenConfidentialClientWithoutSecret`
        -   `UpdateClientAsync_ShouldSetClientTypeToConfidential_WhenSecretProvided`
        -   5 additional tests with parameter mapping fixes

**E2E Validation (Playwright MCP):**
-   ✅ **CREATE Operation**: Created confidential client "e2e-test-client"
    -   Auto-generated secret: `kAxi1CixgN-ko1H2kUbyBZ9U3la9Hog-W4nBpKJmjvs` (32-byte base64url)
    -   Secret display modal with one-time security warning
    -   Client list updated (5 → 6 clients)
-   ✅ **READ Operation**: Client list displaying correctly
    -   All 6 clients visible with metadata (Redirect URIs, Type, Display Name)
    -   Pagination: "顯示第 1 至 6 項結果，共 6 項"
-   ✅ **UPDATE Operation**: Edit modal loaded and validated
    -   Client ID field disabled (read-only)
    -   All data pre-populated correctly (Display Name, Redirect URI, Permissions, Scopes)
-   ✅ **DELETE Operation**: Successfully deleted test client
    -   Confirmation dialog: "確定要刪除這個用戶端嗎？"
    -   Client removed from list (6 → 5 clients)
    -   Pagination updated: "顯示第 1 至 5 項結果，共 5 項"
-   ✅ **REGENERATE SECRET Operation**: Regenerated secret for existing client
    -   Confirmation dialog: "您確定要為 "test_client" 重新產生密鑰嗎？舊密鑰將立即失效。"
    -   New secret generated: `WQy1z25iNgKGHPmOpxawJxuygUp5QxCLK913b0HYBTo` (32-byte base64url)
    -   Old secret immediately invalidated

#### Git Commits

```bash
fix(api): Reorganize CreateClientAsync validation for public clients with secrets
test(api): Fix ClientServiceTests parameter order in request constructors
docs: Update PROJECT_STATUS.md with Phase 5.7 completion and E2E test results
```

**Total Commits:** 3

#### 技術亮點

-   **Validation Logic**: Public client + secret check moved before type inference
-   **Secret Generation**: Secure 32-byte base64url-encoded secrets using `RandomNumberGenerator`
-   **Test Coverage**: 100% unit test pass rate (125/125)
-   **E2E Validation**: Full CRUD cycle tested with Playwright MCP
-   **Security**: One-time secret display with explicit warnings
-   **User Experience**: Clear confirmation dialogs for destructive operations

#### 驗證結果

-   ✅ All 125 unit tests passing (0 failures, 0 regressions)
-   ✅ All 5 CRUD operations validated via E2E testing
-   ✅ Secret auto-generation working correctly for confidential clients
-   ✅ Public client validation preventing secret assignment
-   ✅ Client type switching when secret is added/removed
-   ✅ UI properly displaying secret once and warning users

#### Production Ready

Phase 5.7 refactoring is **production ready**. All tests passing, no regressions detected, full E2E validation completed.

---

## 🚧 Phase 6: Code Quality & Technical Debt Reduction (進行中)

**目標：** 重構 fat controllers，提升測試覆蓋率至 80%+，建立可維護的程式碼基礎

**完成時間：** 預計 2025-11-18

### Phase 6.1: 補充現有 Services 的 Unit Tests (規劃中)

**優先級：** ⭐⭐⭐ 最高

**目標：**
- 檢查現有 Services 的測試覆蓋率（ClientService, UserManagementService, RoleManagementService, ScopeService, SettingsService, SecurityPolicyService）
- 補充缺失的測試案例（edge cases, error handling, validation）
- 確保每個 Service 都有完整的單元測試
- 目標測試覆蓋率：80%+

**預估時間：** 2-3 天

**為什麼優先？**
- 核心功能已完成，確保品質才能安心前進
- 防止未來修改時引入 regression
- 為後續重構提供安全網

---

### Phase 6.2: 重構 ClaimsController → ClaimsService ✅

**完成時間：** 2025-01-22

**成果：**
- ✅ 創建 `IClaimsService` interface 和 `ClaimsService` implementation (288 行)
- ✅ 將 ClaimsController 從 252 行重構為 ~80 行 thin controller
- ✅ 撰寫 23 個單元測試 (100% passing)：
  - GetClaimsAsync: 6 tests (all/filter/sort/pagination/scope count)
  - GetClaimByIdAsync: 3 tests (found/not found/includes scope claims)
  - CreateClaimAsync: 5 tests (success/defaults/duplicate/validation)
  - UpdateClaimAsync: 5 tests (success/standard protection/partial update)
  - DeleteClaimAsync: 4 tests (success/not found/standard claim/in use)
- ✅ E2E 測試通過 (Playwright MCP): LIST/CREATE/UPDATE/DELETE 無 regression
- ✅ 註冊服務至 DI 容器 (Program.cs line 144)

**技術實現：**
- Service 方法：GetClaimsAsync, GetClaimByIdAsync, CreateClaimAsync, UpdateClaimAsync, DeleteClaimAsync
- 包含搜尋、排序、分頁邏輯
- 標準 claim 保護：禁止修改 ClaimType/UserPropertyPath/DataType/IsRequired
- 欄位預設值：DisplayName→Name, UserPropertyPath→Name, DataType→"String", IsStandard→false
- TODO 註解：行 24-29 標記 Include 優化考量（deferred loading, projection, aggregation）
- 保留 HasPermission 授權於 Controller layer

**Commits:**
1. `test: Add ClaimsServiceTests with 23 comprehensive unit tests`
2. `feat: Create IClaimsService interface`
3. `feat: Implement ClaimsService with business logic extraction`
4. `feat: Register IClaimsService in DI container`
5. `refactor: Convert ClaimsController to thin controller pattern`

---

### Phase 6.3: 重構 ScopeClaimsController → 整合至 ScopeService ✅

**完成時間：** 2025-01-22

**成果：**
- ✅ 在 `IScopeService` 中添加 `GetScopeClaimsAsync`, `UpdateScopeClaimsAsync` 方法
- ✅ 撰寫 8 個單元測試 (100% passing)：
  - GetScopeClaimsAsync: 3 tests (scope not found/empty list/correct DTO mapping)
  - UpdateScopeClaimsAsync: 5 tests (scope not found/claim not found/remove old and add new/AlwaysInclude from IsRequired/allow empty list)
- ✅ 實作 ScopeService 的 scope claims 方法 (97 行新增)
- ✅ 整合至 ScopesController，添加 GET/PUT /api/admin/scopes/{scopeId}/claims endpoints
- ✅ 刪除 ScopeClaimsController.cs (154 行移除)

**技術實現：**
- 使用 EF Core projection 直接映射到 ScopeClaimDto
- UpdateScopeClaimsAsync 使用 RemoveRange + Add 模式
- AlwaysInclude 自動從 UserClaim.IsRequired 設定
- 保留路由結構 `/api/admin/scopes/{scopeId}/claims`
- 異常映射：KeyNotFoundException→404, ArgumentException→400
- 保留 HasPermission 授權於 Controller layer

**Commits:**
1. `test: Add ScopeService scope claims tests (8 new tests)`
2. `feat: Extend IScopeService with scope claims methods`
3. `feat: Implement scope claims methods in ScopeService`
4. `feat: Add scope claims endpoints to ScopesController`
5. `refactor: Remove ScopeClaimsController after integration`

---

### Phase 6.4: 異常登入偵測 - 管理者解除封鎖 ✅

**完成時間：** 2025-11-16

**功能摘要：**
- 實作管理者手動解除異常登入封鎖功能
- 允許管理員批准可疑登入嘗試，信任特定 IP 位址
- 遵循 TDD 開發流程：單元測試 → 實作 → E2E 測試

**API Endpoints:**
- `GET /api/admin/users/{id}/login-history` - 取得使用者登入歷史
- `POST /api/admin/users/{id}/login-history/{loginHistoryId}/approve` - 批准異常登入

**技術實現：**
- 新增 `IsApprovedByAdmin` 欄位至 `LoginHistory` 實體
- 擴展 `ILoginHistoryService.ApproveAbnormalLoginAsync` 方法
- 更新 `DetectAbnormalLoginAsync` 邏輯，考慮已批准的 IP
- 新增 EF Core 遷移 `AddIsApprovedByAdminToLoginHistory`
- 單元測試覆蓋率：3 個新測試 (100% passing)
- E2E 測試：Playwright API 端點驗證

**安全考量：**
- 僅限具有 `users.update` 權限的管理員可批准異常登入
- 批准後該 IP 將被視為信任來源，不再觸發異常偵測
- 保留完整稽核記錄

**Commits:**
1. `feat: Add IsApprovedByAdmin field to LoginHistory entity`
2. `feat: Extend ILoginHistoryService with ApproveAbnormalLoginAsync`
3. `feat: Implement abnormal login approval in LoginHistoryService`
4. `test: Add unit tests for ApproveAbnormalLoginAsync (3 tests)`
5. `feat: Add approve abnormal login API endpoint`
6. `db: Add migration for IsApprovedByAdmin field`
7. `test: Add E2E test for approve endpoint`

---

### Phase 7.1a: AuditService 整合至重點系統 (Domain Events 解耦整合) ✅ 已完成

**目標：** 將 AuditService 整合至 UserManagementService、ClientService、RoleManagementService、ScopeService 等重點業務服務，使用 Domain Events 實現解耦合設計。

**預估 token：** ~2000  
**預估時間：** 3-4 天

**功能範圍：**
- Domain Events 架構擴展 (IDomainEventHandler 介面)
- 業務服務 Domain Event 觸發
- AuditService 作為 Event Handler 訂閱並記錄稽核事件
- 關鍵業務操作稽核記錄 (CRUD 操作、權限變更、安全策略更新)
- TDD 測試驅動開發 (每個整合點的單元測試)

**整合服務清單：**

- ✅ **UserManagementService**: 用戶 CRUD、角色分配、密碼變更、帳戶狀態變更 (已完成，14 單元測試通過)
- ✅ **ClientService**: Client 建立/更新/刪除、Secret 管理、Scope 權限變更 (已完成，46 單元測試通過)
- ✅ **RoleManagementService**: 角色 CRUD、權限分配變更 (已完成，16 單元測試通過)
- ✅ **ScopeService**: Scope 管理、Claim 關聯變更 (已完成，32 單元測試通過)
- 📋 **LoginService**: 登入/登出事件、失敗嘗試追蹤
- 📋 **SecurityPolicyService**: 安全策略更新、密碼政策變更

**技術實現重點：**

- **解耦合設計**: 業務邏輯不直接依賴 AuditService，使用 Domain Events
- **Event Types**: UserCreated, UserUpdated, UserDeleted, ClientModified, RoleChanged, ScopeUpdated, LoginAttempt, SecurityPolicyChanged
- **Audit Fields**: 符合台灣資安法 (用戶ID、動作、時間戳、IP位址、詳細資訊)
- **測試覆蓋**: 每個整合點的單元測試 + Domain Event 發佈驗證

**Domain Events 架構：**

```csharp
// 業務服務觸發事件
await _domainEventPublisher.PublishAsync(new UserCreatedEvent(user.Id, user.UserName));

// AuditService 訂閱處理
public class AuditService : IDomainEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(UserCreatedEvent @event)
    {
        await LogEventAsync(AuditEventTypes.UserCreated, 
            @event.UserId, 
            $"User '{@event.UserName}' created", 
            GetClientIP(), 
            GetUserAgent());
    }
}
```

**開發策略：**

- 每個服務單獨 commit (API → Tests → Integration)
- 先實作 Domain Events 架構，再逐一整合服務
- 確保所有業務邏輯保持解耦合
- 完整單元測試覆蓋 (Event 發佈 + Handler 處理)

---

## Phase 7: Audit & Monitoring System

> Phase 7 將實作完整的稽核與監控系統，分為多個子階段以控制開發複雜度與 token 消耗

### Phase 7.1: 基礎稽核日誌架構 (Audit Logging Infrastructure)

**目標：** 建立事件驅動的稽核日誌系統
**預估 token：** ~3000
**預估時間：** 2-3 天

**功能範圍：**

- 定義 AuditEvent 實體與相關 DTOs
- 實作 IAuditService 介面與 AuditService
- 建立 Domain Events 系統
- 新增 EF Core 遷移與索引優化
- 單元測試覆蓋 (100% passing)

**API Endpoints:**

- `GET /api/admin/audit/events` - 查詢稽核事件
- `POST /api/admin/audit/events/{id}/export` - 匯出特定事件

### Phase 7.2: 稽核日誌檢視器 UI (Audit Log Viewer UI) ✅ 已完成

**完成時間：** 2025-11-18

**目標：** 實作完整的稽核日誌檢視器 UI，提供管理員查看、篩選、排序和匯出系統稽核事件的功能

#### 實施內容

**Frontend Vue.js Components:**
- ✅ **AuditApp.vue** (269 行) - 主應用程式元件，負責狀態管理與 API 整合
- ✅ **AuditLogViewer.vue** (201 行) - 稽核事件表格元件，包含排序、分頁和載入狀態
- ✅ **AuditLogFilters.vue** (145 行) - 篩選表單元件，支援日期範圍、使用者、事件類型、IP 位址篩選
- ✅ **AuditLogExport.vue** (89 行) - 匯出元件，提供 CSV 和 Excel 匯出功能

**Backend Integration:**
- ✅ 使用現有的 `/api/admin/audit/events` API 端點
- ✅ 支援分頁、排序和篩選參數
- ✅ 權限驗證：`audit.read` 權限檢查

**UI Features:**
- ✅ **表格顯示**：時間戳記、事件類型、使用者、詳細資訊、IP 位址欄位
- ✅ **排序功能**：點擊欄位標題進行升序/降序排序，包含視覺指示器
- ✅ **分頁控制**：支援每頁 10/25/50/100 筆資料，顯示總計和分頁資訊
- ✅ **進階篩選**：日期範圍、使用者搜尋、事件類型下拉選單、IP 位址篩選、一般搜尋
- ✅ **匯出功能**：CSV 和 Excel 格式匯出，包含所有篩選後的資料
- ✅ **載入狀態**：載入中動畫和錯誤處理
- ✅ **空狀態**：無資料時的友善提示

**i18n 本地化支援：**
- ✅ **中文 (zh-TW)**：完整翻譯所有 UI 文字、表格標題、按鈕和訊息
- ✅ **英文 (en-US)**：完整英文支援
- ✅ **表格標題**：新增 `admin.audit.tableHeaders` 命名空間
- ✅ **動態翻譯**：事件類型、狀態和錯誤訊息的本地化

**Navigation & Permissions:**
- ✅ 新增稽核選單項目至管理員側邊欄
- ✅ Razor Page：`Pages/Admin/Audit.cshtml` 與權限授權
- ✅ Vite 配置：新增 `admin-audit` 進入點

**技術實作亮點：**
- **Composition API**：使用 Vue 3 Composition API 進行響應式狀態管理
- **效能優化**：computed properties 用於分頁計算，debounced 搜尋
- **Type Safety**：完整的 TypeScript 支援與介面定義
- **一致性設計**：遵循現有管理介面設計模式和 Tailwind CSS 樣式
- **錯誤處理**：完善的錯誤狀態顯示和恢復機制

#### E2E 驗證結果（Playwright MCP）

**功能測試：**
- ✅ **頁面載入**：成功載入 `/Admin/Audit` 頁面，Vue 應用程式正確掛載
- ✅ **資料顯示**：顯示 7 個真實稽核事件，包含各種事件類型（ScopeClaimChanged、ScopeUpdated、ScopeCreated、RoleDeleted、RolePermissionChanged、RoleUpdated、RoleCreated）
- ✅ **表格功能**：所有欄位正確顯示（時間戳記、事件類型、使用者、詳細資訊、IP 位址）
- ✅ **中文本地化**：所有 UI 元素正確顯示中文翻譯，無 i18n 警告
- ✅ **分頁控制**：顯示 "顯示第 1 至 7 項結果，共 7 項"，分頁按鈕狀態正確
- ✅ **篩選器 UI**：所有篩選欄位正常顯示（開始日期、結束日期、使用者搜尋、事件類型下拉選單、IP 位址、一般搜尋）
- ✅ **匯出按鈕**：CSV 和 Excel 匯出按鈕正常顯示並可點擊
- ✅ **排序功能**：欄位標題顯示排序箭頭，支援點擊排序

**資料驗證：**
- ✅ **事件類型**：正確顯示各種稽核事件類型，包含視覺化徽章
- ✅ **使用者欄位**：系統事件顯示 "系統"，其他顯示實際使用者
- ✅ **時間格式**：時間戳記正確格式化為本地時間
- ✅ **IP 位址**：未知 IP 顯示 "未知"，已知 IP 正確顯示
- ✅ **詳細資訊**：長文字正確截斷，hover 顯示完整內容

#### Git Commits（Small Steps 策略）

```bash
feat: Implement Phase 7.2 Audit Log Viewer UI

- Add Vue.js audit log viewer with sorting, pagination, and filtering
- Create AuditApp.vue main component with reactive state management
- Implement AuditLogViewer.vue with table display and sorting controls
- Add AuditLogFilters.vue for date, user, event type, and IP filtering
- Add AuditLogExport.vue for CSV and Excel export functionality
- Integrate with backend /api/admin/audit/events endpoint
- Add proper i18n localization for Chinese (zh-TW) and English (en-US)
- Update navigation and permissions for audit access
- Configure Vite build for admin-audit entry point
- Add tableHeaders translations for proper column headers
- Fix i18n key references to use admin.audit namespace
- Test with real audit data showing 7 events with proper formatting
```

**Commit Hash:** `370c9e5`

#### 架構說明

**Component Architecture:**
```
AuditApp.vue (Main App)
├── AuditLogFilters.vue (Filtering UI)
├── AuditLogViewer.vue (Data Table + Pagination)
└── AuditLogExport.vue (Export Buttons)
```

**State Management:**
- 使用 Vue 3 Composition API 的 `ref` 和 `reactive`
- 集中式狀態管理在 AuditApp.vue
- Props drilling 用於元件間通訊

**API Integration:**
- RESTful API 呼叫使用原生 fetch
- 錯誤處理與載入狀態管理
- 支援 URL 參數同步（書籤和重新整理）

**Security & Performance:**
- 基於權限的存取控制
- 高效能分頁和排序（後端處理）
- 防抖搜尋避免過度 API 呼叫
- 記憶體安全的檔案匯出

#### 技術亮點

- **Responsive Design**: 適應不同螢幕尺寸的管理介面
- **Accessibility**: 語意化 HTML 和鍵盤導航支援
- **Performance**: 虛擬滾動和分頁優化大量資料顯示
- **Maintainability**: 模組化元件設計，易於擴展和測試
- **User Experience**: 直觀的篩選和排序體驗，符合管理員使用習慣

#### 後續增強建議

- 📝 **即時更新**：WebSocket 整合用於即時稽核事件更新
- 📝 **進階篩選**：更多篩選條件，如事件嚴重性等級
- 📝 **資料視覺化**：圖表展示稽核事件趨勢和統計
- 📝 **大量資料優化**：虛擬化表格用於數萬筆稽核記錄
- 📝 **稽核事件詳情**：展開式詳細資訊面板

### Phase 7.3: 異常登入管理 UI (Abnormal Login Management UI) ✅

**完成時間：** 2025-11-18

**目標：** 實作異常登入的手動管理介面

#### 實施內容

**LoginHistoryDialog.vue 功能：**
- ✅ 顯示使用者完整登入歷史記錄（時間、IP、User Agent、風險評分、狀態）
- ✅ 異常登入視覺化標記：
  - 橙色警告三角形圖標 (⚠️) 顯示在 IP 地址旁
  - 橙色徽章顯示「異常」狀態
  - 高風險評分顯示為粉紅色徽章
- ✅ 管理員批准功能：
  - 綠色「批准」按鈕
  - 批准前顯示確認對話框（包含 IP 地址）
  - 批准後顯示成功提示
  - 狀態即時更新為「已批准」（綠色徽章）
- ✅ 過濾功能：「僅顯示異常登入」複選框
- ✅ 分頁支援：每頁 10 筆記錄
- ✅ i18n 完整支援（繁體中文/英文）

**API Integration:**
- ✅ GET `/api/admin/users/{userId}/login-history` - 載入登入記錄
- ✅ POST `/api/admin/users/{userId}/login-history/{loginId}/approve` - 批准異常登入
- ✅ 查詢參數支援：`page`, `pageSize`, `showAbnormalOnly`

**Database Schema:**
- ✅ `LoginHistories` table with columns:
  - `IsFlaggedAbnormal` (boolean) - 標記異常登入
  - `IsApprovedByAdmin` (boolean) - 管理員批准狀態
  - `RiskScore` (integer 0-100) - 風險評分

#### E2E 測試結果（Playwright MCP Server）

**測試場景：**
1. ✅ 建立測試數據（10 條正常登入 + 1 條異常登入）
2. ✅ 開啟 LoginHistoryDialog，驗證 UI 顯示
3. ✅ 驗證異常登入視覺標記（橙色徽章 + 警告圖標）
4. ✅ 測試「僅顯示異常登入」過濾功能
5. ✅ 執行批准工作流：
   - 點擊「批准」按鈕
   - 確認對話框顯示 IP 地址
   - 批准後顯示成功提示
   - 狀態更新為「已批准」
6. ✅ 驗證數據庫更新（`IsApprovedByAdmin = true`）

**測試截圖：**
- `login-history-dialog-abnormal.png` - 異常登入顯示（橙色徽章 + 圖標）
- `login-history-dialog-filtered.png` - 過濾後只顯示異常登入
- `login-history-dialog-approved.png` - 批准後狀態變更（綠色徽章）

**測試數據：**
- Test User: testuser@example.com (ID: 019a6167-3f3b-7e6c-b0c9-a31d1a6595a4)
- Normal IP: 192.168.1.100 (10 records, RiskScore: 10-15)
- Abnormal IP: 10.0.0.50 (1 record, RiskScore: 95, IsFlaggedAbnormal: true)

#### Git Commits

```bash
b21a694 - fix(i18n): Fix loginHistory key nesting and add Chinese menu translations
[pending] - feat(ui): Complete Phase 7.3 abnormal login management with E2E testing
```

#### 技術亮點

- **Vue 3 Composition API**: Reactive state management with `ref()` and `computed()`
- **Tailwind CSS**: Utility-first styling with responsive design
- **i18n Integration**: Separate translations for frontend (vue-i18n) and backend (Resources)
- **Visual Indicators**: Combined orange badge + warning icon for abnormal logins
- **Real-time Updates**: UI reflects approval status immediately after API call
- **Pagination**: Efficient handling of large login history datasets
- **Filter Support**: Quick access to abnormal logins only
- **Comprehensive Testing**: E2E testing with Playwright MCP Server

#### 架構說明

**異常登入批准流程：**
1. 系統偵測異常登入（新 IP 地址），設定 `IsFlaggedAbnormal = true`
2. 管理員在 Users 頁面點擊「登入歷史記錄」按鈕
3. LoginHistoryDialog 顯示所有登入記錄，異常登入以橙色標記
4. 管理員點擊「批准」按鈕
5. 確認對話框顯示 IP 地址，管理員確認
6. API 呼叫更新 `IsApprovedByAdmin = true`
7. UI 即時更新，狀態變為「已批准」（綠色徽章）
8. 該 IP 地址後續登入不再被標記為異常

**UI 狀態邏輯：**
- `IsFlaggedAbnormal = false` → 狀態：「成功」（藍色徽章）
- `IsFlaggedAbnormal = true, IsApprovedByAdmin = false` → 狀態：「異常」（橙色徽章 + ⚠️ 圖標 + 批准按鈕）
- `IsFlaggedAbnormal = true, IsApprovedByAdmin = true` → 狀態：「已批准」（綠色徽章）

**風險評分顏色：**
- 0-30: 綠色（低風險）
- 31-70: 黃色（中風險）
- 71-100: 粉紅色（高風險）

---

### Phase 7.4: 即時活動儀表板 (Real-time Activity Dashboard) ✅ 已完成

**完成時間：** 2025-11-20
**實際 token：** ~4500
**實際時間：** 1 天

**功能摘要：**
- ✅ SignalR 即時更新架構 (MonitoringHub)
- ✅ 統一儀表板 UI (移除獨立監控選單，整合至 Dashboard)
- ✅ 活動統計卡片 (活躍工作階段、登入次數、失敗登入、風險評分)
- ✅ 安全指標視覺化 (儀表板與計數器，OpenTelemetry 整合)
- ✅ 即時警報系統 (無活躍警報時顯示)
- ✅ TDD 實作 (IDashboardService, DashboardService, 6 單元測試)
- ✅ API 端點 (/api/admin/monitoring/dashboard/*)
- ✅ 前端整合 (DashboardApp.vue with SignalR)
- ✅ E2E 測試驗證 (Playwright 登入與儀表板載入)

**技術細節：**
- Service Layer: DashboardService (聚合監控資料)
- Real-time: SignalR Hub 推送更新
- UI: Card-based 設計，Chart.js 圖表支援
- Testing: 6 單元測試 (100% 通過)
- Security: IP 白名單授權，管理員角色限制

**驗證結果：**
- ✅ SignalR 連線成功 (WebSocket wss://localhost:7035/monitoringHub)
- ✅ API 端點正常 (stats, alerts, system-metrics)
- ✅ 即時更新運作 (活動統計、安全警報、系統指標)
- ✅ UI 載入正常 (快速統計、活動儀表板、安全指標、即時警報)
- ✅ E2E 測試通過 (登入流程 + 儀表板存取)

### Phase 7.5: 進階安全警報系統 (Advanced Security Alerts)

**目標：** 實作智慧型安全警報機制
**預估 token：** ~2500
**預估時間：** 2 天

**功能範圍：**

- 可配置警報規則
- 多通道通知 (Email, Webhook)
- 警報升級機制
- 警報歷史與分析
- 整合第三方安全工具

**功能模組：**

- AlertRuleEngine
- NotificationService 擴展
- AlertDashboard

---

### Phase 9: Scope Authorization & Client Management (Partial) ✅

**完成時間：** 2025-11-27 (Phase 9.1-9.4)

**功能摘要：**
- **9.1 Required Scopes:** Database support, consent UI enforcement, server-side validation.
- **9.2 Authorization Policies:** `[Authorize(Policy = "RequireScope:name")]` attribute support.
- **9.3 UserInfo Protection:** OIDC UserInfo endpoint protected by scope.
- **9.4 Client Scope UI:** Refactored Admin UI with dual-column layout, search/pagination, and required scope toggle.

**API Endpoints:**
- `GET/PUT /api/admin/clients/{id}/required-scopes`
- `GET /api/admin/scopes?search=...` (Enhanced usage)

**Components:**
- `ClientScopeManager.vue` (New dual-list UI)
- `ClientForm.vue` (Integrated)

**Verification:**
- ✅ UI Build & Backend Build passed.
- ✅ Unit/Integration tests passed for backend logic.

---

## Backlog (功能增強和技術債務)

### 功能增強

#### User Self-Service (Deferred for AD Integration)

- [ ] Implement user self-service password change flow
- [ ] Add password expiration check during login
- [ ] Prompt user to change password if expired
- [ ] Update user account management UI to show policy requirements

#### User Management

- [ ] Bulk user import (CSV)
- [ ] User profile picture upload
- [ ] Advanced user search (by department, role, creation date)
- [ ] User export (CSV/Excel)

#### Session Management

- [x] Display active sessions (device, location, last active)
- [x] Revoke session (logout from specific device)
- [x] Revoke all sessions (logout everywhere)
- [x] **Suspicious login detection and alerts** (configurable IP-based abnormal login detection)
- [x] Admin unblock blocked login attempts (manual override for false positives)
- [x] **BUG: UI does not refresh session list after revoke operations**
- [x] **BUG: Some sessions fail to revoke (authorizations without associated clients)**

#### Audit & Monitoring

- [ ] Advanced audit logging
- [ ] Audit log viewer with filters
- [ ] Export audit logs (CSV/Excel)
- [ ] Real-time activity dashboard
- [ ] Security alerts (failed login attempts, permission changes)
- [ ] **Abnormal login management UI** (view flagged logins, approve/reject suspicious attempts)

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

- [x] Unit test coverage to 80%+ ✅ (Phase 6.1 完成：158 tests, ~85% coverage)
- [ ] E2E tests for all critical user flows (Phase 6.4 待執行)
- [ ] Integration tests for all API endpoints
- [ ] Frontend component unit tests (Vitest)
- [ ] Load testing (Apache JMeter / k6)
- [ ] Security testing (OWASP ZAP)
- [ ] Accessibility testing

### Technical Debt

**程式碼品質：**

- [x] Refactor large controllers into smaller handlers/services (Phase 6 進行中)
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

### ⚠️ 每個新功能必須

1. **遵循 Small Steps Git 策略**
   - API → Tests → UI 分別 commit
   - 每個 endpoint/component 獨立 commit

2. **更新文件**
   - 完成後更新 `PROJECT_STATUS.md`
   - 標記 `PROJECT_STATUS.md` 完成項目
   - 必要時更新 `DEVELOPMENT_GUIDE.md`

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
