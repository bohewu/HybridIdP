# HybridIdP 開發指南

## 🎯 簡介與文件結構

本文件旨在提供 HybridAuthIdP 專案的開發規範、工作流程、最佳實踐和實作範本。它整合了原有的 `WORKFLOW.md`、`dev_testing_guide.md` 和 `implementation_guidelines.md`，以提供更集中、高效的資訊查閱體驗。

### 📚 文件結構總覽

```
docs/
├── DEVELOPMENT_GUIDE.md           # 👈 你在這裡 - 開發工作流程、規範、測試指南
├── ARCHITECTURE.md                # 📐 架構決策與技術棧詳解
├── FEATURES.md                    # ✨ 未來增強功能與特定功能整合
├── PROJECT_STATUS.md              # ✅ 專案進度、待辦事項與已完成摘要
├── README.md                      # 📚 專案總覽與文件快速導航
├── idp_req_details.md             # 📚 完整需求文件（參考用）
└── examples/                      # 程式碼範例目錄
    └── ...
```

---

## 🚀 快速開始（新 Session）

### 1. 閱讀順序

**第一次進入專案:**
1.  `DEVELOPMENT_GUIDE.md` (本文件) - 了解工作流程、規範與測試
2.  `PROJECT_STATUS.md` - 了解已完成的部分與下一步要做什麼
3.  `ARCHITECTURE.md` - 學習專案架構與技術棧

**繼續開發時:**
1.  `PROJECT_STATUS.md` - 確認當前任務
2.  `DEVELOPMENT_GUIDE.md` - 查閱實作範本與測試指南

### 2. 環境啟動檢查清單與正確的啟動順序

在開始開發前，請確保所有必要的服務都已正確啟動。

**✅ 檢查清單**
-   □ PostgreSQL 資料庫運行中
-   □ IdP Backend 運行中
-   □ Vite Dev Server 運行中

**正確的啟動順序**

#### 1. 啟動資料庫（PostgreSQL）

```powershell
// See docs/examples/development_guide_start_db.ps1.example
```

#### 2. 啟動 IdP 後端（ASP.NET Core）

```powershell
// See docs/examples/development_guide_start_idp_backend.ps1.example
```

**重要提示**:
-   IdP 會啟動在 `https://localhost:7035`
-   Vite **不會**自動啟動（已關閉 AutoRun）

#### 3. 手動啟動 Vite Dev Server

**⚠️ 重要注意事項：**
-   **只能啟動一次**：如果 Vite 已經在運行，**絕對不要**再次執行 `npm run dev`
-   **檢查方法**：查看終端機是否已有 Vite 運行中（顯示 `VITE vX.X.X ready in XXX ms`）
-   **錯誤徵兆**：重複啟動會導致連接埠衝突或 Vite HMR 失效
-   **🚫 禁止執行 `npm run build`**：
    -   開發時**永遠不需要**執行 build 指令
    -   Build 是用於正式環境部署，會覆蓋開發用的檔案
    -   如果誤執行了 build，請重新啟動 Vite dev server

**開啟新的終端機視窗**，執行：

```powershell
// See docs/examples/development_guide_start_vite.ps1.example
```

**驗證**:
-   Vite 應該啟動在 `http://localhost:5173`
-   終端機會顯示：`VITE v5.4.21 ready in XXX ms`
-   瀏覽器 console 應顯示：`[vite] connected`

#### 4. （可選）啟動 TestClient

如果需要測試 OIDC 流程，開啟另一個終端機：

```powershell
// See docs/examples/development_guide_start_testclient.ps1.example
```

-   TestClient 會啟動在 `https://localhost:7001`

### 3. Git 狀態確認

```bash
// See docs/examples/development_guide_git_status.bash.example
```

---

## 🎯 Git Commit 策略：Small Steps (Option A)

### 核心原則

**Philosophy:** Commit early, commit often - 每個邏輯單元一個 commit

### 實作順序

```text
// See docs/examples/idp_req_details_git_commit_strategy_implementation_order.txt.example
```

### Commit Message 格式

```text
// See docs/examples/idp_req_details_git_commit_message_format.txt.example
```

**Types:**
-   `feat`: 新功能
-   `fix`: Bug 修復
-   `test`: 測試
-   `docs`: 文件
-   `refactor`: 重構
-   `style`: 格式化
-   `chore`: 建置工具

**Scopes:**
-   `api`: Backend API
-   `ui`: Frontend UI
-   `auth`: 認證/授權
-   `db`: 資料庫
-   `test`: 測試

**範例:**

```bash
// See docs/examples/idp_req_details_git_commit_examples.bash.example
```

