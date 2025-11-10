# HybridIdP 專案狀態

## 🎯 簡介

本文件整合了 HybridAuth IdP 專案的已完成功能摘要和待辦事項，提供一個清晰的專案進度概覽。

---

## ✅ 已完成功能

> 本節記錄所有已完成的 Phases，採用摘要格式以節省 token

最後更新：2025-11-04

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
-   樣式系統：Tailwind CSS 3.4.17

**驗證結果：**
-   ✅ Admin 用戶可訪問 /Admin
-   ✅ 非 Admin 用戶被拒絕（403）
-   ✅ 側邊欄導航正常運作
-   ✅ 手機響應式設計正常

### Phase 3.2: Admin Dashboard (Vue.js Rewrite) ✅

**完成時間：** Phase 3.2 完成

**功能摘要：**
-   Dashboard API 實作 (GET /api/admin/dashboard/stats)
-   Vue.js SPA 實作（DashboardApp.vue）
-   統計卡片：Total Clients, Total Scopes, Total Users
-   快速導航卡片：Clients, Scopes 管理連結

**技術實作：**
-   Razor Page: `Pages/Admin/Index.cshtml`
-   Vue SPA: `ClientApp/src/admin/dashboard/`
-   API: `Api/Admin/DashboardController.cs`

**驗證結果：**
-   ✅ 統計數據正確顯示
-   ✅ 導航卡片連結正常
-   ✅ 響應式佈局（1-3 欄位自適應）

### Phase 3.3-3.5: Scope Management ✅

**完成時間：** Phase 3.5 完成

**功能摘要：**
-   Scope CRUD 完整實作
-   Scope claims 管理（多對多關係）
-   分頁、搜尋、篩選功能

**API Endpoints:**
-   GET /api/admin/scopes (分頁列表)
-   GET /api/admin/scopes/{id} (詳細資料)
-   POST /api/admin/scopes (建立)
-   PUT /api/admin/scopes/{id} (更新)
-   DELETE /api/admin/scopes/{id} (刪除)

**UI Features:**
-   Scope 列表（表格顯示，分頁）
-   建立 Scope 表單（Name, DisplayName, Description, Claims）
-   編輯 Scope（包含 Claims 管理）
-   刪除確認

**驗證結果：**
-   ✅ 所有 CRUD 操作正常
-   ✅ Claims 多選功能正常
-   ✅ 驗證規則生效（必填欄位、唯一性）

### Phase 3.6-3.8: Client Management ✅

**完成時間：** Phase 3.8 完成

**功能摘要：**
-   OIDC Client 完整管理
-   Client Type（Public / Confidential）
-   Redirect URIs 管理
-   Permissions 管理（允許的 Scopes）
-   Client Secret 管理

**API Endpoints:**
-   GET /api/admin/clients (列表，包含 redirectUrisCount)
-   GET /api/admin/clients/{id} (詳細資料)
-   POST /api/admin/clients (建立)
-   PUT /api/admin/clients/{id} (更新)
-   DELETE /api/admin/clients/{id} (刪除)

**UI Features:**
-   Client 列表（Type, Redirect URIs 數量）
-   建立 Client 表單（完整欄位）
-   編輯 Client（Redirect URIs array, Permissions multi-select）
-   刪除確認

**驗證結果：**
-   ✅ Public/Confidential Type 正確顯示
-   ✅ Redirect URIs 多行輸入正常
-   ✅ Permissions 多選正常
-   ✅ Client Secret 顯示/隱藏切換正常

### Phase 3.9-3.11: Claim Type Management ✅

**完成時間：** Phase 3.11 完成

**功能摘要：**
-   Custom Claim Types 管理
-   系統預設 Claims vs 自訂 Claims
-   Claim 使用追蹤（顯示哪些 Scopes 使用此 Claim）

**API Endpoints:**
-   GET /api/admin/claims (列表)
-   GET /api/admin/claims/{id} (詳細資料，包含 usedByScopes)
-   POST /api/admin/claims (建立)
-   PUT /api/admin/claims/{id} (更新)
-   DELETE /api/admin/claims/{id} (刪除，檢查使用狀況)

