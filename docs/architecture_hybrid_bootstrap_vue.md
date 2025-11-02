# Hybrid Architecture: Bootstrap 5 + Vue.js 3

## 📐 架構概述

HybridAuth IdP Admin Portal 採用**混合架構**，結合伺服器端渲染（SSR）和客戶端互動（SPA）的優勢：

```
┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core Razor Pages (Server-side Rendering)          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  _AdminLayout.cshtml (Bootstrap 5 from CDN)          │  │
│  │  ├── Sidebar (260px fixed, responsive)               │  │
│  │  ├── Header (Breadcrumbs, User menu)                 │  │
│  │  └── Footer (Copyright, Links)                       │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Admin Pages (Razor Pages with [Authorize])          │  │
│  │  ├── /Admin/Index.cshtml                             │  │
│  │  ├── /Admin/Clients.cshtml                           │  │
│  │  └── /Admin/Scopes.cshtml                            │  │
│  └───────────────────────────────────────────────────────┘  │
│                        ↓                                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Vue.js 3 SPAs (Client-side Interactivity)          │  │
│  │  ├── DashboardApp.vue (Tailwind CSS)                │  │
│  │  ├── ClientsApp.vue (Tailwind CSS)                  │  │
│  │  └── ScopesApp.vue (Tailwind CSS)                   │  │
│  └───────────────────────────────────────────────────────┘  │
│                        ↓                                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  API Controllers (Backend Services)                  │  │
│  │  ├── GET /api/admin/clients                          │  │
│  │  ├── POST /api/admin/clients                         │  │
│  │  └── GET /api/admin/dashboard/stats                  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
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
// 每個 Razor Page 都有伺服器端授權
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class ClientsModel : PageModel
{
    public void OnGet()
    {
        // ✅ 只有通過 [Authorize] 驗證才會執行到這裡
        // ✅ 無法透過前端路由繞過授權檢查
    }
}
```

**為什麼不用 Vue Router？**
- ❌ 前端路由守衛可被繞過（修改 JS、停用 JS）
- ❌ 初次載入需要額外 API 呼叫驗證身份
- ❌ SEO 不友好，需要額外的 SSR 配置
- ✅ **Razor Pages** 提供伺服器端路由 + 授權，安全可靠

### 3. **漸進增強（Progressive Enhancement）**

```html
<!-- 1. 基礎 HTML 由 Razor 渲染（Bootstrap 5） -->
<div class="container-fluid">
  <div class="sidebar">...</div>
  <main class="main-content">
    <!-- 2. Vue.js 掛載點 -->
    <div id="app">
      <!-- 3. 載入中顯示基礎內容 -->
      <p>Loading...</p>
    </div>
  </main>
</div>

<!-- 4. Vue.js 接管並增強互動性 -->
<script type="module" vite-src="~/src/admin/clients/main.js"></script>
```

**好處**：
- 即使 JavaScript 載入失敗，Layout 結構仍正常顯示
- 搜尋引擎可索引基礎 HTML 結構
- 使用者體驗更佳（快速顯示外框，再載入互動功能）

---

## 🛠️ 技術棧詳解

### Bootstrap 5 (Layout Layer)

**用途**：Admin Layout 外框（`_AdminLayout.cshtml`）

**載入方式**：CDN（Content Delivery Network）

```html
<!-- Bootstrap 5 CSS -->
<link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" rel="stylesheet">

<!-- Bootstrap Icons -->
<link href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.1/font/bootstrap-icons.css" rel="stylesheet">

<!-- Bootstrap 5 JS (Optional, for interactive components) -->
<script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/js/bootstrap.bundle.min.js"></script>
```

**優勢**：
- ✅ **無需構建**：直接從 CDN 載入，不依賴 Vite
- ✅ **快速載入**：CDN 分佈全球，低延遲
- ✅ **穩定可靠**：成熟的框架，廣泛使用
- ✅ **瀏覽器快取**：多個網站共用 CDN，快取命中率高

**使用範例**：

