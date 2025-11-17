---
title: "Phase 5: Security Policies, i18n, Consent & API Resources"
owner: HybridIdP Team
last-updated: 2025-11-16
percent-complete: 95
---

# Phase 5: Security Policies、i18n、Consent 與 API Resources

簡短摘要：Phase 5 包含本地化 Identity 錯誤、動態密碼策略 TDD、Security Policy API/UI、Consent Screen Customization、ApiResource 管理與 Scope Authorization 等，多數已完成，少數 Part2/Part3 為待辦。

- 已完成要點：LocalizedIdentityErrorDescriber、DynamicPasswordValidator (TDD)、SecurityPolicy API/UI、Consent customization (Part1)、ApiResource CRUD、Scope whitelisting（後端與前端）
- 待辦：Consent Part2 (多語系資源表整合)、Scope Authorization 完整 UI 連動（部分）
- 相關檔案：`Core.Application/DTOs/SecurityPolicyDto.cs`, `ClientApp/src/admin/security/`, `Pages/Admin/Resources.cshtml`

詳情：

### Phase 5.1: Internationalized Identity Errors ✅

**完成時間：** 2025-11-09

**功能摘要：**

- 實作多語言化的 ASP.NET Core Identity 錯誤訊息。
- 建立 `SharedResource.resx` (英文) 和 `SharedResource.zh-TW.resx` (繁體中文) 資源檔。
- 建立自訂 `LocalizedIdentityErrorDescriber` 類別，用於從資源檔中獲取翻譯後的錯誤訊息。
- 在 `Web.IdP/Program.cs` 中配置應用程式的本地化服務，並將 `LocalizedIdentityErrorDescriber` 註冊到 Identity 服務中。
- 支援根據瀏覽器 `Accept-Language` 標頭動態切換語言。

**技術實作：**

- `Web.IdP/Resources/SharedResource.resx`
- `Web.IdP/Resources/SharedResource.zh-TW.resx`
- `Infrastructure/Identity/LocalizedIdentityErrorDescriber.cs`
- `Web.IdP/Program.cs` (配置 `AddLocalization`, `Configure<RequestLocalizationOptions>`, `AddErrorDescriber`)
- `Infrastructure/Infrastructure.csproj` (新增 `Microsoft.Extensions.Localization` 參考)

**驗證結果：**

- ✅ 專案成功編譯，無相關錯誤。
- ✅ `LocalizedIdentityErrorDescriber` 中的 `InvalidUserName` 參數 nullability 警告已解決。
- ✅ 應用程式已準備好根據用戶語言設定顯示本地化的 Identity 錯誤訊息。

### Phase 5.2: TDD for Dynamic Password Validator ✅

**完成時間：** 2025-11-09

**功能摘要：**

- 建立 `DynamicPasswordValidatorTests.cs` 測試檔案，包含針對密碼策略驗證的單元測試。
- 測試涵蓋了最小長度、非英數字元、數字、小寫字母、大寫字母等基本複雜度要求。
- 建立 `Infrastructure/Identity/DynamicPasswordValidator.cs` 類別的骨架，使其能夠編譯並被測試專案引用。
- 驗證所有新撰寫的測試在 `DynamicPasswordValidator` 尚未實作實際驗證邏輯時，均按預期失敗（TDD 的 Red 階段）。

**技術實作：**

- `Tests.Application.UnitTests/DynamicPasswordValidatorTests.cs` (包含多個測試案例)
- `Infrastructure/Identity/DynamicPasswordValidator.cs` (初始骨架，暫時返回 `IdentityResult.Success`)

**驗證結果：**

- ✅ `DynamicPasswordValidatorTests` 中的所有測試均已編譯成功。
- ✅ 所有測試均按預期失敗，確認了 TDD 的 Red 階段已達成。
- ⚠️ 注意：`SettingsServiceTests` 中存在與本任務無關的測試失敗，將在後續處理。

### Phase 5.4: API & UI for Security Policies ✅

**完成時間：** 2025-11-09

**功能摘要：**

