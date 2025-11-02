# 開發測試指南

## 本機開發環境設定

### 預設語系
- 預設語系已設定為 **zh-TW（繁體中文）**
- 可在 `Program.cs` 中修改支援的語系

### Vite 開發伺服器設定
- **Vite AutoRun 已關閉**（`appsettings.Development.json` → `Vite.Server.AutoRun: false`）
- 原因：Vite.AspNetCore 的 AutoRun 有時會不穩定
- **建議**：手動啟動 Vite dev server

---

## 正確的啟動順序

### 1. 啟動資料庫（PostgreSQL）

```powershell
# 使用 Docker Compose 啟動資料庫
docker compose up -d db-service
```

### 2. 啟動 IdP 後端（ASP.NET Core）

```powershell
# 在專案根目錄執行
cd Web.IdP
dotnet run --launch-profile https
```

**重要提示**：
- IdP 會啟動在 `https://localhost:7035`
- Vite **不會**自動啟動（已關閉 AutoRun）

### 3. 手動啟動 Vite Dev Server

**開啟新的終端機視窗**，執行：

```powershell
# 切換到 ClientApp 目錄
cd Web.IdP\ClientApp

# 啟動 Vite
npm run dev
```

**驗證**：
- Vite 應該啟動在 `http://localhost:5173`
- 終端機會顯示：`VITE v5.4.21 ready in XXX ms`

### 4. （可選）啟動 TestClient

如果需要測試 OIDC 流程，開啟另一個終端機：

```powershell
cd TestClient
dotnet run --launch-profile https
```

- TestClient 會啟動在 `https://localhost:7001`

---

## Admin Portal 架構說明

### Bootstrap 5 + Vue.js 混合架構

```
Admin Portal
├── Razor Pages Layout (Bootstrap 5)
│   ├── _AdminLayout.cshtml - 主要 layout（sidebar、header、footer）
│   ├── Index.cshtml - Dashboard 頁面
│   ├── Clients.cshtml - Clients 管理頁面（掛載 Vue.js）
│   └── Scopes.cshtml - Scopes 管理頁面（掛載 Vue.js）
│
└── Vue.js Components (Tailwind CSS)
    ├── ClientApp/src/admin/clients/ - Clients 管理 SPA
    └── ClientApp/src/admin/scopes/ - Scopes 管理 SPA
```

**設計決策**：
- **Razor Pages 使用 Bootstrap 5**：穩定、不依賴 Vite、適合伺服器端渲染
- **Vue.js 組件使用 Tailwind CSS**：由 Vite 構建、適合互動式管理介面

---

## 測試流程

### 1. 測試 Admin Layout（Bootstrap 5）

訪問：`https://localhost:7035/Admin`

**預期結果**：
- ✅ 左側顯示 sidebar（固定 260px 寬）
- ✅ 頂部顯示 breadcrumbs
- ✅ 底部顯示 footer
- ✅ Bootstrap 5 樣式正常加載（從 CDN）
- ✅ Bootstrap Icons 圖示顯示正常

### 2. 測試 Vue.js 頁面（Clients 管理）

訪問：`https://localhost:7035/Admin/Clients`

**預期結果**：
- ✅ Vue.js 應用正常掛載
- ✅ Tailwind CSS 樣式正常（來自 Vite）
- ✅ 瀏覽器 console 顯示 `[vite] connected`
- ✅ Client 列表、搜尋、篩選、排序功能正常

### 3. 測試語系

訪問：`https://localhost:7035/Account/Login`

**預期結果**：
- ✅ 預設語系為 zh-TW
- ✅ 可透過語系切換器切換到 en-US

---

## 常見問題排除

### 問題 1：Vite 樣式未加載

**症狀**：Vue.js 頁面沒有 Tailwind 樣式

**解決方案**：
1. 確認 Vite dev server 已啟動（`npm run dev`）
2. 檢查瀏覽器 console 是否有 `[vite] connected` 訊息
3. 確認 Vite 運行在 `http://localhost:5173`

### 問題 2：Bootstrap 5 樣式未加載

**症狀**：Admin layout 排版錯亂

**解決方案**：
1. 檢查網路連線（Bootstrap 5 使用 CDN）
2. 確認 `_AdminLayout.cshtml` 的 `<link>` 標籤正確

### 問題 3：資料庫連線失敗

**症狀**：應用啟動時出現資料庫錯誤

**解決方案**：
```powershell
# 確認 PostgreSQL 容器運行中
docker ps

# 如果未運行，啟動它
docker compose up -d db-service
```

### 問題 4：連接埠佔用

**症狀**：`dotnet run` 失敗，顯示連接埠已被使用

**解決方案**：
```powershell
# 停止所有 dotnet 進程
taskkill /F /IM dotnet.exe /T

# 停止所有 node 進程
taskkill /F /IM node.exe /T
```

---

## 清理與重啟

```powershell
# 完整清理所有進程
taskkill /F /IM dotnet.exe /T 2>$null
taskkill /F /IM node.exe /T 2>$null

# 重新啟動（依序執行）
# 1. 資料庫
docker compose up -d db-service

# 2. IdP 後端（在終端機 1）
cd Web.IdP
dotnet run --launch-profile https

# 3. Vite（在終端機 2）
cd Web.IdP\ClientApp
npm run dev

# 4. TestClient（可選，在終端機 3）
cd TestClient
dotnet run --launch-profile https
```

---

## 預設管理員帳號

- **Email**: `admin@hybridauth.local`
- **Password**: `Admin@123`

**重要**：生產環境請務必修改預設密碼！

---

## 開發建議

1. **保持 Vite dev server 運行**：避免頻繁重啟，HMR（熱模組替換）會自動重新加載修改
2. **使用獨立終端機**：分別運行 IdP 和 Vite，方便查看各自的 log
3. **定期清理進程**：測試結束後執行 `taskkill` 避免殘留進程
4. **檢查語系資源檔**：如果新增語系，記得在 `Resources/` 目錄添加對應的 `.resx` 檔案