```html
<!-- Sidebar Navigation -->
<nav class="sidebar">
  <ul class="nav flex-column">
    <li class="nav-item">
      <a class="nav-link active" href="/Admin">
        <i class="bi bi-speedometer2"></i>
        Dashboard
      </a>
    </li>
    <li class="nav-item">
      <a class="nav-link" href="/Admin/Clients">
        <i class="bi bi-grid"></i>
        Clients
      </a>
    </li>
  </ul>
</nav>
```

### Vue.js 3 + Tailwind CSS (Content Layer)

**用途**：主要內容區域的互動式 SPA

**載入方式**：Vite Dev Server（開發環境）/ Vite Build（生產環境）

**開發流程**：

```bash
# 1. 手動啟動 Vite Dev Server
cd Web.IdP/ClientApp
npm run dev

# 2. Vite 監聽 localhost:5173
# 3. Razor Pages 透過 <script vite-src> 載入 Vue 組件
```

**檔案結構**：

```
ClientApp/src/admin/
├── clients/
│   ├── main.js           # Entry Point
│   └── ClientsApp.vue    # Root Component (Tailwind CSS)
├── scopes/
│   ├── main.js
│   └── ScopesApp.vue
└── dashboard/
    ├── main.js
    └── DashboardApp.vue
```

**Vite 配置**（MPA - Multi-Page Application）：

```javascript
// vite.config.js
export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        'admin-clients': resolve(__dirname, 'src/admin/clients/main.js'),
        'admin-scopes': resolve(__dirname, 'src/admin/scopes/main.js'),
        'admin-dashboard': resolve(__dirname, 'src/admin/dashboard/main.js'),
      }
    }
  }
});
```

**Razor Page 整合**：

```html
@page
@model Web.IdP.Pages.Admin.ClientsModel
@{
    ViewData["Title"] = "Client Management";
    ViewData["Breadcrumb"] = "Clients";
}

<!-- Vue.js 掛載點 -->
<div id="app"></div>

<!-- Vite 載入 Vue SPA -->
<script type="module" vite-src="~/src/admin/clients/main.js"></script>
```

**Vue 組件範例**（Tailwind CSS）：

```vue
<template>
  <div class="max-w-7xl mx-auto">
    <!-- 使用 Tailwind 樣式 -->
    <div class="bg-white rounded-lg shadow-sm p-6">
      <h2 class="text-2xl font-bold text-gray-900 mb-4">Clients</h2>
      <!-- CRUD Interface -->
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';

const clients = ref([]);

const fetchClients = async () => {
  const response = await fetch('/api/admin/clients');
  clients.value = await response.json();
};

onMounted(() => {
  fetchClients();
});
</script>
```

---

## 🔐 安全架構

### 多層防護（Defense in Depth）

```
User Request: https://localhost:7035/Admin/Clients
         ↓
┌────────────────────────────────────────────────┐
│ Layer 1: ASP.NET Core Authentication           │
│ └─ Cookie/JWT validation                       │
└────────────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────────────┐
│ Layer 2: Razor Page Authorization              │
│ └─ [Authorize(Roles = "Admin")]                │
└────────────────────────────────────────────────┘
         ↓ (Authorized)
┌────────────────────────────────────────────────┐
│ Layer 3: Render _AdminLayout + Clients.cshtml │
│ └─ Bootstrap 5 Layout + Vue.js mount point    │
└────────────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────────────┐
│ Layer 4: Vue.js loads and calls API            │
│ └─ fetch('/api/admin/clients')                 │
└────────────────────────────────────────────────┘
         ↓
┌────────────────────────────────────────────────┐
│ Layer 5: API Controller Authorization          │
│ └─ [Authorize(Roles = "Admin")]                │
└────────────────────────────────────────────────┘
```

**為什麼需要多層防護？**
- **Layer 1-2**：防止未授權使用者訪問頁面
- **Layer 5**：防止直接呼叫 API（即使繞過前端）

### 範例程式碼

**Razor Page (Layer 2)**：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Core.Domain.Constants;