- 實作了 `SecurityPolicyDto`，用於在前端和後端之間傳輸安全策略數據，並包含數據驗證屬性。
- 擴展了 `ISecurityPolicyService` 介面和 `SecurityPolicyService` 實作，新增 `UpdatePolicyAsync` 方法，用於更新安全策略。`SecurityPolicyService` 現在能夠從 `SecurityPolicyDto` 更新現有策略，並在更新後使快取失效。
- 創建了 `SecurityPolicyController`，提供了 `GET /api/admin/security/policies` 端點用於獲取當前安全策略，以及 `PUT /api/admin/security/policies` 端點用於更新安全策略。
- API 端點受到 `settings.read` 和 `settings.update` 權限的保護。
- 實作了 Vue SPA (`ClientApp/src/admin/security/SecurityApp.vue`)，提供管理員介面來管理安全策略。
- UI 包含密碼要求、密碼歷史、密碼過期和帳戶鎖定等策略編輯區塊。
- UI 提供實時驗證反饋，並支援保存和應用策略。

**技術實作：**

- `Core.Application/DTOs/SecurityPolicyDto.cs`
- `Core.Application/ISecurityPolicyService.cs` (新增 `UpdatePolicyAsync` 方法)
- `Infrastructure/Services/SecurityPolicyService.cs` (實作 `UpdatePolicyAsync` 方法，包含日誌和快取失效)
- `Web.IdP/Api/Admin/SecurityPolicyController.cs` (GET 和 PUT 端點)
- `Core.Application/IApplicationDbContext.cs` (新增 `DbSet<SecurityPolicy> SecurityPolicies { get; }` 以解決編譯錯誤)
- `ClientApp/src/admin/security/SecurityApp.vue` (Vue SPA for Security Policy Editor)
- `Pages/Admin/Security.cshtml` (Razor Page for mounting Vue SPA)

**驗證結果：**

- ✅ 後端專案成功編譯，無錯誤。
- ✅ API 端點已準備就緒，可供前端 UI 調用。
- ✅ 管理員可以透過 UI 查看和更新安全策略。
- ✅ 策略變更會立即生效，並在 UI 中提供驗證反饋。

### Phase 5.5: Integrate Policy System ✅

**完成時間：** 2025-11-09

**功能摘要：**

- 成功將 `DynamicPasswordValidator<ApplicationUser>` 註冊到 ASP.NET Core Identity 的服務容器中，確保密碼驗證流程能夠使用動態策略。
- 由於未來與 Active Directory 整合的規劃，使用者自助密碼變更、帳號管理顯示策略要求以及密碼過期檢查等相關任務已暫時移至待辦事項 (Backlog) 區塊。

**技術實作：**

- `Web.IdP/Program.cs` (註冊 `DynamicPasswordValidator<ApplicationUser>`)

**驗證結果：**

- ✅ `DynamicPasswordValidator` 已正確註冊並可被 Identity 系統使用。
- ✅ 專案編譯成功，無相關錯誤。

### Phase 5.5a: Settings Key/Value Store & Dynamic Branding ✅

**完成時間：** 2025-11-09

**功能摘要：**

- 建立通用的設定服務與品牌動態化，為後續 Email/Security 設定鋪路。
- DB：新增 `Settings` entity 與 migration（Key 唯一、UpdatedUtc）
- Service：`ISettingsService` + `SettingsService`（MemoryCache、快取失效）
- Branding：讀取順序 DB > appsettings > 內建預設
- API：Admin 設定端點（讀取/更新/快取失效）
- UI：Admin Settings（先做 Branding，Email/Security 之後）
- Tests：E2E via Playwright MCP - Settings CRUD, cache invalidation, branding display

**驗證結果：**

- ✅ Settings Key/Value Store with dynamic branding fully working, tested end-to-end.

### Phase 6.1: Service Layer Unit Tests ✅

**完成時間：** 2025-11-12

**目標：** 提升服務層單元測試覆蓋率至 80%+，確保核心業務邏輯的穩定性與可維護性

**功能摘要：**

- 為所有核心服務補充完整單元測試，涵蓋正常流程與邊界情況
- 採用批次測試策略（一次補完一個服務的所有測試 → 運行 → 單次提交）
- 使用 Moq 框架模擬依賴，xUnit 作為測試框架
- 針對 EF Core 查詢，實作同步/異步兼容的解決方案

**測試涵蓋範圍：**