### 每個 Commit 前的檢查清單

-   ✅ 程式碼編譯無錯誤
-   ✅ 相關測試通過
-   ✅ 應用程式可正常運行
-   ✅ 沒有破壞現有功能

---

## 📋 開發工作流程

### Step-by-Step 流程

```text
// See docs/examples/development_guide_step_by_step_workflow.txt.example
```

### API 優先，後端先行

**規則：永遠先完成並測試 API，再開始 UI**

```text
// See docs/examples/development_guide_api_first_rule.txt.example
```

### UI 分層實作

```text
// See docs/examples/development_guide_ui_layered_approach.txt.example
```

### 🔧 Shared UI components — LoadingIndicator (Phase 9.6 ✅)

為了讓整個管理後台在「載入中」狀態顯示一致，我們提供了統一的載入 UI 方案，使用 **藍色 Tailwind spinner** 樣式（`animate-spin rounded-full border-b-2 border-blue-600`）。

**📁 檔案位置：**
- Component: `Web.IdP/ClientApp/src/components/common/LoadingIndicator.vue`
- Directive: `Web.IdP/ClientApp/src/directives/v-loading.js`

**🎨 統一樣式特點：**
- 藍色 spinner (`border-blue-600`)
- 三種尺寸：`sm` (h-8 w-8)、`md` (h-12 w-12)、`lg` (h-16 w-16)
- 支援顯示訊息文字（使用 i18n）
- 提供 `data-testid="loading-indicator"` 用於 E2E 測試

**🔧 註冊方式：**
所有 admin SPA 的 `main.js` 都已註冊 v-loading 指令：

```js
import vLoading from '@/directives/v-loading'
app.directive('loading', vLoading)
```

**📝 使用規範：**
1. **頁面級別載入** → 使用 `v-loading` 指令
2. **組件級別載入** → 使用 `LoadingIndicator` 組件

**組件用法範例（component-level）：**

```vue
<!-- 小尺寸，帶訊息 -->
<LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('loading.message')" />

<!-- 中尺寸（預設） -->
<LoadingIndicator v-if="loading" :loading="loading" :message="t('loading.message')" />
```

### 🔁 v-loading 指令（推薦用於頁面級載入）✅

**用途：** 整頁或大範圍容器的覆蓋式載入狀態

**優勢：**
- 保留頁面內容結構（不破壞布局）
- 一行代碼實現 overlay 效果
- 自動鎖定使用者互動
- 內部使用 `LoadingIndicator` 組件確保視覺一致性

**標準用法（所有 admin 頁面已遷移）：**

```vue
<template>
  <div class="max-w-7xl mx-auto"
       v-loading="{ loading: loading, overlay: true, message: t('admin.xxx.loading') }">
    <!-- 頁面內容 -->
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const loading = ref(true)  // 初始值必須為 true，確保首次載入時顯示 spinner

onMounted(async () => {
  // 載入數據...
  loading.value = false
})
</script>
```

**⚠️ 重要注意事項：**
1. **loading 初始值必須為 `true`**：確保頁面載入時立即顯示 spinner
2. **訊息使用 i18n**：所有顯示文字都應該使用 `t()` 函數翻譯
3. **指令已全域註冊**：所有 admin SPA 的 main.js 都已註冊，無需額外導入

**支援選項：**
- `loading`: boolean - 控制顯示/隱藏
- `overlay`: boolean (預設 true) - 是否使用覆蓋層模式
- `message`: string - 顯示的載入訊息
- `size`: 'sm' | 'md' | 'lg' (預設 'md') - spinner 尺寸

### 🧩 組件級載入 → 使用 LoadingIndicator 組件 ✅

**用途：** 單一組件或局部區域的載入狀態（卡片、表單、小區塊）

**標準用法：**

```vue
<template>
  <div class="component-container">
    <LoadingIndicator v-if="loading" :loading="loading" size="sm" :message="t('component.loading')" />
    
    <div v-else>
      <!-- 組件內容 -->
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import LoadingIndicator from '@/components/common/LoadingIndicator.vue'

const { t } = useI18n()
const loading = ref(true)
</script>
```

**已遷移的組件：**
- ✅ `BrandingSettings.vue`
- ✅ `UserSessions.vue`
- ✅ `UserList.vue`
- ✅ `LoginHistoryDialog.vue`
- ✅ `RoleAssignment.vue`
- ✅ `AuditLogViewer.vue`

