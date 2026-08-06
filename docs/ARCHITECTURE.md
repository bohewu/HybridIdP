# HybridAuth IdP 架構指南

## 🎯 簡介

本文件詳細說明 HybridAuth IdP Admin Portal 的混合架構設計，內容涵蓋架構概覽、設計原則、技術棧詳解、安全考量、開發工作流程、樣式策略、效能考量、遷移策略以及常見問題解答。

---

## 🔐 Session & Refresh Token Lifecycle

本章節定義了與 OpenIddict 內部解耦的 `UserSession` 模型，支援：
- Refresh token 輪轉（單次使用）與安全雜湊儲存。
- 基於客戶端與全域策略的滑動過期擴展。
- 透過追蹤前次 Token 雜湊進行重放攻擊偵測。
- 階層式撤銷：單一會話或整條 Token 鏈撤銷。

---

## 📐 架構概述

HybridAuth IdP Admin Portal 採用**混合架構**，結合伺服器端渲染（SSR）和客戶端互動（SPA）的優勢：

```text
// See docs/examples/architecture_overview_diagram.txt.example
```

---

## 🎯 設計原則

### 1. **責任分離（Separation of Concerns）**

| 層級 | 技術棧 | 負責範圍 | 為何選擇 |
|------|--------|----------|----------|
| **Layout Layer** | Tailwind CSS (CDN/Vite) | 外框結構：Sidebar、Header、Footer | ・高度靈活、組件化<br>・一致的設計語言<br>・現代化 UI/UX |
| **Routing Layer** | ASP.NET Core Razor Pages | URL 路由、權限驗證、頁面渲染 | ・伺服器端安全驗證<br>・每次導航都檢查 `[Authorize]`<br>・無法繞過後端直接訪問 |
| **Content Layer** | Vue.js 3 + Tailwind CSS | 主要內容區域、CRUD 互動 | ・響應式資料綁定<br>・元件化開發<br>・現代化 UI/UX |
| **Data Layer** | ASP.NET Core Web API | RESTful API、業務邏輯 | ・統一的資料存取介面<br>・API 級別的授權驗證 |

### 2. **安全優先（Security-First）**

```csharp
// See docs/examples/architecture_security_razor_page_auth.cs.example
```

**為什麼不用 Vue Router？**
- ❌ 前端路由守衛可被繞過（修改 JS、停用 JS）
- ❌ 初次載入需要額外 API 呼叫驗證身份
- ❌ SEO 不友好，需要額外的 SSR 配置
- ✅ **Razor Pages** 提供伺服器端路由 + 授權，安全可靠

### 3. **漸進增強（Progressive Enhancement）**

```html
// See docs/examples/architecture_progressive_enhancement.html.example
```

**好處**：
-   即使 JavaScript 載入失敗，Layout 結構仍正常顯示
-   搜尋引擎可索引基礎 HTML 結構
-   使用者體驗更佳（快速顯示外框，再載入互動功能）

---

## 🛠️ 技術棧詳解

### Tailwind CSS (Layout & Content Layer)

**用途**：所有 UI 元素的樣式（Layout 外框與 Vue 組件內容）

**載入方式**：Vite 構建（生產環境）/ Vite Dev Server（開發環境）

**優勢**：
-   ✅ **一致性**：全站統一使用 Tailwind CSS
-   ✅ **靈活性**：輕鬆自訂樣式，無框架限制
-   ✅ **高效能**：JIT 編譯僅產生所需的 CSS
-   ✅ **開發體驗**：即時預覽變更，無需刷新

**使用範例**：

```html
// See docs/examples/architecture_bootstrap_sidebar_nav.html.example
```

### Vue.js 3 + Tailwind CSS (Content Layer)

**用途**：主要內容區域的互動式 SPA

**載入方式**：Vite Dev Server（開發環境）/ Vite Build（生產環境）

**開發流程**：

```bash
// See docs/examples/architecture_vue_dev_workflow.bash.example
```

**檔案結構**：

