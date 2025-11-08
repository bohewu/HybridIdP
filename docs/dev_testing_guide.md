# 開發測試指南

## 本機開發環境設定

### 預設語系
- 預設語系已設定為 **zh-TW（繁體中文）**
- 可在 `Program.cs` 中修改支援的語系

### 產品名稱 / 品牌設定（可配置）
- 可在 `Web.IdP/appsettings*.json` 的 `Branding` 節點調整：
  - `AppName`：短名稱（Sidebar/Logo）
  - `ProductName`：完整產品名（頁面標題、登入卡片）
- 例：
  ```json
  {
    "Branding": {
      "AppName": "Contoso IdP",
      "ProductName": "Contoso Identity Provider"
    }
  }
  ```
- 目前為設定檔配置；未提供後台 UI 編輯（可作為後續增強）。

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

**⚠️ 重要注意事項：**
- **只能啟動一次**：如果 Vite 已經在運行，**絕對不要**再次執行 `npm run dev`
- **檢查方法**：查看終端機是否已有 Vite 運行中（顯示 `VITE vX.X.X ready in XXX ms`）
- **錯誤徵兆**：重複啟動會導致連接埠衝突或 Vite HMR 失效
- **🚫 禁止執行 `npm run build`**：
  - 開發時**永遠不需要**執行 build 指令
  - Build 是用於正式環境部署，會覆蓋開發用的檔案
  - 如果誤執行了 build，請重新啟動 Vite dev server

**開啟新的終端機視窗**，執行：

```powershell
# 切換到 ClientApp 目錄
cd Web.IdP\ClientApp

# 啟動 Vite（只執行一次！）
npm run dev
```

**驗證**：
- Vite 應該啟動在 `http://localhost:5173`
- 終端機會顯示：`VITE v5.4.21 ready in XXX ms`
- 瀏覽器 console 應顯示：`[vite] connected`

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
- ✅ 登入頁面顯示「電子郵件或使用者名稱 / 密碼 / 記住我？」
- ✅ 頁面品牌顯示為 `Branding.ProductName`

---

## 常見問題排除

### ⚠️ 最常見錯誤：重複啟動 Vite 或執行 build

**症狀**：
- Vite HMR (Hot Module Replacement) 失效
- 樣式更新不生效
- 連接埠衝突錯誤
- 頁面空白或顯示舊版本

**原因**：
- ❌ 在 Vite 已運行的情況下再次執行 `npm run dev`
- ❌ 錯誤執行 `npm run build`（開發時不需要 build）

**解決方案**：
```powershell
# 1. 停止所有 node 進程
taskkill /F /IM node.exe /T

# 2. 重新啟動 Vite（只執行一次）
cd Web.IdP\ClientApp
npm run dev

# 3. 確認終端機顯示 "VITE vX.X.X ready in XXX ms"
# 4. 確認瀏覽器 console 顯示 "[vite] connected"
```

**預防措施**：
- ✅ 使用專用終端機視窗運行 Vite，保持開啟
- ✅ 檢查終端機標籤，確認 Vite 是否已運行
- ✅ **永遠不要執行 `npm run build`**（除非要部署到正式環境）
- ✅ 如果不確定，先執行 `taskkill /F /IM node.exe /T` 清理

---

### 問題 1：Vite 樣式未加載

**症狀**：Vue.js 頁面沒有 Tailwind 樣式

**解決方案**：
1. 確認 Vite dev server 已啟動（`npm run dev`）
2. 檢查瀏覽器 console 是否有 `[vite] connected` 訊息
3. 確認 Vite 運行在 `http://localhost:5173`
4. 檢查 `main.js` 是否有導入 `import './style.css'`
5. 確認 `style.css` 包含 `@tailwind` 指令

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

**⚠️ 重要提醒：**
- 清理後重啟時，每個服務**只啟動一次**
- 特別注意 Vite：確認終端機 2 沒有重複執行 `npm run dev`
- **絕對不要執行 `npm run build`**