- **ClientService** (41 tests): 列表查詢（排序/分頁/搜尋）、CRUD 驗證（類型推斷、URI 過濾、權限預設）、密鑰重生
- **ScopeService** (24 tests): 列表/搜尋/排序/分頁、建立（重複檢查、明確資源）、更新（資源替換、部分 consent 欄位）、刪除（使用中檢查、例外處理）
- **ApiResourceService** (23 tests): 完整 CRUD、scope 關聯、cascade delete
- **UserManagementService** (14 tests): 列表/過濾/搜尋、角色指派、稽核欄位、最後登入時間
- **RoleManagementService** (14 tests): 權限驗證、系統角色保護、使用者計數
- **SettingsService** (14 tests): 型別轉換、快取機制、前綴搜尋
- **ClientAllowedScopesService** (12 tests): scope 驗證與權限管理
- **LoginService** (6 tests): 驗證流程、帳戶鎖定、legacy auth
- **JitProvisioningService** (2 tests): 使用者自動建立與更新
- **DynamicPasswordValidator** (8 tests): 密碼強度驗證

**技術實作：**

- `Tests.Application.UnitTests/ClientServiceTests.cs` (41 tests)
- `Tests.Application.UnitTests/ScopeServiceTests.cs` (24 tests)
- `Tests.Application.UnitTests/UserManagementTests.cs` (14 tests)
- `Tests.Application.UnitTests/RoleManagementServiceTests.cs` (14 tests)
- `Tests.Application.UnitTests/SettingsServiceTests.cs` (14 tests)
- `Tests.Application.UnitTests/ApiResourceServiceTests.cs` (23 tests)
- `Tests.Application.UnitTests/ClientAllowedScopesServiceTests.cs` (12 tests)
- `Tests.Application.UnitTests/LoginServiceTests.cs` (6 tests)
- `Tests.Application.UnitTests/JitProvisioningServiceTests.cs` (2 tests)
- `Tests.Application.UnitTests/DynamicPasswordValidatorTests.cs` (8 tests)
- `Infrastructure/Services/UserManagementService.cs` (重構為同步查詢以支援測試)

**驗證結果：**

- ✅ **158 tests 全部通過** (100% passing rate)
- ✅ **測試覆蓋率：~85%** (已達標！)
- ✅ 所有核心服務層邏輯均有完整測試保護
- ✅ 測試執行時間：< 3 秒（高效快速）
- ✅ CI/CD ready：測試可在任何環境獨立運行

### Phase 5.6 Part 1: Consent Screen Customization ✅

**完成時間：** 2025-11-10

**目標：** 提供豐富的同意畫面自訂功能，讓管理員可以為每個 scope 定義友善的顯示名稱、說明、圖示、類別和必要性標記

#### 實施內容

**Database Schema:**

- ✅ 建立 `ScopeExtension` 表格，包含以下欄位：
 	- `ConsentDisplayName` (nvarchar(200), nullable) - 同意畫面顯示名稱
 	- `ConsentDescription` (nvarchar(500), nullable) - 權限說明
 	- `IconUrl` (nvarchar(200), nullable) - 圖示 URL 或 CSS 類別 (如 "bi bi-shield-check")
 	- `IsRequired` (bool, default false) - 必要 scope，使用者無法取消勾選
 	- `DisplayOrder` (int, default 0) - 顯示順序（數字越小越前面）
 	- `Category` (nvarchar(100), nullable) - 類別分組 (如 "個人資料", "API 存取")
 	- `ScopeId` (Guid, FK) - 關聯到 OpenIddict Scopes，具唯一索引
- ✅ 建立 `Resource` 表格（預備未來 i18n 支援）
 	- Composite unique key on (Key, Culture)
- ✅ EF Core Migration: `20251110105526_AddScopeExtensionAndResourceTables`

**Backend API:**

- ✅ 擴展 `ScopeDtos.cs` (ScopeSummary, CreateScopeRequest, UpdateScopeRequest)
 	- 新增 6 個 consent 相關屬性（全部 nullable）
- ✅ 更新 `ScopesController.cs` 4 個端點：
 	- `GetScopes`: 使用 `ToDictionaryAsync` 高效 join ScopeExtensions
 	- `Create`: 若提供 consent 欄位則建立 ScopeExtension
 	- `Update`: 更新或建立 ScopeExtension（nullable 欄位處理）
 	- `Delete`: 級聯刪除關聯的 ScopeExtension