namespace Web.IdP.Pages.Admin
{
    [Authorize(Roles = AuthConstants.Roles.Admin)]
    public class ClientsModel : PageModel
    {
        public void OnGet()
        {
            // 只有 Admin 角色才能執行
        }
    }
}
```

**API Controller (Layer 5)**：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Core.Domain.Constants;

namespace Web.IdP.Api
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = AuthConstants.Roles.Admin)]
    public class AdminController : ControllerBase
    {
        [HttpGet("clients")]
        public async Task<IActionResult> GetClients()
        {
            // API 級別的授權驗證
            // 即使前端被繞過，仍會檢查權限
            return Ok(clients);
        }
    }
}
```

---

## 📦 開發工作流程

### 正確的啟動順序

參考 `docs/dev_testing_guide.md`：

```powershell
# 1. 啟動資料庫
docker compose up -d db-service

# 2. 啟動 IdP（終端機 1）
cd Web.IdP
dotnet run --launch-profile https
# ✅ IdP 啟動在 https://localhost:7035
# ✅ Vite 不會自動啟動（AutoRun: false）

# 3. 手動啟動 Vite（終端機 2）
cd Web.IdP\ClientApp
npm run dev
# ✅ Vite 啟動在 http://localhost:5173

# 4. 訪問 Admin Portal
# https://localhost:7035/Admin
```

### 開發體驗

**Bootstrap 5 部分**（不需要 Vite）：
- ✅ Layout 結構立即可見
- ✅ 修改 `_AdminLayout.cshtml` → 重新整理即可看到變更
- ✅ 不依賴 Vite dev server

**Vue.js 部分**（需要 Vite）：
- ✅ 修改 `.vue` 檔案 → HMR 自動更新（Hot Module Replacement）
- ✅ Tailwind CSS 即時編譯
- ✅ 錯誤即時顯示在瀏覽器 console

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
<div class="container-fluid">
  <div class="row">
    <div class="col-md-3">
      <nav class="nav flex-column">
        <a class="nav-link active">Dashboard</a>
      </nav>
    </div>
    <div class="col-md-9">
      <!-- Vue.js mount point -->
    </div>
  </div>
</div>
```

**Tailwind CSS（Vue Component）**：

```vue
<template>
  <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
    <div class="bg-white rounded-lg shadow-sm p-6">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-2xl font-bold text-gray-900">Clients</h2>
        <button class="bg-indigo-600 text-white px-4 py-2 rounded-md hover:bg-indigo-700">
          Add Client
        </button>
      </div>
    </div>
  </div>
</template>
```

---

## 🚀 效能考量

### 為什麼這個架構效能好？

1. **首次載入快速**：
   - Bootstrap 5 從 CDN 快取載入（通常 < 50ms）
   - Layout 立即渲染，使用者看到結構
   - Vue.js 異步載入，不阻塞頁面顯示

2. **後續導航高效**：
   - Bootstrap Layout 已快取，不需重新載入
   - 只需載入對應的 Vue SPA 檔案
   - Vite HMR 使開發體驗極佳

3. **生產環境優化**：
   - Vite build 產生最小化的 JS bundle
   - Tree-shaking 移除未使用的程式碼
   - Code splitting 按需載入

### 效能最佳實踐

```javascript
// vite.config.js - 生產環境優化
export default defineConfig({
  build: {
    minify: 'terser',
    terserOptions: {
      compress: {
        drop_console: true, // 移除 console.log
      }
    },
    rollupOptions: {
      output: {
        manualChunks: {
          'vendor': ['vue'], // Vue 單獨打包
        }
      }
    }
  }
});
```

---

## 🔄 遷移策略

### 從純 Tailwind 遷移到混合架構

**Before（問題）**：

```html
<!-- Index.cshtml - 純 Tailwind -->
<div class="max-w-7xl mx-auto">
  <div class="grid grid-cols-3 gap-6">
    <!-- Tailwind 樣式需要 Vite 才能顯示 -->
  </div>