```powershell
# 完整清理所有進程
taskkill /F /IM dotnet.exe /T 2>$null
taskkill /F /IM node.exe /T 2>$null

# 重新啟動（依序執行）
# 1. 資料庫
docker compose up -d db-service

# 2. IdP 後端（在終端機 1 - 使用專用視窗）
cd Web.IdP
dotnet run --launch-profile https

# 3. Vite（在終端機 2 - 使用專用視窗，只執行一次！）
cd Web.IdP\ClientApp
npm run dev
# ⚠️ 看到 "VITE vX.X.X ready in XXX ms" 後就不要再動這個終端機

# 4. TestClient（可選，在終端機 3 - 使用專用視窗）
cd TestClient
dotnet run --launch-profile https
```

**最佳實踐：**
- ✅ 為每個服務使用**專用的終端機視窗**，並標記清楚（Database / IdP / Vite / TestClient）
- ✅ 保持 Vite 終端機視窗開啟，不要關閉或重複執行
- ✅ 需要重啟時先執行完整清理指令
- ❌ 不要在多個終端機執行相同的指令

---

## 預設管理員帳號

- **Email**: `admin@hybridauth.local`
- **Password**: `Admin@123`

**重要**：生產環境請務必修改預設密碼！

---

## 測試方法：使用 MCP Playwright Browser

本專案使用 **Playwright MCP Server** 進行瀏覽器自動化測試，而非傳統的 `npx playwright test` 命令。

### 為什麼使用 MCP Server？

- ✅ **互動式測試**：可以即時查看瀏覽器狀態
- ✅ **逐步除錯**：每個步驟都可以檢查頁面快照
- ✅ **靈活控制**：可以暫停、檢查、繼續測試流程
- ✅ **整合 VS Code**：所有測試在 VS Code 內完成

### MCP Browser 測試範例

```typescript
// 使用 MCP 工具進行測試（透過 Copilot Agent）
// 1. 導航到頁面
mcp_playwright_browser_navigate({ url: 'https://localhost:7001' })

// 2. 點擊元素
mcp_playwright_browser_click({ 
  element: 'Profile link', 
  ref: 'e13' // 從 snapshot 獲取
})

// 3. 填寫表單
mcp_playwright_browser_type({ 
  element: 'Email input', 
  ref: 'e5',
  text: 'admin@hybridauth.local' 
})

// 4. 檢查頁面狀態
mcp_playwright_browser_snapshot()
```

### E2E 測試檔案位置

- `e2e/tests/testclient-scope-claims.spec.ts` - 測試 scope-mapped claims
- `e2e/tests/admin-claims-ui.spec.ts` - 測試 Admin Claims UI

---

## 失敗場景測試指南

### 1. Authorization/Authentication Failures（授權/認證失敗）

#### 1.1 使用者拒絕授權 (User Denies Consent)

**測試步驟**：
1. 訪問 TestClient (`https://localhost:7001`)
2. 點擊 "Profile" 觸發 OIDC 登入
3. 在授權頁面點擊 **"Deny"** 按鈕

**預期結果**：
- ❌ 應返回 TestClient 並顯示錯誤訊息
- ❌ URL 包含 `error=access_denied`
- ❌ 不應發放 token

**測試重點**：
- 驗證錯誤訊息是否友善
- 確認不會洩漏敏感資訊
- 檢查錯誤是否正確記錄

#### 1.2 無效的 Client ID

**測試步驟**：
1. 手動構建授權請求，使用不存在的 `client_id`
2. 訪問：`https://localhost:7035/connect/authorize?client_id=invalid_client&...`

**預期結果**：
- ❌ 返回 400 Bad Request 或 OAuth 錯誤頁面
- ❌ 錯誤：`error=invalid_client`
- ❌ 不應重定向到 redirect_uri（因為 client 不可信）

#### 1.3 無效的 Redirect URI