**Frontend (Admin UI):**

- ✅ 增強 `ScopeForm.vue` 新增「Consent Screen Customization」區塊
 	- 6 個輸入欄位：ConsentDisplayName, ConsentDescription, IconUrl, Category (select), DisplayOrder (number), IsRequired (checkbox)
- ✅ 完整 i18n 支援（16 個翻譯 keys，支援 en-US 和 zh-TW）
 	- 翻譯涵蓋：section title/help、所有欄位 label/placeholder/help、類別選項
- ✅ 表單驗證與 payload 構建（null fallback 處理）

**Frontend (User-Facing Consent Screen):**

- ✅ 重構 `Authorize.cshtml.cs` PageModel：
 	- 新增 `ScopeInfo` nested class（8 個屬性）
 	- 實作 `LoadScopeInfosAsync` 方法：join OpenIddict scopes 與 ScopeExtensions，按 DisplayOrder 和 Name 排序
- ✅ 完全重寫 `Authorize.cshtml` Razor view：
 	- Category 分組顯示（使用 LINQ `.GroupBy()`）
 	- 顯示 category 標題（當有多個類別時）
 	- Bootstrap Icons 或自訂圖示渲染（fallback to standard icons）
 	- ConsentDisplayName 或 DisplayName 顯示
 	- IsRequired scope 顯示黃色 "Required" 徽章
 	- ConsentDescription 以小字灰色文字顯示在下方

#### E2E 驗證結果（Playwright MCP）

**測試場景：** 完整 consent customization 流程

1. ✅ 管理員登入 Admin Portal
2. ✅ 建立測試 scope "test_consent" with 完整 consent fields：
 - ConsentDisplayName: "Access Your Test Data"
 - ConsentDescription: "This allows the application to read your test data for E2E testing purposes"
 - IconUrl: "bi bi-shield-check"
 - Category: "個人資料" (Profile)
 - DisplayOrder: 10
 - IsRequired: true (勾選)
3. ✅ 編輯 scope 驗證資料持久化：所有欄位正確載入和顯示
4. ✅ 觸發 OIDC 授權流程（手動構建 authorize URL with test_consent scope）
5. ✅ 驗證 consent screen 顯示：
 - ✅ Category 分組：顯示 "General" 和 "Profile" 兩個群組
 - ✅ Custom icon：shield icon (bi bi-shield-check) 正確渲染
 - ✅ Custom display name："Access Your Test Data" 顯示
 - ✅ Required badge：黃色 "Required" 徽章顯示在 scope 旁
 - ✅ Custom description：說明文字以灰色小字顯示在下方
 - ✅ Display order：test_consent scope 顯示在 Profile 群組中

**截圖證據：**

- Before: `consent-screen-before-customization.png` - 舊版簡單列表
- After: `consent-screen-with-customization.png` - 新版分類、圖示、說明、徽章完整顯示

**Git Commits（Small Steps 策略）**

```bash
feat(db): Add ScopeExtension and Resource tables for consent customization
feat(api): Extend Scope DTOs with 6 consent customization fields
feat(api): Update ScopesController CRUD to handle ScopeExtension
feat(ui): Add Consent Screen Customization section to ScopeForm with i18n
feat(ui): Refactor user consent screen with grouping, icons, descriptions
```

**技術亮點**

- **Efficient DB Query**: `ToDictionaryAsync` 避免 N+1 query 問題
- **Nullable Design**: 所有 consent 欄位為 optional，向後相容既有 scopes
- **i18n Ready**: Resource table 已準備好支援未來多語系 consent text
- **Bootstrap Icons**: 支援 CSS class (如 "bi bi-envelope") 或 image URL
- **Category Grouping**: LINQ `.GroupBy()` 動態分組，可擴展至任意類別
- **Required Badge**: 視覺化標記必要 scope，提升使用者理解

**已知限制與未來增強**

- ⚠️ 刪除有 client 使用的 scope 會失敗（400 error）- 需改善錯誤訊息
- 📝 Resource table 尚未使用（預留給 Part 2 多語系 i18n）
- 📝 Consent screen 未實作「取消勾選必要 scope」的 UI 禁用邏輯
- 📝 Icon preview 功能尚未實作（admin 端只有文字輸入）

**後續計劃**

