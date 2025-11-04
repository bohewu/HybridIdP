# HybridIdP 開發工作流程指南

> 🎯 **新 Session 必讀** - 本文件說明如何使用專案文件和開發流程

## 📚 文件結構總覽

```
docs/
├── WORKFLOW.md                    # 👈 你在這裡 - 工作流程指南
├── implementation_guidelines.md   # 📋 開發規範和最佳實踐
├── progress_completed.md          # ✅ 已完成功能摘要
├── progress_todo.md               # 📝 待辦事項和下一步
├── dev_testing_guide.md           # 🧪 開發測試指南
├── idp_req_details.md             # 📚 完整需求文件（參考用）
└── [其他文件...]                  # 特定主題文件
```

## 🚀 快速開始（新 Session）

### 1. 閱讀順序

```
第一次進入專案:
1. WORKFLOW.md (本文件)          - 了解工作流程
2. implementation_guidelines.md   - 學習開發規範
3. progress_completed.md          - 了解已完成的部分
4. progress_todo.md               - 確認下一步要做什麼
5. dev_testing_guide.md           - 測試前必讀

繼續開發時:
1. progress_todo.md               - 確認當前任務
2. implementation_guidelines.md   - 查閱實作範本
3. dev_testing_guide.md           - 測試新功能
```

### 2. 環境啟動檢查清單

```bash
# ✅ 檢查清單
□ PostgreSQL 資料庫運行中 (docker compose up -d db-service)
□ IdP Backend 運行中 (dotnet run --launch-profile https in Web.IdP/)
□ Vite Dev Server 運行中 (npm run dev in Web.IdP/ClientApp/)
  
  ⚠️ 重要：Vite 只能啟動一次！不要重複執行 npm run dev
  ⚠️ 開發時絕對不要執行 npm run build
```

### 3. Git 狀態確認

```bash
# 查看當前狀態
git status

# 查看最近提交
git log --oneline -5
```

## 🎯 Git Commit 策略：Small Steps (Option A)

### 核心原則

**Philosophy:** Commit early, commit often - 每個邏輯單元一個 commit

### 實作順序

```
Phase X.Y: Feature Name
├── Step 1: API - DTOs (commit)
├── Step 2: API - GET endpoint + tests (commit)
├── Step 3: API - POST endpoint + validation + tests (commit)
├── Step 4: API - PUT endpoint + tests (commit)
├── Step 5: API - DELETE endpoint + tests (commit)
├── Step 6: UI - Razor Page + Vue scaffolding + Tailwind CSS (commit)
├── Step 7: UI - List component with API integration (commit)
├── Step 8: UI - Create form component (commit)
├── Step 9: UI - Edit form component (commit)
├── Step 10: UI - Delete confirmation (commit)
└── Step 11: E2E Testing & Verification (commit)
```

### Commit Message 格式

```text
<type>(<scope>): <subject>

[optional body]
[optional footer]
```

**Types:**
- `feat`: 新功能
- `fix`: Bug 修復
- `test`: 測試
- `docs`: 文件
- `refactor`: 重構
- `style`: 格式化
- `chore`: 建置工具

**Scopes:**
- `api`: Backend API
- `ui`: Frontend UI
- `auth`: 認證/授權
- `db`: 資料庫
- `test`: 測試

**範例:**

```bash
feat(api): Add RoleSummaryDto for role list endpoint
feat(api): Implement GET /api/admin/roles with pagination
test(api): Add unit tests for role creation validation
feat(ui): Add Roles.cshtml with admin authorization
feat(ui): Implement RoleList component with table display
```

### 每個 Commit 前的檢查清單

- ✅ 程式碼編譯無錯誤
- ✅ 相關測試通過
- ✅ 應用程式可正常運行
- ✅ 沒有破壞現有功能

## 📋 開發工作流程

### Step-by-Step 流程

```
1. 查看 progress_todo.md
   ↓
2. 確認當前任務（例如：Phase 4.5 - Role Management UI）
   ↓
3. 閱讀 implementation_guidelines.md 中的相關範本
   ↓
4. 開始實作第一個 atomic unit（例如：DTOs）
   ↓
5. 測試變更（unit tests / manual testing）
   ↓
6. Commit with conventional format
   ↓
7. 更新 progress_todo.md（勾選完成項目）
   ↓
8. 如果 sub-phase 完成，更新 progress_completed.md
   ↓
9. 請求批准 → 繼續下一個 unit
```

### API 優先，後端先行

**規則：永遠先完成並測試 API，再開始 UI**

```
❌ 錯誤順序：
UI → API → 回頭修 UI

✅ 正確順序：
API + Tests → UI Layout → UI CRUD
```

### UI 分層實作

```
第一層：Layout/Scaffolding
  - Razor Page (.cshtml)
  - Vue SPA mount point
  - ⚠️ Tailwind CSS setup (style.css + import)

第二層：Data Display
  - List/Table components
  - API integration
  - Pagination, search, filters

第三層：CRUD Operations (一次一個)
  - Create form
  - Edit form
  - Delete confirmation
```

## ⚠️ 關鍵注意事項

### 🔴 Tailwind CSS Setup - 每個 Vue SPA 必須

**每次建立新的 Vue SPA（例如：users/, roles/, clients/）時：**

