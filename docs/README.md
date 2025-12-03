# 📚 HybridIdP 文件指南

> 本目錄包含 HybridIdP 專案的所有文件。本指南幫助你快速找到需要的資訊。

## 🎯 快速導航

### 新 Session 開始時

**第一步：閱讀 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)**
- 📖 **用途：** 工作流程總覽、開發規範、測試指南
- 🎯 **適合：** 新 session、不熟悉專案的開發者
- ⏱️ **閱讀時間：** 10-15 分鐘
- 📌 **必讀理由：** 了解如何使用其他文件，避免迷失方向

### 開始開發前

**第二步：查看 [`PROJECT_PROGRESS.md`](./PROJECT_PROGRESS.md) 與 [`TODOS.md`](./TODOS.md)**
- 📖 **用途：** 待辦事項、下一步計畫與活躍 backlog
- 🎯 **適合：** 確認當前任務、規劃下一步、了解近期進度
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **更新頻率：** `TODOS.md` 隨 sprint 更新，`PROJECT_PROGRESS.md` 用於里程碑摘要

注意：專案已將大檔拆分以利維護與查閱。最新進度摘要請參見 `docs/PROJECT_PROGRESS.md`，各 Phase 的詳細說明已拆分至 `docs/phase-*.md`（例如 `docs/phase-5-security-i18n-consent.md`）。如需深入內容，請由 `PROJECT_PROGRESS.md` 點入對應 Phase 的檔案查閱。

**第三步：參考 [`ARCHITECTURE_CONSOLIDATED.md`](./ARCHITECTURE_CONSOLIDATED.md)**
- 📖 **用途：** 綜合架構指引與設計決策摘要
- 🎯 **適合：** 實作 API、UI、理解系統設計時快速查閱
- ⏱️ **閱讀時間：** 按需查閱（不需全部閱讀）
- 📌 **包含內容：**
  - Hybrid 架構說明
  - 技術棧與整合要點
  - 安全架構摘要
  - 運維與監控要點

### 測試功能時
**第四步：參考 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的測試指南**
- 📖 **用途：** 開發和測試指南
- 🎯 **適合：** 啟動環境、執行測試
- 📌 **包含內容：**
  - 環境啟動步驟
  - 測試流程
  - 常見錯誤排除（⚠️ Vite 管理警告）

### E2E 測試（Playwright）

專案包含 Playwright E2E 測試，並提供 `e2e/README.md`，內含完整使用說明與 Postgres 專用 helper（`scripts/run-e2e-postgres.ps1`）。推薦在本機使用 Postgres E2E runner（會執行容器、遷移、Admin-API seeding 以及 Playwright 測試）：
```

### 查看功能與未來計畫

- 📖 **用途：** 已實作功能概覽與未來增強摘要
- 🎯 **適合：** 了解特定功能（如 Turnstile、MFA）的快速參考
- ⏱️ **閱讀時間：** 3-5 分鐘
- 📌 **更新頻率：** 新功能實作後更新

### 設定資料庫

**查看 [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md)**
- 📖 **用途：** 資料庫設定、切換、Migration、Production 部署
- 🎯 **適合：** 設定開發環境、切換資料庫、部署到正式環境
- ⏱️ **閱讀時間：** 5-10 分鐘
- 📌 **包含內容：**
  - SQL Server / PostgreSQL 快速開始
  - 環境變數與 Secrets 管理
  - Migration 管理指南
  - Production 部署最佳實踐
  - 故障排除

---

## 📋 文件分類

### 🌟 核心文件（高頻使用）

| 文件 | 用途 | 更新頻率 | Token 大小 |
|------|------|----------|-----------|
| [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) | 開發工作流程、規範、測試指南 | 穩定 | ~2000 行 |
| [`PROJECT_PROGRESS.md`](./PROJECT_PROGRESS.md) | 專案進度、待辦事項、已完成摘要 | 每 Phase 更新 | ~1000 行 |
| [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md) | 資料庫設定、Migration、部署 | 穩定 | ~600 行 |

**總 Token 消耗（核心文件）：** ~3600 行
- ✅ 按需查閱，減少 50-70% token 消耗

### 📚 參考文件（按需查閱）

| 文件 | 用途 | 何時查閱 |
|------|--------|----------|
| [`idp_req_details.md`](./archive/historical/idp_req_details.md) | 完整需求文件 | 需要細節規格時 |
| [`ARCHITECTURE.md`](./ARCHITECTURE.md) | 架構決策與技術棧詳解 | 了解架構原因時 |
| [`FEATURES_AND_CAPABILITIES.md`](./FEATURES_AND_CAPABILITIES.md) | 功能細節與未來增強 | 實作特定功能時 |
| [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md) | 資料庫設定與部署 | 設定環境、切換資料庫、Production 部署時 |
| `docs/examples/` | 程式碼範例 | 實作時參考 |

---

## 🔄 文件更新流程

### 完成一個 Phase 後

**步驟 1：更新 [`PROJECT_PROGRESS.md`](./PROJECT_PROGRESS.md)**
```markdown
- [x] Phase 4.5: Role Management UI  # 標記為完成
```

**步驟 2：更新 [`PROJECT_PROGRESS.md`](./PROJECT_PROGRESS.md) 中的已完成摘要**
```markdown
## Phase 4.5: Role Management UI ✅