**最佳實踐：**
1. **使用 v-if 條件渲染**：`v-if="loading"` 確保載入完成後不佔用 DOM
2. **傳遞 i18n 訊息**：`:message="t('xxx.loading')"` 使用翻譯文字
3. **選擇適當尺寸**：`size="sm"` 適合小組件，`size="md"` 適合一般組件
4. **保持無障礙性**：組件內建 aria-label 和 role="status"

---

## ⚠️ 關鍵注意事項

### 🔴 Tailwind CSS Setup - 每個 Vue SPA 必須

**每次建立新的 Vue SPA（例如：users/, roles/, clients/）時：**

1.  **創建 `style.css`**

```css
// See docs/examples/idp_req_details_tailwind_style_css.css.example
```

2.  **在 `main.js` 中 import**

```javascript
// See docs/examples/idp_req_details_tailwind_main_js_import.js.example
```

3.  **驗證：** 瀏覽器開發工具 Console 應該看到 `[vite] connected`，且 Tailwind 樣式正常運作

**❌ 如果忘記 import style.css → 整個排版會跑掉！**

### 🔴 Vite Dev Server 管理

**最常見錯誤：**

1.  **❌ 不要重複執行 `npm run dev`**
    -   Vite 已經在背景運行時，再執行會導致 port 衝突
    -   檢查方法：瀏覽器訪問 `http://localhost:5173` 看是否運行中

2.  **❌ 開發時絕對不要執行 `npm run build`**
    -   Build 是用於生產環境
    -   開發時只需要 `npm run dev`
    -   Build 會清空 dist/ 並影響開發流程

3.  **✅ 正確做法：**
    -   第一次啟動：`npm run dev`
    -   後續開發：保持 Vite 運行，不要關閉
    -   如果需要重啟：先 Ctrl+C 停止，再 `npm run dev`

---

## 🧪 測試指南

### 1. 測試流程總覽

```text
// See docs/examples/development_guide_testing_overview.txt.example
```

### 2. Admin Portal 架構說明

```text
// See docs/examples/development_guide_admin_portal_architecture.txt.example
```

### 3. 測試 Admin Layout（Bootstrap 5）

訪問：`https://localhost:7035/Admin`

**預期結果**：
-   ✅ 左側顯示 sidebar（固定 260px 寬）
-   ✅ 頂部顯示 breadcrumbs
-   ✅ 底部顯示 footer
-   ✅ Bootstrap 5 樣式正常加載（從 CDN）
-   ✅ Bootstrap Icons 圖示顯示正常

### 4. 測試 Vue.js 頁面（Clients 管理）

訪問：`https://localhost:7035/Admin/Clients`

**預期結果**：
-   ✅ Vue.js 應用正常掛載
-   ✅ Tailwind CSS 樣式正常（來自 Vite）
-   ✅ 瀏覽器 console 顯示 `[vite] connected`
-   ✅ Client 列表、搜尋、篩選、排序功能正常

### 5. 測試語系

訪問：`https://localhost:7035/Account/Login`

**預期結果**：
-   ✅ 預設語系為 zh-TW
-   ✅ 可透過語系切換器切換到 en-US
-   ✅ 登入頁面顯示「電子郵件或使用者名稱 / 密碼 / 記住我？」
-   ✅ 頁面品牌顯示為 `Branding.ProductName`

### 6. 常見問題排除

#### ⚠️ 最常見錯誤：重複啟動 Vite 或執行 build

**症狀**：
-   Vite HMR (Hot Module Replacement) 失效
-   樣式更新不生效
-   連接埠衝突錯誤
-   頁面空白或顯示舊版本

**原因**：
-   ❌ 在 Vite 已運行的情況下再次執行 `npm run dev`
-   ❌ 錯誤執行 `npm run build`（開發時不需要 build）

**解決方案**：
```powershell
// See docs/examples/development_guide_vite_troubleshooting.ps1.example
```

**預防措施**：
-   ✅ 使用專用終端機視窗運行 Vite，保持開啟
-   ✅ 檢查終端機標籤，確認 Vite 是否已運行
-   ✅ **永遠不要執行 `npm run build`**（除非要部署到正式環境）
-   ✅ 如果不確定，先執行 `taskkill /F /IM node.exe /T` 清理

#### 問題 1：Vite 樣式未加載

**症狀**：Vue.js 頁面沒有 Tailwind 樣式

**解決方案**：
1.  確認 Vite dev server 已啟動（`npm run dev`）
2.  檢查瀏覽器 console 是否有 `[vite] connected` 訊息
3.  確認 Vite 運行在 `http://localhost:5173`
4.  檢查 `main.js` 是否有導入 `import './style.css'`
5.  確認 `style.css` 包含 `@tailwind` 指令

