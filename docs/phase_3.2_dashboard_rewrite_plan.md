# Phase 3.2: Admin Dashboard Rewrite Plan

## 📋 目標

將目前使用純 Tailwind CSS 的 Dashboard 頁面改寫為 Vue.js SPA，以保持與 Admin Portal 整體架構的一致性。

## 🎯 架構決策

### 目前狀態（問題）

- **檔案**: `Web.IdP/Pages/Admin/Index.cshtml`
- **問題**: 使用 Tailwind CSS 類別（`grid grid-cols-1 md:grid-cols-2`、`bg-white rounded-lg` 等）
- **限制**: 
  - Tailwind CSS 需要 Vite dev server 運行才能正常顯示
  - 與 Admin Layout 的 Bootstrap 5 架構不一致
  - 無法使用 Vue.js 的響應式資料綁定和生命週期管理
  - 統計資料目前是靜態的（`--` 佔位符）

### 目標架構（解決方案）

```
Bootstrap 5 Layout (Razor Pages)
├── _AdminLayout.cshtml (Sidebar + Header + Footer)
└── Index.cshtml (Dashboard 頁面)
    └── Vue.js SPA Mount Point (#dashboard-app)
        └── DashboardApp.vue (Tailwind CSS)
            ├── Stats Cards (API-driven)
            └── Navigation Cards (Quick links)
```

**混合架構**:
- **Bootstrap 5**: 用於 Razor Pages 的主要 layout（不依賴 Vite）
- **Vue.js + Tailwind**: 用於互動式 SPA 組件（由 Vite 構建）

## 📂 需要建立的檔案

### 1. Vue.js Entry Point

**路徑**: `Web.IdP/ClientApp/src/admin/dashboard/main.js`

```javascript
import { createApp } from 'vue';
import DashboardApp from './DashboardApp.vue';
import '../../assets/admin.css'; // Tailwind 樣式

const app = createApp(DashboardApp);
app.mount('#dashboard-app');
```

### 2. Vue.js 主組件

**路徑**: `Web.IdP/ClientApp/src/admin/dashboard/DashboardApp.vue`

```vue
<template>
  <div class="max-w-7xl mx-auto">
    <!-- Loading State -->
    <div v-if="loading" class="flex justify-center items-center py-12">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg p-4 mb-6">
      <p class="text-red-800">{{ error }}</p>
    </div>

    <!-- Dashboard Content -->
    <div v-else>
      <!-- Stats Cards -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
        <div class="bg-white rounded-lg shadow-sm p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm text-gray-600">Total Clients</p>
              <p class="text-3xl font-bold text-indigo-600">{{ stats.clientCount }}</p>
            </div>
            <div class="w-12 h-12 bg-indigo-100 rounded-lg flex items-center justify-center">
              <!-- Icon SVG -->
            </div>
          </div>
        </div>

        <div class="bg-white rounded-lg shadow-sm p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm text-gray-600">Total Scopes</p>
              <p class="text-3xl font-bold text-green-600">{{ stats.scopeCount }}</p>
            </div>
            <div class="w-12 h-12 bg-green-100 rounded-lg flex items-center justify-center">
              <!-- Icon SVG -->
            </div>
          </div>
        </div>

        <div class="bg-white rounded-lg shadow-sm p-6">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm text-gray-600">Total Users</p>
              <p class="text-3xl font-bold text-blue-600">{{ stats.userCount }}</p>
            </div>
            <div class="w-12 h-12 bg-blue-100 rounded-lg flex items-center justify-center">
              <!-- Icon SVG -->
            </div>
          </div>
        </div>
      </div>

      <!-- Navigation Cards -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <a href="/Admin/Clients" 
           class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow p-6 block">
          <div class="flex items-center justify-center w-12 h-12 bg-indigo-100 rounded-lg mb-4">
            <!-- Icon SVG -->
          </div>
          <h2 class="text-xl font-semibold text-gray-900 mb-2">OIDC Clients</h2>
          <p class="text-sm text-gray-600 mb-4">
            Manage OpenID Connect client applications that can authenticate with this IdP.
          </p>
          <span class="inline-flex items-center text-indigo-600 hover:text-indigo-700 font-medium text-sm">
            Manage Clients →
          </span>
        </a>

        <a href="/Admin/Scopes" 
           class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow p-6 block">
          <div class="flex items-center justify-center w-12 h-12 bg-green-100 rounded-lg mb-4">
            <!-- Icon SVG -->
          </div>
          <h2 class="text-xl font-semibold text-gray-900 mb-2">OIDC Scopes</h2>
          <p class="text-sm text-gray-600 mb-4">
            Define scopes that control what information clients can access from user profiles.
          </p>
          <span class="inline-flex items-center text-green-600 hover:text-green-700 font-medium text-sm">
            Manage Scopes →
          </span>
        </a>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';

const loading = ref(true);
const error = ref(null);
const stats = ref({
  clientCount: 0,
  scopeCount: 0,
  userCount: 0
});

const fetchStats = async () => {
  try {
    loading.value = true;
    const response = await fetch('/api/admin/dashboard/stats');
    
    if (!response.ok) {
      throw new Error('Failed to load dashboard statistics');
    }
    
    const data = await response.json();
    stats.value = data;
  } catch (err) {
    error.value = err.message;
    console.error('Dashboard stats error:', err);
  } finally {
    loading.value = false;
  }
};

onMounted(() => {
  fetchStats();
});
</script>
```

### 3. 修改 Razor Page

**路徑**: `Web.IdP/Pages/Admin/Index.cshtml`