**測試步驟**：
1. 使用有效 client_id 但未註冊的 redirect_uri
2. 訪問：`https://localhost:7035/connect/authorize?client_id=test_client&redirect_uri=https://evil.com/callback&...`

**預期結果**：
- ❌ 返回錯誤頁面（不重定向到惡意網址）
- ❌ 錯誤：`error=invalid_request`
- ❌ 記錄安全警告日誌

#### 1.4 缺少必要的 Scope

**測試步驟**：
1. 發送授權請求但不包含 `openid` scope
2. 或請求未授權的 scope

**預期結果**：
- ❌ 返回錯誤：`error=invalid_scope`
- ❌ 不應進入授權頁面

#### 1.5 過期的 Authorization Code

**測試步驟**：
1. 完成授權流程獲取 code
2. 等待 code 過期（預設 5 分鐘）
3. 嘗試兌換 code

**預期結果**：
- ❌ Token endpoint 返回錯誤
- ❌ 錯誤：`error=invalid_grant`
- ❌ Code 應標記為已使用/已過期

#### 1.6 PKCE Challenge 不匹配

**測試步驟**：
1. 使用正確的 `code_challenge` 獲取 code
2. 在 token 請求中使用錯誤的 `code_verifier`

**預期結果**：
- ❌ Token endpoint 返回錯誤
- ❌ 錯誤：`error=invalid_grant`
- ❌ 詳細錯誤：code_verifier 驗證失敗

---

### 2. Token Validation Failures（Token 驗證失敗）

#### 2.1 過期的 Access Token

**測試步驟**：
1. 獲取 access token
2. 修改系統時間或等待 token 過期（預設 1 小時）
3. 使用過期 token 呼叫 API

**預期結果**：
- ❌ API 返回 401 Unauthorized
- ❌ WWW-Authenticate header 包含 `error="invalid_token"`
- ❌ 錯誤描述：token 已過期

#### 2.2 無效的 Token 簽章

**測試步驟**：
1. 獲取有效 token
2. 修改 token 的任意字元
3. 使用修改後的 token

**預期結果**：
- ❌ 返回 401 Unauthorized
- ❌ 錯誤：簽章驗證失敗
- ❌ 記錄安全警告

#### 2.3 Token 在 nbf 之前使用

**測試步驟**：
1. 獲取 token
2. 如果 token 包含 `nbf`（not before），修改系統時間到 nbf 之前
3. 使用 token

**預期結果**：
- ❌ 返回 401 Unauthorized
- ❌ 錯誤：token 尚未生效

#### 2.4 已撤銷的 Token

**測試步驟**：
1. 獲取 token
2. 透過管理介面或 API 撤銷該 token
3. 嘗試使用被撤銷的 token

**預期結果**：
- ❌ 返回 401 Unauthorized
- ❌ 錯誤：token 已被撤銷

---

### 3. Scope-Mapped Claims Edge Cases（Scope 映射 Claims 邊緣情況）

#### 3.1 User Property Path 不存在

**測試步驟**：
1. 在 Claims 管理建立 claim，UserPropertyPath 設為 `User.NonExistentProperty`
2. 將該 claim 映射到 scope
3. 登入並請求該 scope

**預期結果**：
- ✅ Token 仍應成功發放
- ⚠️ 該 claim 不應出現在 token 中（或值為 null/empty）
- ⚠️ 後端應記錄警告日誌
- ❌ 不應拋出例外導致登入失敗

**程式碼位置**：`Web.IdP/Pages/Connect/Authorize.cshtml.cs` → `ResolveUserProperty()`

#### 3.2 Null Property 值且 AlwaysInclude=false

**測試步驟**：
1. 建立 claim 映射到 `User.PhoneNumber`（可能為 null）
2. 設定 `AlwaysInclude = false`
3. 登入時 user.PhoneNumber 為 null

**預期結果**：
- ✅ Token 成功發放
- ✅ 該 claim 不應出現在 token 中（因為 AlwaysInclude=false）
- ✅ 如果 AlwaysInclude=true，應包含空字串