**Phase 5.6 Part 2: API Resource Scopes**（待實作）

- API Resource 實體與管理介面
- Scope 分配到 API Resources
- Access token audience claim

**Phase 5.6 Part 3: Scope Authorization Policies**（待實作）

- Client 允許的 scopes 白名單管理
- 授權請求驗證與拒絕邏輯

---

### Phase 5.6 Part 2: API Resource Scopes ✅

**完成時間：** 2025-11-11

**目標：** 實作 API Resource 管理，將 scopes 分組至不同的 API 資源，組織和管理 OAuth2 授權範圍

#### 實施內容

**Database Schema:**

- ✅ 建立 `ApiResource` entity 與 migration
 	- Id, Name (unique), DisplayName, Description, BaseUrl
 	- CreatedAt, UpdatedAt timestamps
 	- Scopes collection (One-to-Many)
- ✅ 建立 `ApiResourceScope` entity（Join table）
 	- ApiResourceId (FK), ScopeId (FK to OpenIddict)
 	- Many-to-Many relationship
- ✅ EF Core Migration: `20251111113128_AddApiResourceAndApiResourceScopeTables`
 	- Unique index on ApiResource.Name
 	- Cascade delete configured

**Backend API:**

- ✅ DTOs (`Core.Application/DTOs/ApiResourceDtos.cs`):
 	- `ApiResourceSummary` (list view with ScopeCount)
 	- `ApiResourceDetail` (with Scopes array)
 	- `ResourceScopeInfo` (ScopeId, Name, DisplayName)
 	- `CreateApiResourceRequest` ([Required] Name, validation attributes)
 	- `UpdateApiResourceRequest` (nullable fields)
- ✅ Service Layer (`Infrastructure/Services/ApiResourceService.cs`):
 	- `IApiResourceService` interface with 6 methods
 	- `ApiResourceService` implementation with:
  		- Pagination & sorting (name/displayName)
  		- Search filtering
  		- Scope management (add/remove)
  		- Duplicate name validation
  		- Cascade delete with scope cleanup
  		- Comprehensive logging
- ✅ Thin Controller (`Web.IdP/Api/ApiResourcesController.cs`):
 	- 6 endpoints with `[HasPermission(Permissions.Scopes.*)]`
 	- GET /api/admin/resources (list with pagination)
 	- GET /api/admin/resources/{id} (detail with scopes)
 	- POST /api/admin/resources (create, returns 201)
 	- PUT /api/admin/resources/{id} (update)
 	- DELETE /api/admin/resources/{id} (delete)
 	- GET /api/admin/resources/{id}/scopes (scopes only)
- ✅ Service registration in `Program.cs`

**Frontend (Admin UI):**

- ✅ Vue SPA (`ClientApp/src/admin/resources/`):
 	- `ResourcesApp.vue` (269 lines) - Main app with CRUD handlers
 	- `components/ResourceList.vue` - Table with formatting
 	- `components/ResourceForm.vue` - Modal form with scope multi-select
 	- `main.js` - Vue 3 app initialization
 	- `style.css` - Tailwind CSS imports
- ✅ Razor Page (`Pages/Admin/Resources.cshtml`):
 	- `[Authorize(Policy = Permissions.Scopes.Read)]`
 	- Mounts Vue SPA at `#resources-app`
- ✅ Navigation Update (`_AdminLayout.cshtml`):
 	- Added "Resources" menu item in OIDC Management section
- ✅ i18n Support:
 	- Frontend translations in `ClientApp/src/i18n/locales/en-US.json`
 	- Chinese translations in `zh-TW.json`
 	- 50+ translation keys for resources section
 	- Backend translations in `Web.IdP/Resources/*.resx`

**Unit Tests:**

- ✅ Comprehensive test suite (`Tests.Application.UnitTests/ApiResourceServiceTests.cs`):
 	- 19 unit tests covering all service methods
 	- In-memory database provider (EF Core)
 	- Moq for ApplicationDbContext
 	- Test coverage:
  		- GetResourcesAsync: All/Filter/Sort/Pagination (4 tests)
  		- GetResourceByIdAsync: Found/NotFound/WithScopes (3 tests)
  		- CreateResourceAsync: Success/Duplicate/WithScopes (3 tests)
  		- UpdateResourceAsync: Success/NotFound/UpdateScopes/RemoveScopes (4 tests)
  		- DeleteResourceAsync: Success/NotFound/CascadeDeleteScopes (3 tests)
  		- GetResourceScopesAsync: Success/NotFound (2 tests)
 	- ✅ All 19 tests passing (execution time: 2.45s)