```
// See docs/examples/architecture_vue_file_structure.txt.example
```

**Vite 配置**（MPA - Multi-Page Application）：

```javascript
// See docs/examples/architecture_vite_mpa_config.js.example
```

**Razor Page 整合**：

```html
// See docs/examples/architecture_razor_page_vue_integration.html.example
```

**Vue 組件範例**（Tailwind CSS）：

```vue
// See docs/examples/architecture_vue_component_example.vue.example
```

---

## 🔐 安全架構

### 多層防護（Defense in Depth）

```
// See docs/examples/architecture_defense_in_depth_diagram.txt.example
```

**為什麼需要多層防護？**
-   **Layer 1-2**：防止未授權使用者訪問頁面
-   **Layer 5**：防止直接呼叫 API（即使繞過前端）

### 範例程式碼

**Razor Page (Layer 2)**：

```csharp
// See docs/examples/architecture_security_razor_page_code.cs.example
```

**API Controller (Layer 5)**：

```csharp
// See docs/examples/architecture_security_api_controller_code.cs.example
```

---

## 📦 開發工作流程

### 正確的啟動順序

參考 `DEVELOPMENT_GUIDE.md`：

```powershell
// See docs/examples/architecture_correct_startup_sequence.ps1.example
```

### 開發體驗

**Layout 部分**（整合在 Vite 中）：
-   ✅ Layout 結構與樣式由 Tailwind 管理
-   ✅ 修改 `_AdminLayout.cshtml` 配合 Tailwind class
-   ✅ 受益於 Vite 的快速構建

**Vue.js 部分**（需要 Vite）：
-   ✅ 修改 `.vue` 檔案 → HMR 自動更新（Hot Module Replacement）
-   ✅ Tailwind CSS 即時編譯
-   ✅ 錯誤即時顯示在瀏覽器 console

---

## 🎨 樣式策略

### Styling Strategy (Tailwind CSS)

本專案完全採用 **Tailwind CSS** 作為樣式解決方案，取代了早期的 Bootstrap。

| 使用場景 | 技術選擇 | 原因 |
|----------|----------|------|
| **全站樣式** | Tailwind CSS | 靈活、Utility-first、一致性 |
| **Grid & Flex** | Tailwind CSS | 強大的佈局系統 |
| **互動式 UI** | Tailwind CSS | 快速原型開發、自訂樣式簡單 |

### 範例對比

**Bootstrap 5（Layout）**：

```html
// See docs/examples/architecture_bootstrap_layout_example.html.example
```

**Tailwind CSS（Vue Component）**：

```vue
// See docs/examples/architecture_tailwind_vue_component_example.vue.example
```

---

## 🚀 效能考量

### 為什麼這個架構效能好？

1.  **首次載入快速**：
    -   Bootstrap 5 從 CDN 快取載入（通常 < 50ms）
    -   Layout 立即渲染，使用者看到結構
    -   Vue.js 異步載入，不阻塞頁面顯示

2.  **後續導航高效**：
    -   Bootstrap Layout 已快取，不需重新載入
    -   只需載入對應的 Vue SPA 檔案
    -   Vite HMR 使開發體驗極佳

3.  **生產環境優化**：
    -   Vite build 產生最小化的 JS bundle
    -   Tree-shaking 移除未使用的程式碼
    -   Code splitting 按需載入

### 效能最佳實踐

```javascript
// See docs/examples/architecture_vite_perf_optimization.js.example
```

---

## 🔄 遷移策略

### 從純 Tailwind 遷移到混合架構

**Before（問題）**：

```html
// See docs/examples/architecture_tailwind_migration_before.html.example
```

❌ **問題**：
-   Tailwind 樣式需要 Vite dev server 運行
-   Layout 和 Content 耦合，難以維護
-   無法利用 Bootstrap 的穩定性

**After（解決方案）**：

```html
// See docs/examples/architecture_tailwind_migration_after_layout.html.example
```

```html
// See docs/examples/architecture_tailwind_migration_after_mount_point.html.example
```