**完成時間：** 2025-11-XX

**功能摘要：**
- Role CRUD 完整實作
- Permission 分配管理
- ...（3-5 行摘要）

**API Endpoints:**
- GET /api/admin/roles
- ...

**驗證結果：**
- ✅ ...
```

**步驟 3：Commit**
```bash
git add docs/PROJECT_PROGRESS.md
git commit -m "docs: Update progress - Phase 4.5 completed"
```

### 發現新的最佳實踐時

**更新 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的相關章節**
- 新增範本
- 更新常見陷阱
- 提供範例程式碼

### 工作流程改變時

**更新 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的工作流程章節**
- 修改開發流程
- 更新 Git 策略
- 調整檢查清單

---

## 💡 使用建議

### 給 AI Agent

**新 Session 啟動時：**
```
請讀取以下文件來了解專案：
1. docs/DEVELOPMENT_GUIDE.md - 工作流程、規範、測試指南
2. docs/PROJECT_STATUS.md - 確認下一步
3. docs/ARCHITECTURE.md - 查閱需要的架構說明
```

**開始實作時：**
```
請參考 docs/DEVELOPMENT_GUIDE.md 中的：
- API 實作範本
- UI 實作範本
- Tailwind CSS 設定（⚠️ 重要）
```

**測試時：**
```
請參考 docs/DEVELOPMENT_GUIDE.md 執行測試
注意 Vite 管理警告！
```

### 給開發者

1. **首次接觸專案：** 按順序閱讀 `DEVELOPMENT_GUIDE.md` → `ARCHITECTURE.md` → `PROJECT_PROGRESS.md`
2. **日常開發：** 只需查看 PROJECT_STATUS.md 和 DEVELOPMENT_GUIDE.md
3. **需要細節：** 再查閱 `docs/archive/historical/idp_req_details.md` 或 `FEATURES_AND_CAPABILITIES.md` 相關 Phase

---

## 🧪 Remote Browser (MCP Playwright) 測試流程

> 針對登入、同意(Consent) 與 invalid_scope 等互動式流程的遠端瀏覽器自動化說明。此流程假設你使用外部 MCP Playwright 代理（或等效的遠端瀏覽器服務）。

### 前置環境

- 已安裝 .NET 10 SDK (本專案目標為 .NET 10)
- 已啟動必要容器：預設為 **MSSQL (mssql-service)** 與 **Redis (redis-service)**。若你要使用 PostgreSQL 作為 provider，請啟動 **postgres-service** 與 Redis。
- 管理端與 IdP 資料表已遷移（如需：`dotnet ef database update` 對相關專案執行）

### 啟動基礎服務順序

```powershell
# 1. 啟動資料庫與快取 (預設: MSSQL)
# 使用 MSSQL (預設)
docker compose up -d mssql-service redis-service

# 或者使用 PostgreSQL (Postgres E2E runner 將使用 postgres-service)
docker compose up -d postgres-service redis-service

# 2. 啟動 Identity Provider (HTTPS)
dotnet run --project .\Web.IdP\Web.IdP.csproj --launch-profile https

# 3. 啟動 TestClient（用於授權與 invalid_scope 測試）
dotnet run --project .\TestClient\TestClient.csproj
```

### 常用服務 URL

- IdP 授權端點（OpenIddict）：`https://localhost:7035/connect/authorize`
- Consent 頁面：授權流程中自動重導（`/Account/Consent` 之類路徑）
- TestClient 起始頁：`https://localhost:xxxx/`（執行後主控台會列出實際埠）

### Invalid Scope 測試

- 啟動 TestClient 後使用提供的 "Invalid Scopes" 導覽連結（或特定測試路由）
- 流程：在 authorization request 中注入不存在的 scope → IdP 返回 `invalid_scope` 錯誤 → MCP 代理截取結果

### 遠端 MCP Playwright 測試要點

- 等待頁面載入後再執行語言切換（避免 i18n lazy load race）
- 登入流程：填入測試帳號（依專案 `setup-test-user.ps1` 建立）後提交 → 截取同意頁 scopes & icon 標記
- 語言切換：在登入頁與同意頁各執行一次（驗證 zh-TW / en-US JSON 均可解析）
- 捕捉錯誤：若遇到 locale JSON parse error，記錄 `console.error` 與 network 請求回應 body 以利追蹤

### 建議的 MCP 腳本步驟摘要

