## Session & Refresh Token Lifecycle (Stub)

This section (work-in-progress) will define the local `UserSession` model decoupled from OpenIddict internals to support:

- Refresh token rotation (one-time use) with secure hashing (no raw storage).
- Sliding expiration extensions governed by per-client and global policies (max absolute lifetime enforced).
- Reuse (replay) detection by tracking previous refresh token hash and marking audit events.
- Cascade revocation: single session or entire chain with audit reason and cache invalidation hooks.
- Audit events: `RefreshTokenRotated`, `RefreshTokenReuseDetected`, `SessionRevoked`, `SlidingExpirationExtended`.
- Integration points: Monitoring dashboards, security anomaly detection, scope/settings cache invalidation.

Upcoming implementation phases will replace placeholder methods in `SessionService` (see `RefreshAsync`, `RevokeChainAsync`) guided by the failing unit tests in `SessionRefreshLifecycleTests`.

# HybridAuth IdP 架構指南

## 🎯 簡介

本文件詳細說明 HybridAuth IdP Admin Portal 的混合架構設計，結合伺服器端渲染（SSR）和客戶端互動（SPA）的優勢。它整合了原有的 `architecture_hybrid_bootstrap_vue.md` 和 `idp_vue_mpa_structure.md`，提供全面的架構概覽、設計原則、技術棧詳解、安全考量、開發工作流程、樣式策略、效能考量、遷移策略以及常見問題解答。

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
| **Layout Layer** | Bootstrap 5 (CDN) | 外框結構：Sidebar、Header、Footer | ✅ 穩定、不依賴構建工具<br>✅ 即使 JS 失敗也能顯示<br>✅ SEO 友好 |
| **Routing Layer** | ASP.NET Core Razor Pages | URL 路由、權限驗證、頁面渲染 | ✅ 伺服器端安全驗證<br>✅ 每次導航都檢查 `[Authorize]`<br>✅ 無法繞過後端直接訪問 |
| **Content Layer** | Vue.js 3 + Tailwind CSS | 主要內容區域、CRUD 互動 | ✅ 響應式資料綁定<br>✅ 元件化開發<br>✅ 現代化 UI/UX |
| **Data Layer** | ASP.NET Core Web API | RESTful API、業務邏輯 | ✅ 統一的資料存取介面<br>✅ API 級別的授權驗證 |

### 2. **安全優先（Security-First）**

```csharp
// See docs/examples/architecture_security_razor_page_auth.cs.example
```

**為什麼不用 Vue Router？**
-   ❌ 前端路由守衛可被繞過（修改 JS、停用 JS）
-   ❌ 初次載入需要額外 API 呼叫驗證身份
-   ❌ SEO 不友好，需要額外的 SSR 配置
-   ✅ **Razor Pages** 提供伺服器端路由 + 授權，安全可靠

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

### Bootstrap 5 (Layout Layer)

**用途**：Admin Layout 外框（`_AdminLayout.cshtml`）

**載入方式**：CDN（Content Delivery Network）

```html
// See docs/examples/architecture_bootstrap_cdn_load.html.example
```

**優勢**：
-   ✅ **無需構建**：直接從 CDN 載入，不依賴 Vite
-   ✅ **快速載入**：CDN 分佈全球，低延遲
-   ✅ **穩定可靠**：成熟的框架，廣泛使用
-   ✅ **瀏覽器快取**：多個網站共用 CDN，快取命中率高

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

**Bootstrap 5 部分**（不需要 Vite）：
-   ✅ Layout 結構立即可見
-   ✅ 修改 `_AdminLayout.cshtml` → 重新整理即可看到變更
-   ✅ 不依賴 Vite dev server

**Vue.js 部分**（需要 Vite）：
-   ✅ 修改 `.vue` 檔案 → HMR 自動更新（Hot Module Replacement）
-   ✅ Tailwind CSS 即時編譯
-   ✅ 錯誤即時顯示在瀏覽器 console

---

## 🎨 樣式策略

### Bootstrap 5 vs Tailwind CSS

| 使用場景 | 技術選擇 | 原因 |
|----------|----------|------|
| **Layout 外框** | Bootstrap 5 | 穩定、不依賴構建、CDN 快取 |
| **Navigation** | Bootstrap 5 | 成熟的導航組件（`.nav`, `.navbar`） |
| **Grid System** | Bootstrap 5 | 響應式網格（`.container`, `.row`, `.col-*`） |
| **Vue 組件內容** | Tailwind CSS | 靈活、Utility-first、現代化 |
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
-   Layout 使用 Bootstrap（穩定、無構建依賴）
-   Content 使用 Vue + Tailwind（靈活、現代化）
-   責任分離，易於維護

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

### Q2: 為什麼 Layout 用 Bootstrap 而不是 Tailwind？

**A**: 穩定性和獨立性：
-   ✅ Bootstrap 從 CDN 載入，不依賴 Vite
-   ✅ 即使 Vite 故障，Layout 仍正常顯示
-   ✅ 瀏覽器快取命中率高（多網站共用 CDN）
-   ✅ 成熟穩定，組件豐富

### Q3: 如何確保 Vite 和 Bootstrap 不衝突？

**A**: 樣式隔離：
-   Bootstrap 只用於 `_AdminLayout.cshtml`（外框）
-   Tailwind 只用於 Vue 組件內部（`.vue` 檔案）
-   兩者不共用 DOM 元素，不會樣式衝突

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

HybridAuth IdP 採用**混合架構**，結合 Bootstrap 5 和 Vue.js 3 的優勢：

| 優勢 | 說明 |
|------|------|
| 🔐 **安全** | 伺服器端路由 + 授權，無法繞過 |
| 🚀 **效能** | Bootstrap CDN 快取 + Vue.js 按需載入 |
| 🎨 **靈活** | Bootstrap 穩定 Layout + Tailwind 現代 UI |
| 🛠️ **易維護** | 責任分離，技術棧各司其職 |
| 📱 **響應式** | Bootstrap Grid + Tailwind Utilities |
| 🔍 **SEO 友好** | 伺服器端渲染基礎結構 |

這個架構設計經過深思熟慮，兼顧**安全性、效能、開發體驗和可維護性**，是生產環境的最佳實踐。