```vue
// See docs/examples/architecture_tailwind_migration_after_vue_component.vue.example
```

✅ **優勢**：
-   一致性：全站統一技術棧
-   Content 使用 Vue + Tailwind
-   易於維護

---

## 📚 Vue.js 3 Multi-Page Application (MPA) 結構

本節概述 `ClientApp` (Vue.js) 部分的 MPA 架構，遵循 `Vite.AspNetCore` 庫的官方文檔。

### 1. 目錄結構

`ClientApp` 資料夾是 Vite 專案的根目錄，每個功能區域（例如 `admin`、`account-manage`）都有自己的入口點。

```
// See docs/examples/architecture_mpa_directory_structure.txt.example
```

### 2. 配置

配置分為 `vite.config.js` (用於構建設置) 和 `appsettings.json` (用於伺服器和庫設置)。

#### 2.1. Vite 配置 (`vite.config.js`)

此文件主要用於 Vite 的構建過程。我們在 `build.rollupOptions.input` 中定義 `root` 和 MPA 入口點。

```javascript
// See docs/examples/architecture_mpa_vite_config.js.example
```

#### 2.2. ASP.NET Core 配置 (`appsettings.Development.json`)

運行時行為，例如啟動 Vite 開發伺服器，在此處控制。這避免了在 `Program.cs` 中硬編碼路徑。

```json
// See docs/examples/architecture_mpa_appsettings_config.json.example
```

#### 2.3. 服務註冊 (`Program.cs`)

服務註冊現在簡單得多，因為配置是從 `appsettings.json` 加載的。

```csharp
// See docs/examples/architecture_mpa_program_cs_config.cs.example
```

---

## 3. 在 Razor Pages 中使用

配置完成後，在 Razor 中使用 MPA 入口點是透過 `vite-src` 標籤助手完成的。這是文檔推薦的方法。

### 3.1. 啟用標籤助手 (`_ViewImports.cshtml`)

首先，使標籤助手在所有 Razor 視圖中可用。

```csharp
// See docs/examples/architecture_mpa_viewimports_config.cs.example
```

### 3.2. 使用 `vite-src` 標籤助手

在您的 Razor Page 中，使用帶有 `vite-src` 屬性的 `<script>` 標籤。路徑應相對於 `PackageDirectory` (`ClientApp`)。標籤助手會自動處理在開發和生產環境中生成正確的 URL。

**範例: Admin Page (`/Pages/Admin/Clients/Index.cshtml`)**

```html
// See docs/examples/architecture_mpa_razor_page_usage.html.example
```

---

## 💡 常見問題

### Q1: 為什麼不全部用 Vue.js + Vue Router？

**A**: 安全性和 SEO 考量：
-   ✅ Razor Pages 提供伺服器端路由驗證（無法繞過）
-   ✅ 每次導航都經過 `[Authorize]` 檢查
-   ✅ SEO 友好（搜尋引擎可索引 HTML 結構）
-   ❌ Vue Router 是客戶端路由，可被停用 JS 繞過

### Q2: 為什麼不繼續使用 Bootstrap？

**A**: 現代化與開發體驗：
-   ✅ Tailwind 提供更強大的設計靈活性
-   ✅ 減少 CSS bundle 大小
-   ✅ 全站樣式統一，避免兩套框架的維護成本
-   ✅ 與 Vue.js 元件化開發完美結合

### Q3: 使用 Tailwind 有什麼好處？

**A**: 
-   Utility-first 模式加快開發速度
-   易於實現自訂設計語言
-   編譯後的 CSS 極小

### Q4: 生產環境如何部署？

**A**: 構建流程：
```bash
// See docs/examples/architecture_production_deployment.bash.example
```

### Q5: 如何新增一個 Admin 頁面？

**A**: 4 步驟：

```bash
// See docs/examples/architecture_add_admin_page_workflow.bash.example
```

---

## ✅ 總結

HybridAuth IdP 採用**現代化架構**，結合 Tailwind CSS 和 Vue.js 3 的優勢：