#### 問題 2：Bootstrap 5 樣式未加載

**症狀**：Admin layout 排版錯亂

**解決方案**：
1.  檢查網路連線（Bootstrap 5 使用 CDN）
2.  確認 `_AdminLayout.cshtml` 的 `<link>` 標籤正確

#### 問題 3：資料庫連線失敗

**症狀**：應用啟動時出現資料庫錯誤

**解決方案**：
```powershell
// See docs/examples/development_guide_db_connection_troubleshooting.ps1.example
```

#### 問題 4：連接埠佔用

**症狀**：`dotnet run` 失敗，顯示連接埠已被使用

**解決方案**：
```powershell
// See docs/examples/development_guide_port_in_use_troubleshooting.ps1.example
```

### 7. 清理與重啟

**⚠️ 重要提醒：**
-   清理後重啟時，每個服務**只啟動一次**
-   特別注意 Vite：確認終端機 2 沒有重複執行 `npm run dev`
-   **絕對不要執行 `npm run build`**

```powershell
// See docs/examples/development_guide_cleanup_and_restart.ps1.example
```

**最佳實踐：**
-   ✅ 為每個服務使用**專用的終端機視窗**，並標記清楚（Database / IdP / Vite / TestClient）
-   ✅ 保持 Vite 終端機視窗開啟，不要關閉或重複執行
-   ✅ 需要重啟時先執行完整清理指令
-   ❌ 不要在多個終端機執行相同的指令

### 8. 預設管理員帳號

-   **Email**: `admin@hybridauth.local`
-   **Password**: `Admin@123`

**重要**：生產環境請務必修改預設密碼！

### 9. 測試方法：使用 MCP Playwright Browser

本專案使用 **Playwright MCP Server** 進行瀏覽器自動化測試，而非傳統的 `npx playwright test` 命令。

#### 為什麼使用 MCP Server？

-   ✅ **互動式測試**：可以即時查看瀏覽器狀態
-   ✅ **逐步除錯**：每個步驟都可以檢查頁面快照
-   ✅ **靈活控制**：可以暫停、檢查、繼續測試流程
-   ✅ **整合 VS Code**：所有測試在 VS Code 內完成

#### MCP Browser 測試範例

```typescript
// See docs/examples/development_guide_mcp_test_example.ts.example
```

#### E2E 測試檔案位置
-   請先讀取e2e/README.md
-   `e2e/tests/...` - 測試 scope-mapped claims


### 10. 失敗場景測試指南

#### 10.1 Authorization/Authentication Failures（授權/認證失敗）

##### 10.1.1 使用者拒絕授權 (User Denies Consent)

**測試步驟**：
1.  訪問 TestClient (`https://localhost:7001`)
2.  點擊 "Profile" 觸發 OIDC 登入
3.  在授權頁面點擊 **"Deny"** 按鈕

**預期結果**：
-   ❌ 應返回 TestClient 並顯示錯誤訊息
-   ❌ URL 包含 `error=access_denied`
-   ❌ 不應發放 token

**測試重點**：
-   驗證錯誤訊息是否友善
-   確認不會洩漏敏感資訊
-   檢查錯誤是否正確記錄

##### 10.1.2 無效的 Client ID

**測試步驟**：
1.  手動構建授權請求，使用不存在的 `client_id`
2.  訪問：`https://localhost:7035/connect/authorize?client_id=invalid_client&...`

**預期結果**：
-   ❌ 返回 400 Bad Request 或 OAuth 錯誤頁面
-   ❌ 錯誤：`error=invalid_client`
-   ❌ 不應重定向到 redirect_uri（因為 client 不可信）

##### 10.1.3 無效的 Redirect URI

**測試步驟**：
1.  使用有效 client_id 但未註冊的 redirect_uri
2.  訪問：`https://localhost:7035/connect/authorize?client_id=test_client&redirect_uri=https://evil.com/callback&...`

**預期結果**：
-   ❌ 返回錯誤頁面（不重定向到惡意網址）
-   ❌ 錯誤：`error=invalid_request`
-   ❌ 記錄安全警告日誌

##### 10.1.4 缺少必要的 Scope

**測試步驟**：
1.  發送授權請求但不包含 `openid` scope
2.  或請求未授權的 scope

**預期結果**：
-   ❌ 返回錯誤：`error=invalid_scope`
-   ❌ 不應進入授權頁面

##### 10.1.5 過期的 Authorization Code

**測試步驟**：
1.  完成授權流程獲取 code
2.  等待 code 過期（預設 5 分鐘）
3.  嘗試兌換 code