1. **創建 `style.css`**

```css
/* src/admin/[feature]/style.css */
@tailwind base;
@tailwind components;
@tailwind utilities;
```

2. **在 `main.js` 中 import**

```javascript
// src/admin/[feature]/main.js
import { createApp } from 'vue';
import './style.css';  // ⚠️ 必須加這行！
import App from './App.vue';

createApp(App).mount('#app');
```

3. **驗證：** 瀏覽器開發工具 Console 應該看到 `[vite] connected`，且 Tailwind 樣式正常運作

**❌ 如果忘記 import style.css → 整個排版會跑掉！**

### 🔴 Vite Dev Server 管理

**最常見錯誤：**

1. **❌ 不要重複執行 `npm run dev`**
   - Vite 已經在背景運行時，再執行會導致 port 衝突
   - 檢查方法：瀏覽器訪問 `http://localhost:5173` 看是否運行中

2. **❌ 開發時絕對不要執行 `npm run build`**
   - Build 是用於生產環境
   - 開發時只需要 `npm run dev`
   - Build 會清空 dist/ 並影響開發流程

3. **✅ 正確做法：**
   - 第一次啟動：`npm run dev`
   - 後續開發：保持 Vite 運行，不要關閉
   - 如果需要重啟：先 Ctrl+C 停止，再 `npm run dev`

## 📝 文件更新規則

### 完成一個 Sub-Phase 後

1. **更新 `progress_todo.md`**
   - 將完成的項目從 `[ ]` 改為 `[x]`
   - 如果整個 Phase 完成，移除該 section

2. **更新 `progress_completed.md`**
   - 新增完成的 Phase 摘要（3-5 行）
   - 包含：功能描述、API endpoints、UI routes、測試狀態

3. **Commit 文件更新**

```bash
git add docs/progress_*.md
git commit -m "docs: Update progress - Phase X.Y completed"
```

## 🧪 測試流程

詳見 `dev_testing_guide.md`，摘要：

```
1. Backend 測試：
   - Unit tests: dotnet test
   - API 測試：Swagger UI (https://localhost:7035/swagger)

2. Frontend 測試：
   - 手動測試：透過瀏覽器
   - E2E 測試：Playwright MCP (browser_snapshot, browser_click, etc.)

3. 整合測試：
   - 完整 CRUD 流程
   - 權限驗證
   - 錯誤處理
```

## 🔄 開發循環範例

**假設任務：實作 Phase 4.5 - Role Management UI**

```bash
# 1. 確認任務
# 讀取 progress_todo.md，確認 Phase 4.5 是下一步

# 2. Step 1: DTOs
# - 實作 RoleSummaryDto, RoleDetailDto
# - Commit: feat(api): Add RoleSummaryDto and RoleDetailDto

# 3. Step 2: GET endpoint
# - 實作 GET /api/admin/roles
# - 加 unit tests
# - Commit: feat(api): Implement GET /api/admin/roles with pagination
# - Commit: test(api): Add unit tests for role list endpoint

# 4. Step 3-5: POST, PUT, DELETE endpoints
# - 每個 endpoint 一個 commit
# - 每個都包含 validation 和 tests

# 5. Step 6: UI Scaffolding
# - 創建 Roles.cshtml
# - 創建 src/admin/roles/style.css ⚠️
# - 創建 src/admin/roles/main.js (import style.css) ⚠️
# - Commit: feat(ui): Add Roles.cshtml with admin authorization
# - Commit: feat(ui): Setup Vue SPA for role management with Tailwind

# 6. Step 7: List component
# - 實作 RoleList.vue
# - API integration
# - Commit: feat(ui): Implement RoleList component with table display

# 7. Step 8-10: CRUD components
# - CreateRole.vue → commit
# - EditRole.vue → commit
# - DeleteRole confirmation → commit

# 8. Step 11: E2E Testing
# - Playwright MCP 測試
# - Commit: test(e2e): Add role management E2E tests

# 9. 更新文件
# - progress_todo.md: [x] Phase 4.5
# - progress_completed.md: 新增 Phase 4.5 摘要
# - Commit: docs: Update progress - Phase 4.5 completed
```

## 📖 進階參考

- **完整需求：** `idp_req_details.md` (只在需要詳細規格時查閱)
- **架構決策：** `architecture_hybrid_bootstrap_vue.md`
- **MFA 需求：** `idp_mfa_req.md`
- **Turnstile 整合：** `turnstile_integration.md`

## 💡 最佳實踐

1. **小步前進** - 不要一次寫太多程式碼才 commit
2. **測試驅動** - 先寫測試，確保 API 正確再做 UI
3. **文件同步** - 完成功能立即更新 progress 文件
4. **遵循範本** - 使用 `implementation_guidelines.md` 中的範本
5. **保持整潔** - 每個 commit 都應該是可運行的狀態

## 🆘 遇到問題

1. **樣式跑掉** → 檢查是否 import './style.css'
2. **Vite 錯誤** → 檢查是否重複執行 npm run dev
3. **API 404** → 檢查 IdP Backend 是否運行
4. **資料庫錯誤** → 檢查 PostgreSQL Docker container 狀態

---

**記住：這個文件是你的起點。每次新 session 先讀這個，就知道該做什麼！** 🚀