| 優勢 | 說明 |
|------|------|
| 🔐 **安全** | 伺服器端路由 + 授權，無法繞過 |
| 🚀 **效能** | Bootstrap CDN 快取 + Vue.js 按需載入 |
| 🎨 **靈活** | Tailwind 一致性現代 UI |
| 🛠️ **易維護** | 技術棧各司其職 |
| 📱 **響應式** | Tailwind Flex/Grid |
| 🔍 **SEO 友好** | 伺服器端渲染基礎結構 |

這個架構設計經過深思熟慮，兼顧**安全性、效能、開發體驗和可維護性**，是生產環境的最佳實踐。

---

## 🔐 Scope-Based Authorization

### Overview

HybridIdP implements OAuth 2.0/OpenID Connect scope-based authorization with runtime enforcement and consent management. This allows fine-grained access control for protected API endpoints.

### Architecture Components

#### 1. ScopeAuthorizationHandler

**Purpose**: Runtime authorization handler that validates scope claims in access tokens.

**Implementation**: `Infrastructure/Authorization/ScopeAuthorizationHandler.cs`

```csharp
public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        // Check 'scope' claim (space-separated string)
        var scopeClaim = context.User.FindFirst("scope")?.Value;
        if (!string.IsNullOrEmpty(scopeClaim))
        {
            var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (scopes.Contains(requirement.Scope, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // Check 'scp' claims (multiple claim instances)
        var scpClaims = context.User.FindAll("scp").Select(c => c.Value);
        if (scpClaims.Contains(requirement.Scope, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

**Key Features**:
- Supports both `scope` (space-separated) and `scp` (multiple claims) formats
- Case-insensitive scope matching
- Logs authorization failures for debugging

#### 2. ScopeAuthorizationPolicyProvider

**Purpose**: Dynamically creates authorization policies for the `RequireScope:` pattern.

**Implementation**: `Infrastructure/Authorization/ScopeAuthorizationPolicyProvider.cs`

```csharp
public class ScopeAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
    private const string PolicyPrefix = "RequireScope:";

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var scopeName = policyName.Substring(PolicyPrefix.Length);
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new ScopeRequirement(scopeName))
                .Build();
            return policy;
        }

        // Fall back to default provider for other policies
        return await _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}
```

**Key Features**:
- Recognizes `RequireScope:{scopeName}` policy pattern
- Falls back to default provider for non-scope policies
- No need to register policies individually

#### 3. Required Scopes Model

**Purpose**: Store client-specific required scopes that users cannot opt-out of.

**Database Entity**: `Core.Domain/Entities/ClientRequiredScope.cs`

```csharp
public class ClientRequiredScope
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }  // FK to OpenIddictApplications
    public Guid ScopeId { get; set; }   // FK to OpenIddictScopes
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
```

**Service Layer**: `Infrastructure/Services/ClientAllowedScopesService.cs`

```csharp
public interface IClientAllowedScopesService
{
    Task<IReadOnlyList<string>> GetRequiredScopesAsync(Guid clientId);
    Task SetRequiredScopesAsync(Guid clientId, IEnumerable<string> scopeNames);
    Task<bool> IsScopeRequiredAsync(Guid clientId, string scopeName);
}
```

### Usage Patterns

#### Protecting API Endpoints

Apply the `RequireScope:` policy to controllers or actions:

```csharp
[ApiController]
[Route("api/[controller]")]
public class CompanyController : ControllerBase
{
    [Authorize(Policy = "RequireScope:api:company:read")]
    [HttpGet]
    public IActionResult GetCompanyData()
    {
        // Only accessible with api:company:read scope
        return Ok(new { company = "Acme Corp" });
    }
}
```

#### Multiple Scope Requirements

Require multiple scopes by stacking `[Authorize]` attributes:

```csharp
[Authorize(Policy = "RequireScope:api:company:read")]
[Authorize(Policy = "RequireScope:api:admin")]
[HttpGet("sensitive")]
public IActionResult GetSensitiveData()
{
    // Requires BOTH scopes
    return Ok();
}
```

#### Userinfo Endpoint (OIDC Compliance)

The `/connect/userinfo` endpoint requires `openid` scope per OIDC specification:

```csharp
[Authorize(
    AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
    Policy = "RequireScope:openid")]