1. `goto(TestClient start URL)`
2. `click('Login / Authorize')`
3. 等待重導至 IdP 授權/登入頁
4. 切換語言（繁中 → 英文）並擷取標題文字
5. 輸入使用者名稱/密碼並登入
6. 在同意頁擷取 scope 列表（文字、圖示、已選狀態）
7. 切換語言再次擷取 scope 區塊（驗證 i18n）
8. 針對 invalid scope 路由重複 1-7 以取得 `invalid_scope` 錯誤視覺化/JSON 回應

### 可能的環境變數（視自動化服務需求）

| 名稱 | 用途 | 範例值 |
|------|------|--------|
| `IDP_BASE_URL` | 指向 IdP 基礎 URL | `https://localhost:7035` |
| `TESTCLIENT_BASE_URL` | 指向 TestClient URL | `https://localhost:7001` |
| `MCP_TIMEOUT_MS` | 最大等待時間 | `30000` |

> 若遠端測試代理需要額外認證或 WebSocket 連線參數，請在各自的環境設定文件補充，本節只涵蓋專案自身啟動與流程。

### 疑難排解

- IdP 啟動失敗：檢查 HTTPS 開發憑證或埠衝突（停止殘留 dotnet 程序）。
- 語言切換無效：確認 `en-US.json` / `zh-TW.json` 未出現重複或語法錯誤；使用瀏覽器 DevTools Network 檢查回應是否 200。
- invalid_scope 未觸發：確認 TestClient 注入路由是否仍存在於 `Program.cs` 事件管線中。

### 後續擴充建議

- 加入自動擷取同意頁 scope 計數與 `openid` 必選警示（已在 UI/i18n 完成，可在腳本驗證該字串存在）。
- 將 invalid_scope 失敗回應擴充為 JSON 結構快照供報告產生。

---

## 📊 Token 效率對比

### 之前（單一大文件）

```text
每次 session 必須讀取:
- idp_req_details.md (1284 行)
- dev_testing_guide.md (200 行)
= 約 1484 行

Token 消耗: ~1500 行 × 每次
```

### 現在（模組化文件）

```text
新 session 閱讀:
- DEVELOPMENT_GUIDE.md (約 1000 行)
- PROJECT_STATUS.md (約 500 行)
= 約 1500 行

開發時查閱:
- DEVELOPMENT_GUIDE.md (按需查閱，不需全讀)
- 平均查閱 200 行

Token 消耗: ~700 行 × 每次
節省: ~53%
```

**實際節省更多：**

- 熟悉專案後，只需 PROJECT_STATUS.md (500 行)
- 查閱範本時，只看需要的 section
- 節省可達 **60-70%**

---

## ⚠️ 重要提醒

### 文件同步

- ✅ **DO:** 完成 Phase 立即更新 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)
- ❌ **DON'T:** 累積多個 Phase 再一次更新

### 保持簡潔

- ✅ **DO:** [`PROJECT_STATUS.md`](./PROJECT_STATUS.md) 中的已完成摘要使用 3-5 行摘要
- ❌ **DON'T:** 複製完整程式碼到文件中

### Tailwind CSS 警告

- ⚠️ **每個新 Vue SPA 必須：**
  1. 創建 `style.css`
  2. `main.js` 中 `import './style.css'`
  3. 參考 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 範本

### Vite 管理

- ⚠️ **絕對不要：**
  1. 重複執行 `npm run dev`
  2. 開發時執行 `npm run build`
  3. 詳見 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) 中的 Vite 管理章節

---

## 🆘 找不到資訊？

### 檢查順序

1. **[`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)** - 流程、實作、測試問題？
2. **[`ARCHITECTURE.md`](./ARCHITECTURE.md)** - 架構、技術棧問題？
3. **[`FEATURES_AND_CAPABILITIES.md`](./FEATURES_AND_CAPABILITIES.md)** - 特定功能問題？
4. **[`idp_req_details.md`](./archive/historical/idp_req_details.md)** - 需求細節？

### 常見問題

**Q: 下一步要做什麼？**
→ 查看 [`PROJECT_STATUS.md`](./PROJECT_STATUS.md)

**Q: 怎麼實作 API？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > API 實作範本

**Q: 如何切換資料庫 (SQL Server/PostgreSQL)？**
→ 查看 [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md) > 本地開發環境

**Q: 如何部署到 Production？**
→ 查看 [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md) > Production 部署

**Q: E2E 測試失敗 (TestClient 認證錯誤)？**
→ 查看 [`DATABASE_CONFIGURATION.md`](./DATABASE_CONFIGURATION.md) > 測試資料設定

**Q: Tailwind CSS 不工作？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > 常見陷阱 #1

**Q: Vite 出錯？**
→ 查看 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md) > 最常見錯誤

**Q: 需要完整需求？**
→ 查看 [`idp_req_details.md`](./idp_req_details.md) 對應 Phase

---

**建立時間：** 2025-11-04  
**維護者：** HybridIdP Team  
**版本：** 1.0

**記住：先讀 [`DEVELOPMENT_GUIDE.md`](./DEVELOPMENT_GUIDE.md)，就知道該讀什麼！** 🚀