#### E2E 驗證結果

**API Endpoint Tests (Playwright MCP):**

- ✅ GET /api/admin/resources - 200 OK, returned 2 resources
- ✅ POST /api/admin/resources - 201 Created, resource "test-api" created
- ✅ GET /api/admin/resources/{id} - 200 OK, returned resource with scopes
- ✅ PUT /api/admin/resources/{id} - 200 OK, updated description and scopes
- ✅ DELETE /api/admin/resources/{id} - 200 OK, resource deleted
- ✅ GET /api/admin/resources/{id}/scopes - 200 OK, returned scope list
- ✅ Unauthorized test - 401 when token missing

**UI Tests (Playwright MCP):**

1. ✅ **CREATE Test:**
 - Logged in as <admin@hybridauth.local>
 - Navigated to /Admin/Resources
 - Clicked "建立新資源" button
 - Filled form: name="payment-api", displayName="Payment API"
 - Description: "API for payment processing and transactions"
 - BaseUrl: "<https://api.payment.example.com>"
 - Selected scopes: email ✓, openid ✓
 - Submitted → Resource created successfully
 - List shows 2 resources (payment-api, test-api)

2. ✅ **READ Test:**
 - List displays resources with proper formatting
 - Scope count badges: "2 個範圍" displayed correctly
 - Clickable base URL shown
 - Last updated timestamp formatted in Chinese locale

3. ✅ **UPDATE Test:**
 - Clicked "編輯" button for payment-api
 - Modal loaded with existing data
 - Added "profile" scope (3 scopes total)
 - Updated description
 - Saved → Success message displayed
 - List refreshed showing "3 個範圍"
 - Timestamp updated to reflect change

4. ✅ **DELETE Test:**
 - Clicked "刪除" button for test-api
 - Confirmation dialog: "您確定要刪除此 API 資源嗎？所有範圍關聯都將被移除。"
 - Accepted → Resource deleted
 - List refreshed showing only payment-api
 - Pagination updated: "顯示第 1 至 1 項結果，共 1 項"

5. ✅ **i18n Validation:**
 - All labels properly translated in Chinese
 - Page title: "API 資源管理"
 - Buttons: "建立新資源", "編輯", "刪除"
 - Form labels and placeholders all in Chinese
 - Validation messages in Chinese

**Total Commits:** 10 (following small step strategy)

#### 技術亮點

- **Service-Repository Pattern**: Thin controller delegates all logic to service layer
- **Pagination & Sorting**: Efficient database queries with LINQ
- **Scope Management**: Many-to-Many relationship with join entity pattern
- **Cascade Delete**: Automatically removes ApiResourceScope entries
- **Duplicate Prevention**: Unique constraint and validation on Name field
- **Comprehensive Testing**: 19 unit tests + 7 API endpoint tests + full UI E2E testing
- **i18n Support**: Separate frontend (vue-i18n) and backend (Resources) translations
- **Authorization**: Permission-based access control (Permissions.Scopes.*)
- **Vue 3 Composition API**: Modern reactive patterns with `<script setup>`
- **Tailwind CSS**: Utility-first styling with consistent design system

#### 架構說明

**API Resources 用途:**
API Resources 用於組織相關的 scopes，將它們歸類到特定的 API 服務中。例如：

- **Payment API** (payment-api): payment:read, payment:write, payment:refund
- **User API** (user-api): user.profile:read, user.profile:update

**OAuth2 驗證流程:**

1. Client 向 IdP 請求 token，指定需要的 scopes
2. IdP 發行 token 時，在 JWT 的 `aud` (audience) claim 中包含相關的 API Resource names
3. Client 使用 token 呼叫 API
4. API Server 驗證 token 的 `aud` claim 是否包含自己的 resource name
5. 若 `aud` 不符，拒絕請求（403 Forbidden）

**Token 範例:**

```json
{
  "aud": ["payment-api", "user-api"],
  "scope": "payment:read user.profile:read",
  "client_id": "mobile-app"
}
```