**程式碼位置**：`AddScopeMappedClaimsAsync()` 的邏輯

#### 3.3 Scope 無對應的 Claims

**測試步驟**：
1. 建立新 scope（如 `custom_scope`）
2. 不映射任何 claims 到該 scope
3. 請求該 scope

**預期結果**：
- ✅ 授權流程正常
- ✅ Token 中不包含額外 claims（只有標準 claims）
- ✅ Scope 仍出現在 token 的 `scope` claim 中

#### 3.4 循環參照的 Property Path

**測試步驟**：
1. 建立 claim，UserPropertyPath 為 `User.User.User...`（如果可能）
2. 或建立自引用的複雜物件圖

**預期結果**：
- ❌ 應偵測並中止無限迴圈
- ❌ 返回 null 或記錄錯誤
- ✅ 不應造成 StackOverflowException

**建議**：
- 限制 property path 深度（如最多 5 層）
- 添加迴圈偵測機制

---

### 4. Database/Infrastructure Failures（資料庫/基礎設施失敗）

#### 4.1 資料庫連線中斷

**測試步驟**：
1. 啟動應用並登入
2. 停止 PostgreSQL：`docker compose stop db-service`
3. 嘗試授權或 token 操作

**預期結果**：
- ❌ 返回 500 Internal Server Error 或友善錯誤頁面
- ❌ 記錄詳細錯誤日誌
- ✅ 不應洩漏資料庫連線字串或敏感資訊

**恢復步驟**：
```powershell
docker compose start db-service
```

#### 4.2 EF Core Concurrency Conflicts

**測試步驟**：
1. 同時從兩個瀏覽器對同一個 authorization 進行操作
2. 或在同一時間更新同一個 token

**預期結果**：
- ❌ 其中一個操作失敗並返回錯誤
- ❌ 錯誤：`DbUpdateConcurrencyException`
- ✅ 應重試或提示使用者刷新

**程式碼位置**：所有 `SaveChangesAsync()` 呼叫應包含 try-catch

#### 4.3 Redis Cache 不可用（如使用分散式快取）

**測試步驟**：
1. 如果配置了 Redis，停止 Redis 服務
2. 嘗試登入或操作

**預期結果**：
- ⚠️ 應降級到記憶體快取或直接查詢資料庫
- ✅ 功能仍可正常運作（效能降低）
- ⚠️ 記錄警告日誌

---

### 5. UI/UX Failure Paths（UI/UX 失敗路徑）

#### 5.1 重複的 Claim Name

**測試步驟**：
1. 在 Admin Claims UI 建立 claim，Name = `email`
2. 嘗試建立另一個 Name = `email` 的 claim

**預期結果**：
- ❌ 應顯示驗證錯誤
- ❌ 錯誤訊息：「Claim name 已存在」
- ✅ 表單不應提交
- ✅ 使用者可修正錯誤並重試

#### 5.2 映射到不存在的 Claim

**測試步驟**：
1. 建立 scope mapping 並選擇某個 claim
2. 刪除該 claim（但不刪除 mapping）
3. 嘗試請求該 scope

**預期結果**：
- ⚠️ Token 仍應發放
- ⚠️ 忽略無效的 mapping
- ⚠️ 記錄警告日誌
- 🔧 **建議**：刪除 claim 時應級聯刪除或警告相關 mappings

#### 5.3 刪除已映射的 Claim

**測試步驟**：
1. 建立 claim 並映射到多個 scopes
2. 嘗試刪除該 claim

**預期選項**：
- **選項 A（嚴格）**：阻止刪除，顯示錯誤訊息：「此 claim 正被 X 個 scopes 使用」
- **選項 B（級聯）**：刪除 claim 並同時刪除所有 mappings（需確認）
- **選項 C（軟刪除）**：標記為已刪除但保留資料

**目前實作**：需檢查並實作適當的保護機制

#### 5.4 無效的 UserPropertyPath 格式