**預期結果**：
-   ❌ Token endpoint 返回錯誤
-   ❌ 錯誤：`error=invalid_grant`
-   ❌ Code 應標記為已使用/已過期

##### 10.1.6 PKCE Challenge 不匹配

**測試步驟**：
1.  使用正確的 `code_challenge` 獲取 code
2.  在 token 請求中使用錯誤的 `code_verifier`

**預期結果**：
-   ❌ Token endpoint 返回錯誤
-   ❌ 錯誤：`error=invalid_grant`
-   ❌ 詳細錯誤：code_verifier 驗證失敗

#### 10.2 Token Validation Failures（Token 驗證失敗）

##### 10.2.1 過期的 Access Token

**測試步驟**：
1.  獲取 access token
2.  修改系統時間或等待 token 過期（預設 1 小時）
3.  使用過期 token 呼叫 API

**預期結果**：
-   ❌ API 返回 401 Unauthorized
-   ❌ WWW-Authenticate header 包含 `error="invalid_token"`
-   ❌ 錯誤描述：token 已過期

##### 10.2.2 無效的 Token 簽章

**測試步驟**：
1.  獲取有效 token
2.  修改 token 的任意字元
3.  使用修改後的 token

**預期結果**：
-   ❌ 返回 401 Unauthorized
-   ❌ 錯誤：簽章驗證失敗
-   ❌ 記錄安全警告

##### 10.2.3 Token 在 nbf 之前使用

**測試步驟**：
1.  獲取 token
2.  如果 token 包含 `nbf`（not before），修改系統時間到 nbf 之前
3.  使用 token

**預期結果**：
-   ❌ 返回 401 Unauthorized
-   ❌ 錯誤：token 尚未生效

##### 10.2.4 已撤銷的 Token

**測試步驟**：
1.  獲取 token
2.  透過管理介面或 API 撤銷該 token
3.  嘗試使用被撤銷的 token

**預期結果**：
-   ❌ 返回 401 Unauthorized
-   ❌ 錯誤：token 已被撤銷

#### 10.3 Scope-Mapped Claims Edge Cases（Scope 映射 Claims 邊緣情況）

##### 10.3.1 User Property Path 不存在

**測試步驟**：
1.  在 Claims 管理建立 claim，UserPropertyPath 設為 `User.NonExistentProperty`
2.  將該 claim 映射到 scope
3.  登入並請求該 scope

**預期結果**：
-   ✅ Token 仍應成功發放
-   ⚠️ 該 claim 不應出現在 token 中（或值為 null/empty）
-   ⚠️ 後端應記錄警告日誌
-   ❌ 不應拋出例外導致登入失敗

**程式碼位置**：`Web.IdP/Pages/Connect/Authorize.cshtml.cs` → `ResolveUserProperty()`

##### 10.3.2 Null Property 值且 AlwaysInclude=false

**測試步驟**：
1.  建立 claim 映射到 `User.PhoneNumber`（可能為 null）
2.  設定 `AlwaysInclude = false`
3.  登入時 user.PhoneNumber 為 null

**預期結果**：
-   ✅ Token 成功發放
-   ✅ 該 claim 不應出現在 token 中（因為 AlwaysInclude=false）
-   ✅ 如果 AlwaysInclude=true，應包含空字串

**程式碼位置**：`AddScopeMappedClaimsAsync()` 的邏輯

##### 10.3.3 Scope 無對應的 Claims

**測試步驟**：
1.  建立新 scope（如 `custom_scope`）
2.  不映射任何 claims 到該 scope
3.  請求該 scope

**預期結果**：
-   ✅ 授權流程正常
-   ✅ Token 中不包含額外 claims（只有標準 claims）
-   ✅ Scope 仍出現在 token 的 `scope` claim 中

##### 10.3.4 循環參照的 Property Path

**測試步驟**：
1.  建立 claim，UserPropertyPath 為 `User.User.User...`（如果可能）
2.  或建立自引用的複雜物件圖

**預期結果**：
-   ❌ 應偵測並中止無限迴圈
-   ❌ 返回 null 或記錄錯誤
-   ✅ 不應造成 StackOverflowException

**建議**：
-   限制 property path 深度（如最多 5 層）
-   添加迴圈偵測機制

#### 10.4 Database/Infrastructure Failures（資料庫/基礎設施失敗）

##### 10.4.1 資料庫連線中斷

**測試步驟**：
1.  啟動應用並登入
2.  停止 PostgreSQL：`docker compose stop postgres-service`
3.  嘗試授權或 token 操作