**關鍵欄位:**

- **Name**: 唯一識別符，用於 JWT `aud` claim
- **BaseUrl**: API 的基礎 URL（僅用於文件說明，不參與驗證）
- **Scopes**: 與此 resource 關聯的權限列表

**已知限制與未來增強**

- ⚠️ 目前僅實作 CRUD 管理，尚未整合至 OpenIddict token 發行流程
- 📝 BaseUrl 欄位僅供文件參考，實際驗證使用 JWT `aud` claim
- 📝 未實作 Client 選擇 API Resources 的 UI（需在 Phase 5.6 Part 3 實作）
- 📝 Access token 中的 `aud` claim 需額外配置 OpenIddict

---

### Phase 5.6 Part 3: Scope Authorization Policies (Whitelisting) - Backend ✅

**完成時間：** 2025-11-11

**目標：** 實作 Client 允許的 scopes 白名單管理，防止未授權的 scope 請求

#### 實施內容（後端）

**Backend Service & API:**

- ✅ Service Interface (`Core.Application/IClientAllowedScopesService.cs`):
 	- `GetAllowedScopesAsync(Guid clientId)` - 取得允許的 scopes
 	- `SetAllowedScopesAsync(Guid clientId, IEnumerable<string> scopes)` - 設定允許的 scopes
 	- `IsScopeAllowedAsync(Guid clientId, string scope)` - 檢查單一 scope 是否允許
 	- `ValidateRequestedScopesAsync(Guid clientId, IEnumerable<string> requestedScopes)` - 驗證並過濾請求的 scopes
- ✅ Service Implementation (`Infrastructure/Services/ClientAllowedScopesService.cs`):
 	- 使用 `IOpenIddictApplicationManager` 管理 client permissions
 	- 過濾 `scp:` prefix 的 permissions（OpenIddict scope 格式）
 	- 更新時保留非 scope permissions（endpoints, grant types）
 	- Client 不存在時拋出 `InvalidOperationException`
- ✅ Thin Controller (`Web.IdP/Api/ClientsController.cs`):
 	- GET `/api/admin/clients/{id}/scopes` - 回傳 `{ scopes: string[] }`
 	- PUT `/api/admin/clients/{id}/scopes` - 請求 body: `{ scopes: string[] }`
 	- POST `/api/admin/clients/{id}/scopes/validate` - 請求 body: `{ requestedScopes: string[] }`，回傳 `{ allowedScopes: string[] }`
 	- Authorization: `[HasPermission(DomainPermissions.Clients.*)]`
- ✅ Service registration in `Program.cs` (line 142)

**Unit Tests:**

- ✅ Comprehensive test suite (`Tests.Application.UnitTests/ClientAllowedScopesServiceTests.cs`):
 	- 14 unit tests covering all service methods
 	- Moq for `IOpenIddictApplicationManager`
 	- Test coverage:
  		- GetAllowedScopesAsync: 3 tests (found, not found, no scope permissions)
  		- SetAllowedScopesAsync: 3 tests (success, not found, preserve non-scope)
  		- IsScopeAllowedAsync: 3 tests (allowed, not allowed, client not found)
  		- ValidateRequestedScopesAsync: 5 tests (all allowed, partial, none, not found, empty)
 	- ✅ All 14 tests passing (execution time: 1.1s)

#### E2E 驗證結果（Backend API）

**API Endpoint Tests (Playwright MCP):**

- ✅ GET `/api/admin/clients/{id}/scopes` - 200 OK, returned `["openid", "profile", "email", "roles", "test_consent"]`
- ✅ PUT `/api/admin/clients/{id}/scopes` - 200 OK, updated scopes to `["openid", "profile", "email"]`, persistence verified
- ✅ POST `/api/admin/clients/{id}/scopes/validate` - 200 OK, correctly filtered requested scopes (removed "notallowed")
 	- Request: `["openid", "profile", "notallowed", "email"]`
 	- Response: `["openid", "profile", "email"]`

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

- **OpenIddict Integration**: 直接使用 OpenIddict 的 Permission 系統管理 scopes
- **Permission Prefix**: 使用 `scp:` prefix 區分 scopes 與其他 permissions
- **Preserve Non-Scope Permissions**: 更新 scopes 時自動保留 endpoints 和 grant types
- **Comprehensive Testing**: 14 unit tests + 3 API endpoint E2E tests
- **Service Pattern**: Thin controller 完全委派業務邏輯給 service layer
- **Validation**: 內建 scope 驗證與過濾機制
- **Error Handling**: Client 不存在時明確拋出例外