</div>
```

❌ **問題**：
- Tailwind 樣式需要 Vite dev server 運行
- Layout 和 Content 耦合，難以維護
- 無法利用 Bootstrap 的穩定性

**After（解決方案）**：

```html
<!-- _AdminLayout.cshtml - Bootstrap 5 -->
<div class="container-fluid">
  <div class="sidebar">...</div> <!-- Bootstrap -->
  <main class="main-content">
    @RenderBody() <!-- Vue.js mount point -->
  </main>
</div>
```

```html
<!-- Index.cshtml - Vue mount point -->
<div id="dashboard-app"></div>
<script type="module" vite-src="~/src/admin/dashboard/main.js"></script>
```

```vue
<!-- DashboardApp.vue - Tailwind CSS -->
<template>
  <div class="max-w-7xl mx-auto">
    <div class="grid grid-cols-3 gap-6">
      <!-- Tailwind 樣式由 Vite 處理 -->
    </div>
  </div>
</template>
```

✅ **優勢**：
- Layout 使用 Bootstrap（穩定、無構建依賴）
- Content 使用 Vue + Tailwind（靈活、現代化）
- 責任分離，易於維護

---

## 📚 參考資源

### 官方文件

- **Bootstrap 5**: <https://getbootstrap.com/docs/5.3/>
- **Vue.js 3**: <https://vuejs.org/>
- **Tailwind CSS**: <https://tailwindcss.com/>
- **Vite**: <https://vitejs.dev/>
- **Vite.AspNetCore**: <https://github.com/Eptagone/Vite.AspNetCore>

### 專案文件

- `docs/idp_req_details.md` - Phase 3 完整需求
- `docs/dev_testing_guide.md` - 開發測試指南
- `docs/idp_vue_mpa_structure.md` - Vue.js MPA 配置
- `docs/phase_3.2_dashboard_rewrite_plan.md` - Dashboard 改寫計畫

---

## 💡 常見問題

### Q1: 為什麼不全部用 Vue.js + Vue Router？

**A**: 安全性和 SEO 考量：
- ✅ Razor Pages 提供伺服器端路由驗證（無法繞過）
- ✅ 每次導航都經過 `[Authorize]` 檢查
- ✅ SEO 友好（搜尋引擎可索引 HTML 結構）
- ❌ Vue Router 是客戶端路由，可被停用 JS 繞過

### Q2: 為什麼 Layout 用 Bootstrap 而不是 Tailwind？

**A**: 穩定性和獨立性：
- ✅ Bootstrap 從 CDN 載入，不依賴 Vite
- ✅ 即使 Vite 故障，Layout 仍正常顯示
- ✅ 瀏覽器快取命中率高（多網站共用 CDN）
- ✅ 成熟穩定，組件豐富

### Q3: 如何確保 Vite 和 Bootstrap 不衝突？

**A**: 樣式隔離：
- Bootstrap 只用於 `_AdminLayout.cshtml`（外框）
- Tailwind 只用於 Vue 組件內部（`.vue` 檔案）
- 兩者不共用 DOM 元素，不會樣式衝突

### Q4: 生產環境如何部署？

**A**: 構建流程：
```bash
# 1. 構建 Vue.js 應用
cd Web.IdP/ClientApp
npm run build

# 2. 發佈 ASP.NET Core 應用
cd ..
dotnet publish -c Release

# 3. Vite 構建輸出會自動包含在發佈目錄
# wwwroot/dist/admin-clients.js
# wwwroot/dist/admin-scopes.js
```

### Q5: 如何新增一個 Admin 頁面？

**A**: 4 步驟：

```bash
# 1. 建立 Razor Page
Pages/Admin/MyFeature.cshtml
Pages/Admin/MyFeature.cshtml.cs

# 2. 建立 Vue SPA
ClientApp/src/admin/myfeature/main.js
ClientApp/src/admin/myfeature/MyFeatureApp.vue

# 3. 更新 vite.config.js
input: {
  'admin-myfeature': './src/admin/myfeature/main.js'
}

# 4. 在 Razor Page 中載入
<div id="app"></div>
<script type="module" vite-src="~/src/admin/myfeature/main.js"></script>
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