[HttpGet("~/connect/userinfo")]
public IActionResult Userinfo()
{
    // Returns user claims only if openid scope is present
    return Ok(userinfo);
}
```

### Consent Page Integration

#### Required Scopes Display

Required scopes are shown as disabled checkboxes in the consent page:

**View**: `Web.IdP/Pages/Connect/Authorize.cshtml`

```html
<input 
    class="form-check-input" 
    type="checkbox" 
    name="granted_scopes" 
    value="@scopeInfo.Name" 
    checked
    @if (scopeInfo.IsRequired) { <text>disabled</text> }
/>
@if (scopeInfo.IsRequired)
{
    <span class="badge bg-secondary">Required</span>
}
```

#### Server-Side Validation

The consent POST handler validates that all required scopes are present:

**Page Model**: `Web.IdP/Pages/Connect/Authorize.cshtml.cs`

```csharp
var clientRequiredScopes = await _clientAllowedScopesService.GetRequiredScopesAsync(clientGuid);
var missingRequired = clientRequiredScopes.Except(effectiveScopes).ToList();

if (missingRequired.Any())
{
    await _auditService.LogAsync(new AuditEvent
    {
        EventType = AuditEventType.ConsentTamperingDetected,
        UserId = userId,
        Details = new { clientId, missingRequiredScopes = missingRequired }
    });

    return BadRequest("Required scopes cannot be excluded from consent.");
}
```

### Admin UI Integration

#### Client Scope Manager

The Admin UI provides a ClientScopeManager component for managing allowed and required scopes:

**Component**: `Web.IdP/ClientApp/src/components/ClientScopeManager.vue`

**Features**:
- Two-column layout: Available Scopes | Selected Scopes
- Search/filter functionality
- Toggle required switch for each selected scope
- Server-side validation (required scopes must be in allowed list)

**API Endpoints**:
```
GET  /api/admin/clients/{id}/scopes          → Returns allowed scopes
PUT  /api/admin/clients/{id}/scopes          → Set allowed scopes
GET  /api/admin/clients/{id}/required-scopes → Returns required scopes
PUT  /api/admin/clients/{id}/required-scopes → Set required scopes
```

### Security Considerations

1. **Server-Side Enforcement**: All authorization checks occur on the server; client-side checks are for UX only.

2. **Tampering Detection**: Attempts to remove required scopes during consent are logged as `ConsentTamperingDetected` audit events.

3. **Token Validation**: Access tokens are validated on every request using the `ScopeAuthorizationHandler`.

4. **OIDC Compliance**: The `openid` scope is mandatory for the userinfo endpoint per OIDC specification.

5. **Principle of Least Privilege**: Clients should only request scopes they actually need.

### Testing

**Unit Tests**: `Tests.Infrastructure.UnitTests/Authorization/`
- ScopeAuthorizationHandlerTests (11 tests)
- ScopeAuthorizationPolicyProviderTests (4 tests)

**Integration Tests**: `Tests.Infrastructure.IntegrationTests/ClientRequiredScopeIntegrationTests.cs` (10 tests)

**E2E Tests**: `e2e/tests/feature-auth/`
- consent-required-scopes.spec.ts (5 tests)
- userinfo-scope-enforcement.spec.ts (3 tests)
- scope-authorization-flow.spec.ts (5 tests)

### Related Documentation

- [SCOPE_AUTHORIZATION.md](./SCOPE_AUTHORIZATION.md) - Developer guide for using scope-based authorization
- [phase-9-scope-authorization.md](./archive/phases/phase-9-scope-authorization.md) - Implementation details and verification
- [E2E Testing Guide](../e2e/README.md) - Testing scope authorization flows

---

## 👤 Identity & Person Model (身分與 Person 模型)

### 核心概念

#### Person vs ApplicationUser

```
Person (1) ─────┬───→ ApplicationUser (upstream-linked account)
                │         └── durable provider namespace + provider key
                │
                ├───→ ApplicationUser (external federation account)
                │         └── external provider login
                │
                └───→ ApplicationUser (Local Password)
                          └── PasswordHash (in AspNetUsers table)