**UI Features:**
-   Claim 列表（系統 Claims 標記為 "System"）
-   建立 Claim 表單
-   編輯 Claim（顯示使用此 Claim 的 Scopes）
-   刪除保護（使用中的 Claims 不可刪除）

**驗證結果：**
-   ✅ 系統 Claims 正確標記
-   ✅ UsedByScopes 正確顯示
-   ✅ 刪除保護機制正常
-   ✅ 驗證規則生效

### Phase 4.4: User Management UI ✅

**完成時間：** 2025-11-04

**功能摘要：**
-   User CRUD 完整實作
-   Role 分配管理
-   User Claims 管理
-   Activate/Deactivate 功能
-   分頁、搜尋、角色篩選

**API Endpoints:**
-   GET /api/admin/users (分頁列表，支援搜尋和角色篩選)
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

### 🎯 Next Up: Phase 5.6 Part 2 - API Resource Scopes

**目標：** 實作 API Resource 管理，將 scopes 分組至不同的 API 資源，並在 access token 中加入 audience claim

#### Part 2: API Resource Scopes

**Backend:**
-   [ ] Create `ApiResource` entity
    -   [ ] Name, DisplayName, Description, BaseUrl
    -   [ ] Associated Scopes collection
-   [ ] API: `GET /api/admin/resources`
-   [ ] API: `POST /api/admin/resources`
-   [ ] API: `PUT /api/admin/resources/{id}`
-   [ ] API: `DELETE /api/admin/resources/{id}`
-   [ ] API: `GET /api/admin/resources/{id}/scopes`
-   [ ] OpenIddict integration (register resources, audience claim)

**Frontend:**
-   [ ] Vue SPA: `ClientApp/src/admin/resources/ResourcesApp.vue`
-   [ ] Create API resources (Company API, Inventory API, etc.)
-   [ ] Assign scopes to resources
-   [ ] Visual grouping in client configuration

**驗證:**
-   [ ] Admin can create API resources
-   [ ] Scopes can be assigned to resources
-   [ ] Client configuration shows scopes grouped by resource
-   [ ] Access tokens include audience claim

#### Part 3: Scope Authorization Policies (Whitelisting)

**Backend:**
-   [ ] Manage `ClientAllowedScopes` (OpenIddict)
-   [ ] Validation: Verify requested scopes against whitelist
-   [ ] Update client APIs to manage allowed scopes

**Frontend:**
-   [ ] Add "Allowed Scopes" multi-select in `ClientForm.vue`
-   [ ] Group scopes by: Identity, API Resources, Custom
-   [ ] Validation: `openid` required for OIDC clients

**驗證:**
-   [ ] Client can only request whitelisted scopes
-   [ ] Authorization denied for non-whitelisted scopes
-   [ ] Scope selection grouped and easy to manage

**預計完成時間：** 3-4 開發 sessions

## Backlog (功能增強和技術債務)

### 功能增強

#### User Self-Service (Deferred for AD Integration)
-   [ ] Implement user self-service password change flow
-   [ ] Add password expiration check during login
-   [ ] Prompt user to change password if expired
-   [ ] Update user account management UI to show policy requirements

#### User Management
-   [ ] Bulk user import (CSV)
-   [ ] User profile picture upload
-   [ ] Advanced user search (by department, role, creation date)
-   [ ] User export (CSV/Excel)

#### Session Management
-   [ ] Display active sessions (device, location, last active)
-   [ ] Revoke session (logout from specific device)
-   [ ] Revoke all sessions (logout everywhere)
-   [ ] Suspicious login detection and alerts

#### Audit & Monitoring
-   [ ] Advanced audit logging
-   [ ] Audit log viewer with filters
-   [ ] Export audit logs (CSV/Excel)
-   [ ] Real-time activity dashboard
-   [ ] Security alerts (failed login attempts, permission changes)