```html
@page
@model Web.IdP.Pages.Admin.IndexModel
@{
    ViewData["Title"] = "Dashboard";
    ViewData["Breadcrumb"] = "Dashboard";
}

<!-- Page Header -->
<div class="mb-4">
    <h1 class="h3 mb-0">Admin Dashboard</h1>
    <p class="text-muted mt-2">Welcome to the HybridAuth IdP Administration Interface</p>
</div>

<!-- Vue.js Mount Point -->
<div id="dashboard-app"></div>

@section Scripts {
    <script type="module" vite-src="~/src/admin/dashboard/main.js"></script>
}
```

### 4. 更新 Vite 配置

**路徑**: `Web.IdP/ClientApp/vite.config.js`

```javascript
export default defineConfig({
  // ... existing config
  build: {
    rollupOptions: {
      input: {
        'admin-clients': resolve(__dirname, 'src/admin/clients/main.js'),
        'admin-scopes': resolve(__dirname, 'src/admin/scopes/main.js'),
        'admin-dashboard': resolve(__dirname, 'src/admin/dashboard/main.js'), // 新增
      }
    }
  }
});
```

## 🔌 Backend API

### API Endpoint

**路徑**: `Web.IdP/Api/AdminController.cs`

```csharp
[ApiController]
[Route("api/admin")]
[Authorize(Roles = AuthConstants.Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public AdminController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("dashboard/stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats()
    {
        var clientCount = await _context.Applications.CountAsync();
        var scopeCount = await _context.Scopes.CountAsync();
        var userCount = await _context.Users.CountAsync();

        return Ok(new DashboardStatsDto
        {
            ClientCount = clientCount,
            ScopeCount = scopeCount,
            UserCount = userCount
        });
    }
}

public class DashboardStatsDto
{
    public int ClientCount { get; set; }
    public int ScopeCount { get; set; }
    public int UserCount { get; set; }
}
```

## ✅ 驗證步驟

### 1. 功能測試

- [ ] Dashboard 頁面成功載入 Vue.js SPA
- [ ] API 正確回傳統計資料（clientCount、scopeCount、userCount）
- [ ] Stats Cards 顯示正確的數字（不是 `--` 佔位符）
- [ ] Loading 狀態正常顯示（旋轉圖示）
- [ ] Error 狀態正常處理（顯示錯誤訊息）
- [ ] Navigation Cards 連結正確導向 `/Admin/Clients` 和 `/Admin/Scopes`

### 2. 樣式測試

- [ ] Tailwind CSS 樣式正確套用（需要 Vite dev server 運行）
- [ ] Bootstrap 5 Layout 正常顯示（sidebar、header、footer）
- [ ] Hover 效果正常（卡片陰影變化）
- [ ] 響應式設計正常（手機 1 欄、平板 2 欄、桌面 3 欄）

### 3. 授權測試

- [ ] 非 Admin 使用者無法訪問 `/Admin`（403 Forbidden）
- [ ] API `/api/admin/dashboard/stats` 需要 Admin 角色（401/403）

### 4. 整合測試（使用 Playwright MCP）

```javascript
// 1. 導航到 Dashboard
await page.goto('https://localhost:7035/Admin');

// 2. 等待 Vue.js 載入
await page.waitForSelector('#dashboard-app');

// 3. 驗證統計卡片顯示
const clientCount = await page.textContent('.text-indigo-600.text-3xl');
expect(parseInt(clientCount)).toBeGreaterThan(0);

// 4. 點擊 "Manage Clients" 連結
await page.click('a[href="/Admin/Clients"]');
await page.waitForURL('**/Admin/Clients');
```

## 📝 測試流程（參考 `dev_testing_guide.md`）

```powershell
# 1. 啟動資料庫
docker compose up -d db-service

# 2. 啟動 IdP（終端機 1）
cd Web.IdP
dotnet run --launch-profile https

# 3. 啟動 Vite（終端機 2）
cd Web.IdP\ClientApp
npm run dev

# 4. 訪問 Dashboard
# https://localhost:7035/Admin

# 5. 使用 Playwright MCP 測試
# （透過 VS Code 的 Copilot Chat）
```

## 🎯 預期成果

### 架構優勢

1. **一致性**: Dashboard 與 Clients/Scopes 使用相同的 Vue.js + Tailwind 架構
2. **可維護性**: 統一的開發模式，降低學習成本
3. **響應式**: Vue.js 的響應式資料綁定，即時更新統計數據
4. **可擴展性**: 未來可輕鬆添加更多統計圖表（Chart.js、ECharts 等）

### 技術棧統一

```
Razor Pages (Bootstrap 5)  →  Layout & Navigation (Server-rendered)
Vue.js (Tailwind CSS)      →  Interactive Components (Client-rendered)
API (ASP.NET Core)         →  Data Layer (RESTful)
```

## ⚠️ 注意事項

1. **Vite Dev Server**: 必須手動啟動 `npm run dev`（AutoRun 已關閉）
2. **CSS 依賴**: Tailwind 樣式需要 Vite 運行，但 Bootstrap Layout 不需要
3. **API 授權**: 確保所有 Admin API 都有 `[Authorize(Roles = AuthConstants.Roles.Admin)]`
4. **錯誤處理**: Vue.js 組件要妥善處理 API 失敗的情況
5. **語系**: 考慮未來國際化需求，統計標籤可改為 i18n key

## 🚀 後續增強（未來）

- [ ] 圖表顯示（用戶增長趨勢、Client 類型分佈）
- [ ] Recent Activity Timeline（最近的登入、Client 建立記錄）
- [ ] 即時更新（WebSocket 或 SignalR）
- [ ] 深色模式支援（Tailwind dark mode）
- [ ] 導出報表功能（PDF、CSV）