**預期結果**：
-   ❌ 返回 500 Internal Server Error 或友善錯誤頁面
-   ❌ 記錄詳細錯誤日誌
-   ✅ 不應洩漏資料庫連線字串或敏感資訊

**恢復步驟**：
```powershell
// See docs/examples/development_guide_db_failure_recovery.ps1.example
```

##### 10.4.2 EF Core Concurrency Conflicts

**測試步驟**：
1.  同時從兩個瀏覽器對同一個 authorization 進行操作
2.  或在同一時間更新同一個 token

**預期結果**：
-   ❌ 其中一個操作失敗並返回錯誤
-   ❌ 錯誤：`DbUpdateConcurrencyException`
-   ✅ 應重試或提示使用者刷新

**程式碼位置**：所有 `SaveChangesAsync()` 呼叫應包含 try-catch

##### 10.4.3 Redis Cache 不可用（如使用分散式快取）

**測試步驟**：
1.  如果配置了 Redis，停止 Redis 服務
2.  嘗試登入或操作

**預期結果**：
-   ⚠️ 應降級到記憶體快取或直接查詢資料庫
-   ✅ 功能仍可正常運作（效能降低）
-   ⚠️ 記錄警告日誌

#### 10.5 UI/UX Failure Paths（UI/UX 失敗路徑）

##### 10.5.1 重複的 Claim Name

**測試步驟**：
1.  在 Admin Claims UI 建立 claim，Name = `email`
2.  嘗試建立另一個 Name = `email` 的 claim

**預期結果**：
-   ❌ 應顯示驗證錯誤
-   ❌ 錯誤訊息：「Claim name 已存在」
-   ✅ 表單不應提交
-   ✅ 使用者可修正錯誤並重試

##### 10.5.2 映射到不存在的 Claim

**測試步驟**：
1.  建立 scope mapping 並選擇某個 claim
2.  刪除該 claim（但不刪除 mapping）
3.  嘗試請求該 scope

**預期結果**：
-   ⚠️ Token 仍應發放
-   ⚠️ 忽略無效的 mapping
-   ⚠️ 記錄警告日誌
-   🔧 **建議**：刪除 claim 時應級聯刪除或警告相關 mappings

##### 10.5.3 刪除已映射的 Claim

**測試步驟**：
1.  建立 claim 並映射到多個 scopes
2.  嘗試刪除該 claim

**預期選項**：
-   **選項 A（嚴格）**：阻止刪除，顯示錯誤訊息：「此 claim 正被 X 個 scopes 使用」
-   **選項 B（級聯）**：刪除 claim 並同時刪除所有 mappings（需確認）
-   **選項 C（軟刪除）**：標記為已刪除但保留資料

**目前實作**：需檢查並實作適當的保護機制

##### 10.5.4 無效的 UserPropertyPath 格式

**測試步驟**：
1.  建立 claim，UserPropertyPath = `User..Email` 或 `.Email` 或其他無效格式
2.  映射到 scope 並登入

**預期結果**：
-   ✅ 表單驗證應在輸入時檢查格式
-   ⚠️ 如果繞過驗證，後端應安全處理
-   ⚠️ 記錄警告並返回 null

**建議驗證規則**：
-   只允許 `a-zA-Z0-9._` 字元
-   不能以 `.` 開頭或結尾
-   不能有連續的 `..`
-   長度限制（如最多 200 字元）

### 11. 測試優先順序

| 優先級 | 說明 |
|--------|------|
| 🔴 高 | 必須測試 |
| 🟡 中 | 應該測試 |
| 🟢 低 | 建議測試 |

**🔴 高優先級（必須測試）**
1.  ✅ 使用者拒絕授權
2.  ✅ 無效 Client ID / Redirect URI（安全性）
3.  ✅ Token 過期驗證
4.  ✅ 資料庫連線失敗處理

**🟡 中優先級（應該測試）**
5.  ⚠️ PKCE 驗證失敗
6.  ⚠️ Scope-mapped claims 邊緣情況
7.  ⚠️ Concurrency conflicts

**🟢 低優先級（建議測試）**
8.  ⚙️ UI 驗證錯誤訊息
9.  ⚙️ Cache 降級處理

### 12. 自動化測試實作建議

#### E2E 失敗測試範例

```typescript
// See docs/examples/development_guide_e2e_failure_test_example.ts.example
```

#### Unit Test 範例

```csharp
// See docs/examples/development_guide_unit_test_example.cs.example
```

### 13. 日誌監控建議

#### 應記錄的關鍵錯誤

1.  **安全事件**：
    -   無效的 client_id 或 redirect_uri
    -   Token 簽章驗證失敗
    -   異常的授權請求模式