#### UI/UX Improvements
-   [ ] Dark mode support
-   [ ] Customizable admin dashboard
-   [ ] Remember Me 功能改進
-   [ ] Password strength indicator
-   [ ] Keyboard shortcuts
-   [ ] Accessibility improvements (WCAG 2.1 AA compliance)

#### API Improvements
-   [ ] API documentation (Swagger UI 改進)
-   [ ] API versioning
-   [ ] Rate limiting per endpoint
-   [ ] GraphQL support (optional)

### Security Hardening

**檢查清單：**
-   [ ] HTTPS enforcement in production
-   [ ] HSTS headers
-   [ ] Rate limiting (login, API endpoints)
-   [ ] Input validation comprehensive review
-   [ ] SQL injection prevention audit
-   [ ] XSS prevention audit
-   [ ] CSRF protection verification
-   [ ] Dependency vulnerability scanning
-   [ ] Security headers review (X-Frame-Options, X-Content-Type-Options, etc.)

### Performance Optimization

**待優化：**
-   [ ] Database indexing review and optimization
-   [ ] Query optimization (N+1 problem check)
-   [ ] API response caching strategy
-   [ ] Frontend bundle optimization (Vite build analysis)
-   [ ] Image optimization and lazy loading
-   [ ] CDN configuration for static assets
-   [ ] Database connection pooling tuning

### Testing

**測試涵蓋率提升：**
-   [ ] Unit test coverage to 80%+ (currently ~60%)
-   [ ] Integration tests for all API endpoints
-   [ ] Frontend component unit tests (Vitest)
-   [ ] Load testing (Apache JMeter / k6)
-   [ ] Security testing (OWASP ZAP)
-   [ ] Accessibility testing

### Technical Debt

**程式碼品質：**
-   [ ] Refactor large controllers into smaller handlers/services
-   [ ] Code style consistency (ESLint, Prettier)
-   [ ] Dead code removal
-   [ ] Magic number/string extraction to constants
-   [ ] Comprehensive code comments and documentation

**Architecture:**
-   [ ] Event-driven architecture for audit logging
-   [ ] CQRS pattern for complex operations (optional)
-   [ ] Domain events for loosely coupled features

### DevOps & Deployment

**CI/CD Pipeline:**
-   [ ] GitHub Actions workflow for build/test
-   [ ] Automated deployment to staging
-   [ ] Automated deployment to production (with approval)
-   [ ] Automated database migrations
-   [ ] Rollback automation

**Containerization:**
-   [ ] Multi-stage Docker build optimization
-   [ ] Docker Compose for full stack (local development)
-   [ ] Kubernetes deployment manifests (optional)
-   [ ] Helm charts (optional)

**Monitoring & Observability:**
-   [ ] Application Performance Monitoring (APM)
-   [ ] Error tracking (Sentry / Application Insights)
-   [ ] Metrics collection (Prometheus)
-   [ ] Distributed tracing (Jaeger / Zipkin)
-   [ ] Centralized logging (ELK stack / Seq)

**Database:**
-   [ ] Database backup automation
-   [ ] Database restore procedures
-   [ ] Migration rollback strategy
-   [ ] Database replication (read replicas)
-   [ ] Database monitoring and alerting

---

## 注意事項

### ⚠️ 每個新功能必須：

1.  **遵循 Small Steps Git 策略**
    -   API → Tests → UI 分別 commit
    -   每個 endpoint/component 獨立 commit

2.  **更新文件**
    -   完成後更新 `PROJECT_STATUS.md`
    -   標記 `PROJECT_STATUS.md` 完成項目
    -   必要時更新 `DEVELOPMENT_GUIDE.md`

3.  **測試**
    -   Unit tests for services
    -   API tests (Swagger UI 手動測試或 E2E)
    -   E2E tests for critical flows (Playwright MCP)

4.  **Tailwind CSS 設定**
    -   新 Vue SPA 必須建立 `style.css`
    -   `main.js` 必須 `import './style.css'`

5.  **Authorization 檢查**
    -   Razor Page: `[Authorize(Roles = "Admin")]`
    -   API Controller: `[Authorize(Roles = "Admin")]` or Permission-based