---

### Phase 5.6 Part 3: Scope Authorization Policies (Whitelisting) - Frontend ✅

**完成時間：** 2025-11-11

**目標：** 在 ClientForm.vue 中實作 Allowed Scopes UI

#### 實施內容（前端）

**Frontend Implementation:**

- ✅ Added "Allowed Scopes" multi-select section in `ClientForm.vue`
- ✅ Fetch available scopes from `/api/admin/scopes` endpoint (take=1000 to get all)
- ✅ Group scopes by category with computed property:
 	- **Identity Scopes**: openid, profile, email, address, phone, offline_access
 	- **API Resource Scopes**: Scopes with `resources` array (detected from scope entity)
 	- **Custom Scopes**: Other uncategorized scopes
- ✅ Integrated API endpoints:
 	- GET `/api/admin/clients/{id}/scopes` - Load existing allowed scopes
 	- PUT `/api/admin/clients/{id}/scopes` - Save allowed scopes
- ✅ i18n translations added (en-US, zh-TW):
 	- `allowedScopes`, `allowedScopesHelp`, `allowedScopesRequired`
 	- `allowedScopesOpenidRequired`, `allowedScopesLoading`, `allowedScopesNone`
 	- `scopeCategories.identity`, `scopeCategories.apiResource`, `scopeCategories.custom`
- ✅ Validation: Zod schema validates `openid` scope is included
- ✅ UI: Checkbox multi-select grouped by category, with scope descriptions

**State Management:**

- Reactive state: `availableScopes`, `scopesLoading`, `scopesError`
- Computed property: `categorizedScopes` for grouping logic
- Form data: Added `allowedScopes` array to `formData`
- Auto-fetch scopes on component mount
- Load client allowed scopes when editing (watch for `props.client`)

**UX Features:**

- Loading indicator while fetching scopes
- Error display if scope loading fails
- Empty state message if no scopes available
- Display scope name, display name, and description
- Field-level validation error display

#### E2E 驗證結果（Frontend UI）

**Playwright MCP Tests (手動執行):**

- ✅ Scope selection UI interaction - Toggled "Roles" checkbox successfully
- ✅ Saving allowed scopes - Saved "Roles" scope, verified persistence on reload
- ✅ Scope validation - Unchecked "openid" scope triggered error: "OIDC 用戶端必須包含 'openid' 範圍"
- ✅ Category grouping display - Three categories displayed correctly:
 	- 身分範圍 (Identity Scopes): Email, OpenID, Profile
 	- API 資源範圍 (API Resource Scopes): Roles
 	- 自訂範圍 (Custom Scopes): Test Consent
- ✅ i18n translations - Switched language, verified English translations:
 	- "Allowed Scopes", "Identity Scopes", "API Resource Scopes", "Custom Scopes"
 	- Help text displayed correctly in both languages

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

- **Category Auto-Detection**: Scopes automatically grouped by identity standards vs API resources
- **Computed Property Pattern**: Efficient reactive grouping with Vue 3 composition API
- **Async Loading**: Non-blocking scope fetch with loading/error states
- **Zod Validation**: Client-side validation ensures `openid` scope requirement
- **i18n Complete**: Full bilingual support (en-US, zh-TW)
- **UX Polish**: Loading indicators, error messages, empty states, help text
- **Persistence**: Seamless load/save via dedicated API endpoints

---

### Phase 5.7: Client Service Refactoring & Secret Management ✅

**完成時間：** 2025-11-11

**目標：** 重構 ClientService 的密碼驗證邏輯，修復單元測試，並進行完整的 E2E 驗證

**（此處略其餘內容，完整紀錄已被拆分至各 phase 檔案與 `PROJECT_PROGRESS.md`）**

---

如需我繼續把 Phase 1–4、6、7 的詳細完整段落也搬入各自 `docs/phase-*.md`，我可以依序繼續；或者我可以先把原始大檔案歸檔到 `docs/archive/PROJECT_STATUS_FULL.md` 再逐步拆分。請指示下一步偏好。