2.  **業務邏輯錯誤**：
    -   Property path 解析失敗
    -   Scope mapping 找不到 claim
    -   資料庫操作失敗

3.  **基礎設施問題**：
    -   資料庫連線失敗
    -   Cache 服務不可用
    -   外部 API 呼叫失敗

#### 日誌等級指引

-   **Critical**: 應用無法繼續運行（資料庫完全無法連線）
-   **Error**: 操作失敗但應用可繼續（單一 token 發放失敗）
-   **Warning**: 預期外情況但已處理（property path 不存在）
-   **Information**: 正常業務事件（使用者登入、授權）

### 14. 開發建議

1.  **保持 Vite dev server 運行**：避免頻繁重啟，HMR（熱模組替換）會自動重新加載修改
2.  **使用獨立終端機**：分別運行 IdP 和 Vite，方便查看各自的 log
3.  **定期清理進程**：測試結束後執行 `taskkill` 避免殘留進程
4.  **檢查語系資源檔**：如果新增語系，記得在 `Resources/` 目錄添加對應的 `.resx` 檔案

---

## 🛠️ 技術堆疊

### Backend

-   **Framework**: ASP.NET Core .NET 9
-   **Database**: PostgreSQL 17
-   **ORM**: Entity Framework Core 9
-   **Authentication**: OpenIddict 6.x
-   **Authorization**: Role-based (`Admin`, `User`)
-   **Testing**: xUnit, Moq

### Frontend

-   **Build Tool**: Vite 5.4.21
-   **Framework**: Vue.js 3.5.13 (Composition API)
-   **Styling**: Tailwind CSS 3.4.17
-   **Layout**: Bootstrap 5.3.2 (CDN)
-   **Icons**: Bootstrap Icons 1.11.1
-   **Testing**: Playwright (E2E)

### Development

-   **IDE**: Visual Studio Code / Rider
-   **Version Control**: Git (Conventional Commits)
-   **Containerization**: Docker (PostgreSQL)
-   **API Testing**: Swagger UI

---

## Hybrid 架構模式

### 檔案結構範例

以 **Users Management** 為例：

```text
// See docs/examples/development_guide_hybrid_architecture_file_structure.txt.example
```

### 1. Razor Page 範本

**`Pages/Admin/Users.cshtml`**

```cshtml
// See docs/examples/development_guide_razor_page_template.cshtml.example
```

**`Pages/Admin/Users.cshtml.cs`**

```csharp
// See docs/examples/development_guide_razor_page_model_template.cs.example
```

### 2. Vue SPA 入口點

**`ClientApp/src/admin/users/style.css`** ⚠️ **必須建立**

```css
// See docs/examples/development_guide_vue_spa_style_css.css.example
```

**`ClientApp/src/admin/users/main.js`** ⚠️ **必須 import style.css**

```javascript
// See docs/examples/development_guide_vue_spa_main_js.js.example
```

### 3. Vue 主組件範本

**`ClientApp/src/admin/users/UsersApp.vue`**

```vue
// See docs/examples/development_guide_vue_main_component_template.vue.example
```

---

## API 實作範本

### 1. DTOs

**`Core.Application/DTOs/UserSummaryDto.cs`** (List 用)

```csharp
// See docs/examples/development_guide_user_summary_dto.cs.example
```

**`Core.Application/DTOs/UserDetailDto.cs`** (詳細資料用)

```csharp
// See docs/examples/development_guide_user_detail_dto.cs.example
```

**`Core.Application/DTOs/CreateUserDto.cs`** (建立用)

```csharp
// See docs/examples/development_guide_create_user_dto.cs.example
```

### 2. Service Interface

**`Core.Application/IUserManagementService.cs`**

```csharp
// See docs/examples/development_guide_user_management_service_interface.cs.example
```

### 3. Service Implementation

**`Infrastructure/Services/UserManagementService.cs`**

```csharp
// See docs/examples/development_guide_user_management_service_implementation.cs.example
```

### 4. API Controller

**`Web.IdP/Api/Admin/UsersController.cs`**

```csharp
// See docs/examples/development_guide_users_controller.cs.example
```

---

## UI 實作範本

### Vue 組件範例

#### 1. List Component

**`UserList.vue`**

```vue
// See docs/examples/development_guide_user_list_component.vue.example
```

#### 2. Form Component

**`UserFormModal.vue`**

```vue
// See docs/examples/development_guide_user_form_modal_component.vue.example
```

---

## UI 間距規範

> Phase 4.7 引入的 **統一 Spacing Scale**，協助 Admin 頁面達成一致視覺節奏。採語義化輔助 class，不強制覆蓋既有 Tailwind 用法。