**測試步驟**：
1. 建立 claim，UserPropertyPath = `User..Email` 或 `.Email` 或其他無效格式
2. 映射到 scope 並登入

**預期結果**：
- ✅ 表單驗證應在輸入時檢查格式
- ⚠️ 如果繞過驗證，後端應安全處理
- ⚠️ 記錄警告並返回 null

**建議驗證規則**：
- 只允許 `a-zA-Z0-9._` 字元
- 不能以 `.` 開頭或結尾
- 不能有連續的 `..`
- 長度限制（如最多 200 字元）

---

## 測試優先順序

### 🔴 高優先級（必須測試）
1. ✅ 使用者拒絕授權
2. ✅ 無效 Client ID / Redirect URI（安全性）
3. ✅ Token 過期驗證
4. ✅ 資料庫連線失敗處理

### 🟡 中優先級（應該測試）
5. ⚠️ PKCE 驗證失敗
6. ⚠️ Scope-mapped claims 邊緣情況
7. ⚠️ Concurrency conflicts

### 🟢 低優先級（建議測試）
8. ⚙️ UI 驗證錯誤訊息
9. ⚙️ Cache 降級處理

---

## 自動化測試實作建議

### E2E 失敗測試範例

```typescript
// e2e/tests/authorization-failures.spec.ts
test('User denies consent', async () => {
  // 1. Navigate to TestClient
  await mcp_playwright_browser_navigate('https://localhost:7001')
  
  // 2. Click Profile to trigger login
  await mcp_playwright_browser_click({ element: 'Profile', ref: 'e13' })
  
  // 3. Should redirect to IdP authorization page
  await mcp_playwright_browser_snapshot()
  // Verify: page contains "Allow Access" and "Deny" buttons
  
  // 4. Click Deny button
  await mcp_playwright_browser_click({ element: 'Deny', ref: 'e41' })
  
  // 5. Verify error response
  const snapshot = await mcp_playwright_browser_snapshot()
  // Should contain error message
  // URL should include error=access_denied
})
```

### Unit Test 範例

```csharp
// Tests.Application.UnitTests/PropertyResolverTests.cs
[Fact]
public void ResolveUserProperty_NonExistentPath_ReturnsNull()
{
    var user = new ApplicationUser { Email = "test@example.com" };
    
    var result = ResolveUserProperty(user, "NonExistent.Property");
    
    Assert.Null(result);
    // 應記錄警告日誌
}

[Fact]
public void ResolveUserProperty_NullValue_ReturnsNull()
{
    var user = new ApplicationUser { PhoneNumber = null };
    
    var result = ResolveUserProperty(user, "PhoneNumber");
    
    Assert.Null(result);
}
```

---

## 日誌監控建議

### 應記錄的關鍵錯誤

1. **安全事件**：
   - 無效的 client_id 或 redirect_uri
   - Token 簽章驗證失敗
   - 異常的授權請求模式

2. **業務邏輯錯誤**：
   - Property path 解析失敗
   - Scope mapping 找不到 claim
   - 資料庫操作失敗

3. **基礎設施問題**：
   - 資料庫連線失敗
   - Cache 服務不可用
   - 外部 API 呼叫失敗

### 日誌等級指引

- **Critical**: 應用無法繼續運行（資料庫完全無法連線）
- **Error**: 操作失敗但應用可繼續（單一 token 發放失敗）
- **Warning**: 預期外情況但已處理（property path 不存在）
- **Information**: 正常業務事件（使用者登入、授權）

---

## 開發建議

1. **保持 Vite dev server 運行**：避免頻繁重啟，HMR（熱模組替換）會自動重新加載修改
2. **使用獨立終端機**：分別運行 IdP 和 Vite，方便查看各自的 log
3. **定期清理進程**：測試結束後執行 `taskkill` 避免殘留進程
4. **檢查語系資源檔**：如果新增語系，記得在 `Resources/` 目錄添加對應的 `.resx` 檔案