```

#### 設計原則

1. **Person = 真實身分（Physical Identity）**
   - 代表一個真實的人，儲存身分證件資訊：NationalId, EmployeeId。
   - **一個人只有一個 Person 記錄**。
2. **ApplicationUser = 登入帳號（Authentication Account）**
   - 代表一個登入方式，一個 Person 可以有多個 ApplicationUser。
   - 透過 PersonId 連結到 PersonLock。

### Upstream Authentication Boundary

Current password authentication is Local first and otherwise uses the
configurable LegacyAuth HTTP integration. Direct AD/LDAP is not implemented.

The preferred future upstream credential source is deployment-configured direct
AD/LDAP. A separately configured, standardized, provider-neutral
authentication/profile API adapter is permitted only when a required directory
capability cannot be supplied directly. Provider selection must be explicit for
each attempt: an unavailable, rejected, malformed, timed-out, or ambiguous
selected provider denies the attempt and never falls through to another
credential authority.

The directory remains authoritative for its credentials, enabled/disabled
state, lockout, password expiration and change, and password policy. HybridAuth
IdP remains authoritative for shadow `ApplicationUser` records, the
Person-to-multiple-ApplicationUser relationship, durable linking and JIT
provisioning, local eligibility, MFA, cookies, `UserSession`, OIDC/OAuth
tokens, claims, and consent. It must not add a directory password-history or
other directory password-policy overlay.

Durable linking starts with a namespaced provider identifier and immutable,
provider-scoped key; mutable login names, email addresses, display names, and
directory distinguished names are not durable keys. A stable-person key is
optional and can be used only when the selected provider explicitly declares it
stable, immutable, unique within that provider, and suitable for the configured
matching purpose. Provider-key matching precedes heuristic matching. Email or
stable-person-key linking to an existing local identity requires explicit
provider-specific assurance; unassured data can support isolated-account
provisioning only.

See [Authentication Integration Guide](AUTHENTICATION_INTEGRATION.md) for the
authoritative contract. This boundary is generic OSS guidance and does not
introduce organization-specific source systems, schemas, identifiers,
databases, or policies.

---

## 🚪 SSO Entry Portal (SSO 入口導航)

### 目的
提供統一的應用程式入口，讓使用者可以從一個地方 SSO 到所有獲授權的應用程式。

### 架構組件
1. **HybridAuth IdP**: 負責驗證使用者身份與發放 Token。
2. **SSO Entry Portal**: 獨立的應用程式，負責根據使用者 Role 顯示可用應用清單。
3. **Target Applications**: 驗證來自 IdP 的 Token 並提供功能。

---

## 📊 Monitoring & Background Services (監控與後端服務)

### 概述
`MonitoringBackgroundService` 負責定期從資料庫獲取監控數據（如活動統計、安全警報、系統指標），並透過 SignalR (`MonitoringHub`) 廣播給客戶端。

### 更新頻率
- **活動統計**: 5 秒
- **安全警報**: 10 秒
- **系統指標**: 15 秒

### 監控內容
- **Activity Stats**: 活躍會話、登入成功/失敗次數、風險分數。
- **Security Alerts**: 異常登入行為檢測、暴力破壞攻擊預警。
- **System Metrics**: 整合 Prometheus 指標。

### 即時通道安全邊界

`/monitoringHub` 的 negotiate 與 transport 端點要求既有的
`monitoring.read` 權限，並同時支援管理介面使用的 Identity cookie 與
OpenIddict bearer authentication。通過授權的連線才會加入 `monitoring`
group；Hub 不提供用戶端可呼叫的廣播方法，監控事件只由後端服務透過
`IHubContext<MonitoringHub>` 發布。