### 間距刻度 (Scale)

| 名稱 | 值 (rem) | 建議用途 |
|------|---------|----------|
| xs   | 0.25    | 緊密圖示、徽章間距 |
| sm   | 0.5     | 紧密表單、標籤 |
| md   | 0.75    | 一般表單欄位垂直間距 |
| lg   | 1.0     | 卡片內邊距、分組分隔 |
| xl   | 1.5     | 區塊段落、模態主要分區 |
| xxl  | 2.0     | 稀疏大分隔 (謹慎使用) |

### 語義化 Class 來源

檔案：`ClientApp/src/admin/shared/spacing.css`

| Class | 說明 |
|-------|------|
| `.space-card` / `-tight` / `-wide` | 卡片容器 padding 標準化 |
| `.space-form-group` / `-tight` / `-wide` | 表單欄位群組垂直間距 |
| `.space-modal-body`, `.space-modal-footer` | 模態內容/底部一致化 |
| `.space-table-cell` / `-tight` / `-wide` | 表格儲存格 padding 範圍 |
| `.space-section` / `-tight` | 區塊垂直分隔 |
| `.space-stack-*` (`xs&#124;sm&#124;md&#124;lg&#124;xl`) | 同層兄弟元素縱向節奏 (`> * + *`) |

### 使用範例

```vue
// See docs/examples/development_guide_ui_spacing_example.vue.example
```

### 採用策略

1.  漸進式：新頁/新組件優先使用 `.space-*`。
2.  不強制重構：舊組件逐步替換裸露的 `p-* mb-*`。
3.  模態統一：Body → `.space-modal-body`；Footer → `.space-modal-footer`。
4.  表格列高度：標準 `.space-table-cell`；密集列表用 `-tight`。
5.  垂直節奏：複數欄位群組使用 `.space-stack-md` 取代多個 `mt-*`。

### 驗證清單

-   各頁卡片/模態/表單是否使用語義化間距 class
-   不混用多種 px/py/margin magic numbers
-   表格列高度在 Users / Roles / Clients / Scopes / Claims 一致
-   手機與桌面密度合理 (tight 不犧牲可用性)

### 後續擴充可能

-   Grid gap 語義化 class
-   以 PostCSS 產生 spacing utilities
-   與 E2E 視覺驗證（快照 diff）整合

---

## 常見陷阱

### 1. ❌ 忘記 import Tailwind CSS

**症狀：** 整個排版跑掉，Vue 組件沒有樣式

**原因：** 沒有在 `main.js` 中 import `'./style.css'`

**解決：**

```javascript
// See docs/examples/development_guide_tailwind_import_pitfall.js.example
```

### 2. ❌ 重複執行 `npm run dev`

**症狀：** Port 衝突錯誤

**原因：** Vite dev server 已經在運行

**解決：**

```bash
// See docs/examples/development_guide_npm_run_dev_pitfall.bash.example
```

### 3. ❌ 在開發時執行 `npm run build`

**症狀：** 開發流程中斷，HMR 失效

**原因：** Build 是用於生產環境

**解決：** 開發時只用 `npm run dev`，不要執行 build

### 4. ❌ API 路徑錯誤

**症狀：** 404 Not Found

**原因：** API endpoint 路徑不正確

**解決：** 確認 controller route: `[Route("api/admin/users")]`

### 5. ❌ 忘記 `[Authorize]` 屬性

**症狀：** 未授權用戶可以訪問 admin 功能

**原因：** Razor Page 或 API Controller 沒有加授權檢查

**解決：**

```csharp
// See docs/examples/development_guide_authorize_attribute_pitfall.cs.example
```

### 6. ❌ DTO Validation 不完整

**症狀：** 無效資料進入資料庫

**原因：** 缺少 `[Required]`, `[EmailAddress]` 等驗證屬性

**解決：**

```csharp
// See docs/examples/development_guide_dto_validation_pitfall.cs.example
```

### 7. ❌ 未處理錯誤

**症狀：** 500 Internal Server Error，沒有錯誤訊息

**原因：** API Controller 沒有 try-catch

**解決：**

```csharp
// See docs/examples/development_guide_error_handling_pitfall.cs.example
```

---

## 參考資料

-   **完整需求：** `idp_req_details.md`
-   **專案進度：** `PROJECT_STATUS.md`
-   **架構決策：** `ARCHITECTURE.md`
-   **未來增強：** `FEATURES.md`

---

**記住：遵循這些範本和最佳實踐，可以確保程式碼品質和一致性！** 🚀
